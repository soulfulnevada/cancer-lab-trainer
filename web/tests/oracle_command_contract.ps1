param(
    [string]$DotnetPath = (Get-Command dotnet -CommandType Application |
        Select-Object -First 1 -ExpandProperty Source)
)

$ErrorActionPreference = 'Stop'
$resolvedDotnet = (Resolve-Path -LiteralPath $DotnetPath).Path
if ((Get-Item -LiteralPath $resolvedDotnet).PSIsContainer) {
    throw 'DotnetPath must identify the dotnet executable, not DOTNET_ROOT.'
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$originalDotnetRoot = $env:DOTNET_ROOT
$originalPath = $env:PATH
$testDotnetRoot = Split-Path -Parent $resolvedDotnet
$duplicateDirectory = Join-Path ([IO.Path]::GetTempPath()) ("cancer-lab-dotnet-command-contract-" + [Guid]::NewGuid().ToString('N'))
$env:DOTNET_ROOT = $testDotnetRoot
$env:PATH = (Split-Path -Parent $resolvedDotnet) + [IO.Path]::PathSeparator + $originalPath

try {
    $pathCommands = Get-Command dotnet -CommandType Application -All | ForEach-Object {
        (Resolve-Path -LiteralPath $_.Source).Path
    }
    if ($resolvedDotnet -notin $pathCommands) {
        throw 'The explicit dotnet executable was not available through PATH while DOTNET_ROOT was a directory.'
    }
    & (Join-Path $repo 'scripts\web\generate-parity-oracle.ps1') -DotnetPath $resolvedDotnet
    if ($LASTEXITCODE -ne 0) {
        throw "Explicit parity oracle command failed with $LASTEXITCODE."
    }
    & (Join-Path $repo 'scripts\web\generate-parity-oracle.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Default PATH-discovery parity oracle command failed with $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Force -Path $duplicateDirectory | Out-Null
    $duplicateDotnet = Join-Path $duplicateDirectory (Split-Path -Leaf $resolvedDotnet)
    Copy-Item -LiteralPath $resolvedDotnet -Destination $duplicateDotnet
    if (-not $IsWindows) {
        & chmod +x $duplicateDotnet
        if ($LASTEXITCODE -ne 0) {
            throw "Could not make duplicate dotnet command executable: $LASTEXITCODE."
        }
    }
    $env:DOTNET_ROOT = ''
    $env:PATH = (Split-Path -Parent $resolvedDotnet) + [IO.Path]::PathSeparator + $duplicateDirectory + [IO.Path]::PathSeparator + $originalPath
    $multipleCommands = Get-Command dotnet -CommandType Application -All
    if ($multipleCommands.Count -lt 2) {
        throw 'Duplicate PATH test did not expose multiple dotnet application commands.'
    }
    $firstPathCommand = (Resolve-Path -LiteralPath ($multipleCommands | Select-Object -First 1 -ExpandProperty Source)).Path
    if ($firstPathCommand -ne $resolvedDotnet) {
        throw 'Duplicate PATH test did not order the real dotnet executable first.'
    }
    & (Join-Path $repo 'scripts\web\generate-parity-oracle.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "Duplicate PATH default parity oracle command failed with $LASTEXITCODE."
    }
} finally {
    if ($null -eq $originalDotnetRoot) {
        Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
    } else {
        $env:DOTNET_ROOT = $originalDotnetRoot
    }
    $env:PATH = $originalPath
    Remove-Item -LiteralPath $duplicateDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS: explicit override and deterministic defaults survive a directory-valued DOTNET_ROOT and duplicate PATH commands.'
