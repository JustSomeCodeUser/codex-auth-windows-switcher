$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'CodexAuthSwitcher\CodexAuthSwitcher.csproj'
$output = Join-Path $root 'dist'

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $output"
