$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$source = Join-Path $root 'dist'
$install = Join-Path $env:LOCALAPPDATA 'Programs\CodexAuthSwitcher'
$exePath = Join-Path $install 'CodexAuthSwitcher.exe'

if (-not (Test-Path -LiteralPath (Join-Path $source 'CodexAuthSwitcher.exe'))) {
    & (Join-Path $PSScriptRoot 'build.ps1')
}

New-Item -ItemType Directory -Force -Path $install | Out-Null
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $install -Recurse -Force
}

$programs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $programs 'Codex Auth Switcher.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $install
$shortcut.IconLocation = Join-Path $install 'Assets\CodexAuthSwitcher.ico'
$shortcut.Description = 'Windows UI for codex-auth account switching'
$shortcut.Save()

Write-Host "Installed to $install"
Write-Host "Created Start Menu shortcut: $shortcutPath"
