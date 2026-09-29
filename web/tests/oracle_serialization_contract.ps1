param(
    [string]$DotnetPath = (Get-Command dotnet -CommandType Application |
        Select-Object -First 1 -ExpandProperty Source)
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$expectedHash = ((Get-Content (Join-Path $repo 'tests\parity\csharp-oracle.sha256') -Raw).Trim() -split '\s+')[0]
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('cancer-lab-oracle-serialization-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $scratch 'csharp-oracle.json'

try {
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    & $DotnetPath run --project (Join-Path $repo 'tests\parity\WebParityOracle.csproj') -- $output
    if ($LASTEXITCODE -ne 0) {
        throw "C# oracle serialization command failed with $LASTEXITCODE."
    }
    $bytes = [IO.File]::ReadAllBytes($output)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($text.Contains("`r")) {
        throw 'Oracle bytes contain physical carriage returns; use canonical LF output.'
    }
    if ($text.Contains('\r\n')) {
        throw 'Oracle embeds CRLF in a serialized save string; normalize nested saves before outer serialization.'
    }
    $actualHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Canonical oracle hash $actualHash does not match reviewed expected hash $expectedHash."
    }
} finally {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output 'PASS: production oracle serialization is LF-canonical, including nested save strings.'
