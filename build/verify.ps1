# SS Pen All-in-One Local Verification and Packaging Pipeline
# Run: powershell -ExecutionPolicy Bypass -File build/verify.ps1
#Requires -Version 5
$ErrorActionPreference = 'Stop'

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root 'SSPen.sln'
$unitTestProject = Join-Path $root 'tests\SSPen.Tests\SSPen.Tests.csproj'
$integrationTestProject = Join-Path $root 'tests\SSPen.IntegrationTests\SSPen.IntegrationTests.csproj'
$e2eTestProject = Join-Path $root 'tests\SSPen.E2ETests\SSPen.E2ETests.csproj'
$appProject = Join-Path $root 'src\SSPen\SSPen.csproj'
$publishDir = Join-Path $root 'publish\win-x64'

function Write-Step([string]$title) {
    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
}

function Write-Success([string]$msg) {
    Write-Host "[SUCCESS] $msg" -ForegroundColor Green
}

function Write-Warn([string]$msg) {
    Write-Host "[WARNING] $msg" -ForegroundColor Yellow
}

# 0. Clean running instances
Get-Process SSPen -ErrorAction SilentlyContinue | Stop-Process -Force

# 1. Solution Build
Write-Step '1) Solution Build (Release, net10.0-windows)'
dotnet build $sln -c Release
if ($LASTEXITCODE -ne 0) { throw "Solution build failed (ExitCode: $LASTEXITCODE)" }
Write-Success 'Solution build completed'

# 2. Unit and Simulation Tests
Write-Step '2) Headless Unit & Simulation Tests'
dotnet test $unitTestProject -c Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed (ExitCode: $LASTEXITCODE)" }
Write-Success 'Unit and simulation tests passed'

# 3. Integration Tests
Write-Step '3) OS-Bound Integration Tests'
dotnet test $integrationTestProject -c Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) { throw "Integration tests failed (ExitCode: $LASTEXITCODE)" }
Write-Success 'Integration tests passed'

# 4. E2E User Workflow Tests
Write-Step '4) End-to-End User Workflow Tests'
dotnet test $e2eTestProject -c Release --no-build --verbosity normal
if ($LASTEXITCODE -ne 0) { throw "E2E tests failed (ExitCode: $LASTEXITCODE)" }
Write-Success 'E2E tests passed'

# 5. Self-Contained Publish
Write-Step '5) Self-Contained Publish (win-x64)'
dotnet publish $appProject -c Release -r win-x64 --self-contained true -o $publishDir -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (ExitCode: $LASTEXITCODE)" }
Write-Success "Self-contained publish completed ($publishDir)"

# 6. Publish Integrity Assertion
Write-Step '6) Publish Integrity Assertions'
$required = @('SSPen.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'wpfgfx_cor3.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $publishDir $file))) {
        throw "Missing required file in self-contained publish folder: $file"
    }
}
$runtimeConfig = Get-Content (Join-Path $publishDir 'SSPen.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'runtimeconfig contains framework-dependent references'
}
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -notcontains 'includedFrameworks') {
    throw 'runtimeconfig missing includedFrameworks'
}
Write-Success 'Self-contained publish integrity verified'

# 7. DOTNET_ROOT Masking Startup Test (CRIT-3)
Write-Step '7) DOTNET_ROOT Masking Startup Test (CRIT-3)'
$maskDir = Join-Path $env:TEMP ("sspen-empty-dotnet-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $maskDir | Out-Null
try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $publishDir 'SSPen.exe'
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['DOTNET_ROOT'] = $maskDir
    $psi.EnvironmentVariables['DOTNET_ROOT(x86)'] = $maskDir
    $proc = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 5
    if ($proc.HasExited) {
        throw "Masking run failed: process exited early (ExitCode: $($proc.ExitCode))"
    }
    Write-Success "Masking run passed: PID $($proc.Id) running without system runtime"
    Stop-Process -Id $proc.Id -Force
    $proc.WaitForExit(5000) | Out-Null
}
finally {
    Remove-Item $maskDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 8. Inno Setup Installer Build (Optional)
Write-Step '8) Inno Setup 6 Installer Packaging (Optional)'
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    & $iscc (Join-Path $root 'installer\SSPen.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed (ExitCode: $LASTEXITCODE)" }
    Write-Success 'Installer packaged successfully'
    Get-ChildItem (Join-Path $root 'publish\installer') | Format-Table Name, Length
} else {
    Write-Warn 'ISCC.exe not found. Skipping installer packaging.'
    Write-Warn 'To install Inno Setup: winget install JRSoftware.InnoSetup'
}

$stopwatch.Stop()
Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " [ALL PASSED] Verification and packaging complete (Elapsed: $($stopwatch.Elapsed.ToString('mm\:ss')))" -ForegroundColor Green
Write-Host "========================================================`n" -ForegroundColor Green
