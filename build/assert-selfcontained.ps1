# SS Pen 자체 포함 게시 폴더 단언 (AC-24 결정적 검증 1, CRIT-3) — 93단계, A8-8
# publish.ps1·verify.ps1·ci.yml·release.yml이 모두 이 한 벌을 부른다. 예전에는 네 곳에 복사돼 있었고
# release.yml 사본은 hostpolicy.dll·wpfgfx_cor3.dll·framework 검사가 빠진 채 어긋나 있었다.
# 검증 2(DOTNET_ROOT 마스킹 실행)는 프로세스 수명 정책이 호출자마다 달라 각 스크립트에 남긴다.
# 사용: & build/assert-selfcontained.ps1 -PublishDir publish/win-x64 (상대 경로는 현재 위치 기준)
#Requires -Version 5
param(
    [Parameter(Mandatory)][string]$PublishDir
)
$ErrorActionPreference = 'Stop'

$required = @('SSPen.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'wpfgfx_cor3.dll')
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $PublishDir $file))) {
        throw "Missing $file in publish folder ($PublishDir)"
    }
}
$runtimeConfig = Get-Content (Join-Path $PublishDir 'SSPen.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or
    $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks') {
    throw 'runtimeconfig contains framework dependencies'
}
if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -notcontains 'includedFrameworks') {
    throw 'runtimeconfig missing includedFrameworks'
}
Write-Host 'Publish integrity verified.'
