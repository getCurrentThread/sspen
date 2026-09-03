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
# 이 검증은 "게시본이 시스템 런타임 없이 뜨는가"를 프로세스가 살아 있는지로 판정한다.
# 그런데 App.xaml.cs의 단일 인스턴스 뮤텍스(SSPen-SingleInstance)는 이미 실행 중인 인스턴스가
# 있으면 새 프로세스를 **정상 종료(exit 0)** 시킨다 — 검증에는 자체 포함 실패와 구분되지 않는
# 모습으로 보인다. 그래서 로컬 개발기에서 앱을 띄워 둔 채 배포하면 이 단계가 항상 실패했다.
# 배포 규칙상 어차피 설치 전에 종료해야 하므로(CLAUDE.md), 여기서 먼저 종료한다.
$running = @(Get-Process -Name 'SSPen' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Stopping $($running.Count) running SSPen instance(s) so the single-instance mutex is free"
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($p in $running) { $p.WaitForExit(5000) | Out-Null }
    # 뮤텍스는 마지막 핸들이 닫혀야 사라진다 — 종료 직후 곧바로 띄우면 아직 잡혀 있을 수 있다.
    Start-Sleep -Milliseconds 500
    if (@(Get-Process -Name 'SSPen' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Could not stop the running SSPen instance(s); close SS Pen and re-run.'
    }
}

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
        # exit 0은 자체 포함 실패가 아니라 '다른 인스턴스가 이미 있다'는 뜻일 가능성이 높다
        # (위에서 종료했더라도 그 사이 누군가 다시 띄웠다면 여기로 온다).
        $hint = if ($proc.ExitCode -eq 0) {
            ' — exit 0 usually means another SSPen instance was running (single-instance mutex), not a self-contained failure'
        } else { '' }
        throw "Masking run failed (exit $($proc.ExitCode))$hint"
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
