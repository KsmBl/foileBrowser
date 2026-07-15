<#
.SYNOPSIS
  foileBrowser installer (Windows).
.DESCRIPTION
  Publishes the app to %LOCALAPPDATA%\Programs\foileBrowser, adds a launcher to a bin
  folder on your user PATH, and creates a Start Menu shortcut. Run uninstall.ps1 to reverse.
.PARAMETER SelfContained
  Publish a self-contained build (no .NET runtime required to run).
#>
param(
  [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\foileBrowser'
$binDir = Join-Path $env:LOCALAPPDATA 'Programs\bin'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "The .NET SDK ('dotnet') is required to build foileBrowser."
}

Write-Host "==> Publishing (Release) to $appDir"
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
$args = @('-c', 'Release', '-o', $appDir, '--nologo')
$args += if ($SelfContained) { @('--self-contained', 'true', '-r', 'win-x64') } else { @('--self-contained', 'false') }
dotnet publish (Join-Path $repo 'src\FoileBrowser.csproj') @args

$exe = Join-Path $appDir 'FoileBrowser.exe'

Write-Host "==> Installing launcher to $binDir"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
"@echo off`r`nstart """" ""$exe"" %*" | Set-Content -Encoding ASCII (Join-Path $binDir 'foilebrowser.cmd')

# Add bin dir to the user PATH if missing.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$binDir*") {
  [Environment]::SetEnvironmentVariable('Path', "$userPath;$binDir", 'User')
  Write-Host "    Added $binDir to your user PATH (restart your terminal to pick it up)."
}

Write-Host "==> Creating Start Menu shortcut"
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $startMenu 'foileBrowser.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $appDir
$shortcut.Save()

Write-Host "`nInstalled. Launch 'foileBrowser' from the Start Menu or run 'foilebrowser'."
