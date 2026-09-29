param(
    [string]$DotnetPath,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($DotnetPath)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT) -and (Test-Path -LiteralPath $env:DOTNET_ROOT)) {
        $DotnetPath = $env:DOTNET_ROOT
    } else {
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($command) { $DotnetPath = $command.Source }
    }
}
if ([string]::IsNullOrWhiteSpace($DotnetPath) -or -not (Test-Path -LiteralPath $DotnetPath)) {
    throw 'Provide -DotnetPath or make dotnet available on PATH.'
}
$dotnetItem = Get-Item -LiteralPath $DotnetPath
if ($dotnetItem.PSIsContainer) {
    $candidateNames = if ($IsWindows) { @('dotnet.exe', 'dotnet') } else { @('dotnet', 'dotnet.exe') }
    $dotnetExecutable = $candidateNames |
        ForEach-Object { Join-Path $dotnetItem.FullName $_ } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($dotnetExecutable)) {
        throw 'DotnetPath identifies a directory without a dotnet executable. Provide -DotnetPath or make dotnet available on PATH.'
    }
    $DotnetPath = $dotnetExecutable
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'tests\parity\fixtures\csharp-oracle.json'
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null
& (Resolve-Path -LiteralPath $DotnetPath).Path run --project (Join-Path $root 'tests\parity\WebParityOracle.csproj') -- $OutputPath
if ($LASTEXITCODE -ne 0) { throw "C# parity oracle generation failed with exit code $LASTEXITCODE." }
$expected = ((Get-Content (Join-Path $root 'tests\parity\csharp-oracle.sha256') -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Generated oracle hash $actual does not match reviewed expected hash $expected." }
Write-Host "PASS: generated and verified C# parity oracle $OutputPath"
