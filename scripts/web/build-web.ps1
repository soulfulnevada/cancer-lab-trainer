param(
    [string]$ReleaseId = (Get-Date -Format 'yyyyMMdd-HHmmss'),
    [switch]$Debug,
    [string]$GodotPath = $env:CANCER_LAB_GODOT,
    [string]$WebTemplateDebug = $env:CANCER_LAB_WEB_TEMPLATE_DEBUG,
    [string]$WebTemplateRelease = $env:CANCER_LAB_WEB_TEMPLATE_RELEASE
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $command = Get-Command godot -ErrorAction SilentlyContinue
    if ($command) { $GodotPath = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath)) {
    throw 'Provide -GodotPath or set CANCER_LAB_GODOT to a standard Godot 4.7.2 executable.'
}
$godot = (Resolve-Path -LiteralPath $GodotPath).Path
$version = (& $godot --version).Trim()
if ($version -notmatch '^4\.7\.2\.stable\.official\.' -or $version -match '(?i)mono') {
    throw "Godot must be the standard 4.7.2 editor, not Mono. Reported: $version"
}
if ($ReleaseId -notmatch '^[a-z0-9][a-z0-9-]{0,63}$') { throw 'ReleaseId must be a lowercase hyphenated identifier.' }

$releaseRoot = [IO.Path]::GetFullPath((Join-Path $root 'dist\web'))
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $releaseRoot $ReleaseId))
if (-not $releaseDirectory.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output escapes dist/web.' }
if (Test-Path -LiteralPath $releaseDirectory) { throw "Refusing to overwrite immutable release output $releaseDirectory." }
$archive = "$releaseDirectory.zip"
if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite immutable archive $archive." }

$definitions = @('scenario.v1.json','rules.v1.json','sources.v1.json','transformation-scenario.v1.json','transformation-lessons.v1.json','transformation-cases.v1.json','transformation-rubric.v1.json','transformation-sources.v1.json')
foreach ($name in $definitions) {
    $canonical = Get-FileHash -LiteralPath (Join-Path $root "data\$name") -Algorithm SHA256
    $webCopy = Get-FileHash -LiteralPath (Join-Path $root "web\data\$name") -Algorithm SHA256
    if ($canonical.Hash -ne $webCopy.Hash) { throw "Web definition hash does not match canonical data for $name." }
}

New-Item -ItemType Directory -Path $releaseDirectory | Out-Null
$buildRoot = Join-Path $root ('.scratch\web-build-' + $ReleaseId)
if (Test-Path -LiteralPath $buildRoot) { throw "Temporary build path already exists: $buildRoot" }
New-Item -ItemType Directory -Path $buildRoot | Out-Null
$stagedProject = Join-Path $buildRoot 'web'
Copy-Item -LiteralPath (Join-Path $root 'web') -Destination $stagedProject -Recurse
$editorCache = Join-Path $stagedProject '.godot'
if (Test-Path -LiteralPath $editorCache) { Remove-Item -LiteralPath $editorCache -Recurse -Force }

$presetPath = Join-Path $stagedProject 'export_presets.cfg'
$preset = Get-Content -LiteralPath $presetPath -Raw
if (($WebTemplateDebug -and -not $WebTemplateRelease) -or (-not $WebTemplateDebug -and $WebTemplateRelease)) {
    throw 'Provide both web templates, or neither to use the installed standard Godot templates.'
}
if ($WebTemplateDebug) {
    foreach ($template in @($WebTemplateDebug, $WebTemplateRelease)) {
        if (-not (Test-Path -LiteralPath $template)) { throw "Web template does not exist: $template" }
    }
    $debugPath = (Resolve-Path -LiteralPath $WebTemplateDebug).Path.Replace('\', '/')
    $releasePath = (Resolve-Path -LiteralPath $WebTemplateRelease).Path.Replace('\', '/')
    $preset = $preset -replace 'custom_template/debug="[^"]*"', ('custom_template/debug="{0}"' -f $debugPath)
    $preset = $preset -replace 'custom_template/release="[^"]*"', ('custom_template/release="{0}"' -f $releasePath)
} else {
    $preset = $preset -replace 'custom_template/debug="[^"]*"', 'custom_template/debug=""'
    $preset = $preset -replace 'custom_template/release="[^"]*"', 'custom_template/release=""'
}
Set-Content -LiteralPath $presetPath -Value $preset -NoNewline

$game = Join-Path $releaseDirectory 'game'
New-Item -ItemType Directory -Path $game | Out-Null
$mode = if ($Debug) { '--export-debug' } else { '--export-release' }
& $godot --headless --path $stagedProject $mode 'Web single-thread compatibility' (Join-Path $game 'index.html')
if ($LASTEXITCODE -ne 0) { throw "Godot web export failed with exit code $LASTEXITCODE." }
Copy-Item -LiteralPath (Join-Path $root 'web\web-template\storage_adapter.js') -Destination (Join-Path $game 'storage_adapter.js') -Force

$inspector = Join-Path $buildRoot 'pck-inspector'
New-Item -ItemType Directory -Path $inspector | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'scripts\web\inspect-pck.gd') -Destination (Join-Path $inspector 'inspect-pck.gd')
Set-Content -LiteralPath (Join-Path $inspector 'project.godot') -Value '[application]`nconfig/name="PCK inspector"'
$rawInventory = Join-Path $inspector 'inventory.json'
& $godot --headless --path $inspector --script inspect-pck.gd -- (Join-Path $game 'index.pck') $rawInventory
if ($LASTEXITCODE -ne 0) { throw "PCK inventory failed with exit code $LASTEXITCODE." }
$inventory = @(Get-Content -LiteralPath $rawInventory -Raw | ConvertFrom-Json | Where-Object { $_ -notin @('res://inspect-pck.gd', 'res://project.godot') })
foreach ($name in $definitions) {
    if ($inventory -notcontains "res://data/$name") { throw "Export PCK is missing canonical definition $name." }
}
if ($inventory | Where-Object { $_ -match '(^|/)tests(/|$)|\.(cs|csproj|sln)$' }) {
    throw 'Export PCK includes tests or desktop/.NET source.'
}
$unexpectedGodotMetadata = $inventory | Where-Object { $_ -match '^res://\.godot/' -and $_ -notmatch '^res://\.godot/(exported/[0-9]+/export-[a-f0-9-]+-WebMain\.scn|global_script_class_cache\.cfg|uid_cache\.bin)$' }
if ($unexpectedGodotMetadata) {
    throw "Export PCK includes unexpected editor metadata: $($unexpectedGodotMetadata -join ', ')"
}
[ordered]@{
    pckSha256 = (Get-FileHash -LiteralPath (Join-Path $game 'index.pck') -Algorithm SHA256).Hash.ToLowerInvariant()
    paths = $inventory
    note = 'The three allowed .godot entries are generated runtime import and UID metadata required by Godot; no source cache, tests, or .NET source is exported.'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'pck-inventory.json')

Copy-Item -LiteralPath (Join-Path $root 'web\site\index.html'), (Join-Path $root 'web\site\site.css'), (Join-Path $root 'web\site\site.js'), (Join-Path $root 'web\site\offline.html') -Destination $releaseDirectory
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'NOTICE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $releaseDirectory
Copy-Item -LiteralPath (Join-Path $root 'scripts\web\public-source-allowlist.json') -Destination (Join-Path $releaseDirectory 'public-source-allowlist.json')
$releaseNotices = Join-Path $releaseDirectory 'docs'
New-Item -ItemType Directory -Path $releaseNotices | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\LICENSE-GODOT.txt'), (Join-Path $root 'docs\COPYRIGHT-GODOT.txt'), (Join-Path $root 'docs\LICENSE-DOTNET.txt'), (Join-Path $root 'docs\NOTICES-DOTNET.txt') -Destination $releaseNotices
$assets = Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse |
    Where-Object { $_.Name -notin @('asset-manifest.json', 'sw.js', 'release-manifest.json') } |
    ForEach-Object { $_.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/') } |
    Sort-Object
$assets | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'asset-manifest.json')
$cacheAssets = @('./', './asset-manifest.json', './release-manifest.json', './sw.js', './offline.html') + ($assets | ForEach-Object { './' + $_ })
$cacheAssets = @($cacheAssets | Select-Object -Unique)
$serviceWorker = @"
const CACHE = "cancer-lab-trainer-web-$ReleaseId";
const ASSETS = $($cacheAssets | ConvertTo-Json -Compress);
async function cacheIsComplete() {
  const cache = await caches.open(CACHE);
  const checks = await Promise.all(ASSETS.map(asset => cache.match(asset)));
  return checks.every(Boolean);
}
async function notifyClients(message) {
  const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
  windows.forEach(window => window.postMessage(message));
}
self.addEventListener("install", event => event.waitUntil((async () => {
  try {
    const cache = await caches.open(CACHE);
    await cache.addAll(ASSETS);
    if (!await cacheIsComplete()) throw new Error("incomplete cache");
    await notifyClients({ kind: "cache-complete", cache: CACHE });
    await self.skipWaiting();
  } catch (error) {
    await notifyClients({ kind: "cache-failed", cache: CACHE });
    throw error;
  }
})()));
self.addEventListener("activate", event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key.startsWith("cancer-lab-trainer-web-") && key !== CACHE).map(key => caches.delete(key)))).then(() => self.clients.claim())));
self.addEventListener("message", event => {
  if (event.data?.kind !== "cache-status") return;
  event.waitUntil(cacheIsComplete().then(complete => {
    const message = { kind: complete ? "cache-complete" : "cache-incomplete", cache: CACHE };
    if (event.ports[0]) event.ports[0].postMessage(message); else notifyClients(message);
  }).catch(() => {
    const message = { kind: "cache-failed", cache: CACHE };
    if (event.ports[0]) event.ports[0].postMessage(message); else notifyClients(message);
  }));
});
self.addEventListener("fetch", event => {
  if (event.request.method !== "GET" || new URL(event.request.url).origin !== self.location.origin) return;
  event.respondWith(caches.match(event.request).then(hit => hit || fetch(event.request).catch(() => caches.match("./offline.html"))));
});
"@
Set-Content -LiteralPath (Join-Path $releaseDirectory 'sw.js') -Value $serviceWorker -NoNewline

$manifest = Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
[ordered]@{
    schemaVersion = 1
    releaseId = $ReleaseId
    godotVersion = $version
    templateMode = if ($WebTemplateDebug) { 'explicit-portable-input' } else { 'installed-standard-template' }
    files = $manifest
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'release-manifest.json')

Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $releaseDirectory) -DestinationPath $archive -CompressionLevel Optimal
(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath "$archive.sha256" -NoNewline

$storageCheck = Join-Path $root 'dist\web\browser-storage-check'
New-Item -ItemType Directory -Force -Path $storageCheck | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'web\web-template\storage_adapter.js') -Destination (Join-Path $storageCheck 'storage_adapter.js') -Force
Copy-Item -LiteralPath (Join-Path $root 'web\tests\storage_adapter_browser.html') -Destination (Join-Path $storageCheck 'index.html') -Force
Write-Host "PASS: built immutable single-thread web candidate $releaseDirectory; inventory has $($inventory.Count) PCK paths and all 8 canonical JSON definitions."
