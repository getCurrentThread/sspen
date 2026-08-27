# SS Pen 게시 + 설치 프로그램 빌드 (WI-18/WI-19)
# 선행 조건: .NET SDK 10, Inno Setup 6 (없으면: winget install JRSoftware.InnoSetup)
#Requires -Version 5
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SSPen\SSPen.csproj'
$publishDir = Join-Path $root 'publish\win-x64'

Write-Host '=== 1) self-contained 게시 (net10.0-windows, win-x64) ==='
dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir -v minimal -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

Write-Host '=== 2) AC-24 결정론적 검증 1: 게시 폴더 단언 (CRIT-3) ==='
$required = @('SSPen.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'wpfgfx_cor3.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $publishDir $file))) {
        throw "self-contained 게시 폴더에 $file 이 없습니다"
    }
}
$runtimeConfig = Get-Content (Join-Path $publishDir 'SSPen.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'runtimeconfig 에 프레임워크 의존 참조가 남아 있습니다 (framework-dependent 게시)'
}
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -notcontains 'includedFrameworks') {
    throw 'runtimeconfig 에 includedFrameworks 가 없습니다 (self-contained 아님)'
}
Write-Host '게시 폴더 단언 통과: apphost + coreclr/hostfxr 존재, includedFrameworks 확인'

Write-Host '=== 3) AC-24 결정론적 검증 2: DOTNET_ROOT 마스킹 실행 (CRIT-3) ==='
# 설치된 머신 런타임을 빈 디렉터리로 가리게 해 self-contained 실행을 증명한다.
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
        throw "마스킹 실행 실패: 프로세스가 종료됨 (exit $($proc.ExitCode))"
    }
    Write-Host "마스킹 실행 통과: PID $($proc.Id) 가 머신 런타임 없이 기동됨"
    Stop-Process -Id $proc.Id -Force
    $proc.WaitForExit(5000) | Out-Null
}
finally {
    Remove-Item $maskDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '=== 4) Inno Setup 6 설치 프로그램 (WI-19) ==='
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    # R4 즉시 실패: 선행 조건 안내.
    throw 'ISCC.exe 를 찾을 수 없습니다. 설치: winget install JRSoftware.InnoSetup'
}
& $iscc (Join-Path $root 'installer\SSPen.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC 실패 (exit $LASTEXITCODE)" }

Write-Host '=== 완료 ==='
Get-ChildItem (Join-Path $root 'publish\installer') | Format-Table Name, Length
