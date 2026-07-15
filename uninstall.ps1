<#
.SYNOPSIS
  foileBrowser uninstaller (Windows). Removes what install.ps1 added.
#>
$ErrorActionPreference = 'SilentlyContinue'

$appDir = Join-Path $env:LOCALAPPDATA 'Programs\foileBrowser'
$binDir = Join-Path $env:LOCALAPPDATA 'Programs\bin'
$launcher = Join-Path $binDir 'foilebrowser.cmd'
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\foileBrowser.lnk'

Write-Host "==> Removing foileBrowser"
Remove-Item -Recurse -Force $appDir
Remove-Item -Force $launcher
Remove-Item -Force $shortcut

# Drop the bin dir from the user PATH if we added it and it is now empty of our launcher.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -like "*$binDir*") {
  $newPath = ($userPath -split ';' | Where-Object { $_ -ne $binDir }) -join ';'
  [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
}

Write-Host "Done. (Per-user settings in %APPDATA%\foileBrowser were left untouched.)"
