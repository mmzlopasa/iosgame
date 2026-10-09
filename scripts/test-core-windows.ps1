# Runs the EonwardCore unit tests on Windows (Swift toolchain + VS Build Tools required).
# The package is copied to a short temp path because SwiftPM on Windows struggles with long paths.
param([string]$Command = "test")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dst = Join-Path $env:TEMP "eonw"
robocopy (Join-Path $root "Core") $dst /MIR /XD .build /NFL /NDL /NJH /NJS | Out-Null

$vs = & "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -products * -latest -property installationPath
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
$path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') +
    ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
$sdkroot = [Environment]::GetEnvironmentVariable('SDKROOT', 'User')
if (-not $sdkroot) { $sdkroot = [Environment]::GetEnvironmentVariable('SDKROOT', 'Machine') }

cmd /c "set `"PATH=$path`" && set `"SDKROOT=$sdkroot`" && call `"$vcvars`" >nul && cd /d `"$dst`" && swift $Command 2>&1"
exit $LASTEXITCODE
