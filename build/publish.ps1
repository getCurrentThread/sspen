# SS Pen Publish + Installer Script (WI-18/WI-19)
#Requires -Version 5
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SSPen\SSPen.csproj'
$publishDir = Join-Path $root 'publish\win-x64'

Write-Host '=== 1) self-contained publish (net10.0-windows, win-x64) ==='
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host '=== 2) AC-24 Deterministic Validation 1: Publish Directory Assertions (CRIT-3) ==='
$required = @('SSPen.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'wpfgfx_cor3.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $publishDir $file))) {
        throw "Missing $file in publish folder"
    }
}
$runtimeConfig = Get-Content (Join-Path $publishDir 'SSPen.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'runtimeconfig contains framework dependencies'
}
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -notcontains 'includedFrameworks') {
    throw 'runtimeconfig missing includedFrameworks'
}
Write-Host 'Publish integrity verified.'

Write-Host '=== 3) AC-24 Deterministic Validation 2: DOTNET_ROOT Masking Startup Test (CRIT-3) ==='
$maskDir = Join-Path $env:TEMP ("sspen-empty-dotnet-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $maskDir | Out-Null
try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $publishDir 'SSPen.exe'
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['DOTNET_ROOT'] = $maskDir
    $psi.EnvironmentVariables['DOTNET_ROOT(x86)'] = $maskDir
    $proc = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 6
    if ($proc.HasExited) {
        throw "Masking run failed (exit $($proc.ExitCode))"
    }
    Write-Host "Masking run passed: PID $($proc.Id) running without system runtime"
    Stop-Process -Id $proc.Id -Force
    $proc.WaitForExit(5000) | Out-Null
}
finally {
    Remove-Item $maskDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '=== 4) Inno Setup 6 Installer Build (WI-19) ==='
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'ISCC.exe not found. Install: winget install JRSoftware.InnoSetup'
}
& $iscc (Join-Path $root 'installer\SSPen.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

Write-Host '=== Complete ==='
Get-ChildItem (Join-Path $root 'publish\installer') | Format-Table Name, Length
