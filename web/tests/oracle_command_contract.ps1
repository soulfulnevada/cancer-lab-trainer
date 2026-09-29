param(
    [string]$DotnetPath = (Get-Command dotnet -CommandType Application).Source
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
} finally {
    if ($null -eq $originalDotnetRoot) {
        Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
    } else {
        $env:DOTNET_ROOT = $originalDotnetRoot
    }
    $env:PATH = $originalPath
}

Write-Output 'PASS: explicit override and default directory discovery survive a directory-valued DOTNET_ROOT.'
