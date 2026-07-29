<#
.SYNOPSIS
  foileBrowser installer (Windows).
.DESCRIPTION
  Publishes the app to %LOCALAPPDATA%\Programs\foileBrowser, adds a launcher to a bin
  folder on your user PATH, and creates a Start Menu shortcut. Run uninstall.ps1 to reverse.

  NativeAOT is the default: it is the smallest thing to ship and the lightest to run
  (one .exe, no .NET runtime to install), and it is the build the memory figures in the
  README are measured on. It needs the Visual Studio C++ build tools to compile, and the
  publish takes a few minutes.
.PARAMETER NoAot
  Publish a trimmed self-contained build instead — still runtime-free, but a larger
  footprint. Use this when the C++ build tools are unavailable or the AOT publish is too slow.
.PARAMETER FrameworkDependent
  Smallest download; requires the .NET runtime to be installed to run.
.PARAMETER SelfContained
  Accepted and ignored; implied by the default.
#>
param(
  [switch]$NoAot,
  [switch]$FrameworkDependent,
  [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = Join-Path $env:LOCALAPPDATA 'Programs\foileBrowser'
$binDir = Join-Path $env:LOCALAPPDATA 'Programs\bin'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "The .NET SDK ('dotnet') is required to build foileBrowser."
}

$aot = -not ($NoAot -or $FrameworkDependent)

if ($aot) {
  Write-Host "==> Publishing (Release, NativeAOT) to $appDir — this takes a few minutes"
} elseif (-not $FrameworkDependent) {
  Write-Host "==> Publishing (Release, trimmed self-contained) to $appDir"
} else {
  Write-Host "==> Publishing (Release, framework-dependent) to $appDir"
}

if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
$publishArgs = @('-c', 'Release', '-o', $appDir, '--nologo')
if ($aot) {
  $publishArgs += @('-r', 'win-x64', '--self-contained', 'true', '-p:FoileAot=true')
} elseif (-not $FrameworkDependent) {
  $publishArgs += @('-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=true')
} else {
  $publishArgs += @('--self-contained', 'false')
}

# ILCompiler reports a missing toolchain as a link failure deep in the build log, so say what
# the way out is rather than leaving the reader to work it out from a linker error.
try {
  dotnet publish (Join-Path $repo 'src\FoileBrowser.csproj') @publishArgs
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish exited with $LASTEXITCODE" }
} catch {
  if ($aot) {
    Write-Host ""
    Write-Host "The NativeAOT publish failed. It needs the Visual Studio C++ build tools" -ForegroundColor Yellow
    Write-Host "('Desktop development with C++'). Install those, or re-run with -NoAot." -ForegroundColor Yellow
  }
  throw
}

# The AOT publish drops a separate debug companion larger than the binary itself. Nobody
# installing a file browser wants the symbols; the release packaging drops them for the same reason.
Get-ChildItem -Path $appDir -Include '*.pdb', '*.dbg' -File -ErrorAction SilentlyContinue | Remove-Item -Force

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
