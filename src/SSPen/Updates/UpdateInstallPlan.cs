using System.ComponentModel;
using System.IO;

namespace SSPen.Updates;

/// <summary>
/// 무음 설치 체인의 명령줄과 프로세스 실행 실패 판정 (77단계, A9-7 (c)·A1-8). cmd 인용 규칙이 까다로운데 증인이 없던
/// <see cref="UpdateService.LaunchSilentInstallerAndExit"/>의 보간식을 글자 그대로 옮겨 <c>UpdateInstallPlanTests</c>가 스냅숏으로 지킨다.
/// </summary>
internal static class UpdateInstallPlan
{
    /// <summary>
    /// <c>cmd.exe</c>에 넘길 인자. Inno Setup 스위치:
    /// /VERYSILENT(진행 대화상자 없이 완전 백그라운드 설치), /SUPPRESSMSGBOXES(메시지 박스 억제), /NORESTART(시스템 재부팅 억제),
    /// /CLOSEAPPLICATIONS(충돌 프로그램 자동 닫기 시도), /FORCECLOSEAPPLICATIONS(강제 닫기).
    /// 체이닝: <c>start /wait</c>로 설치 완료를 기다린 뒤 설치된 새 버전의 실행 파일을 시작한다.
    /// <b>연결자는 <c>&amp;</c>이지 <c>&amp;&amp;</c>가 아니다</b> — <c>&amp;&amp;</c>면 설치가 실패(0 아닌 종료 코드)했을 때 앱이
    /// 되살아나지 않아 '업데이트했더니 앱이 사라졌다'가 된다(과거 실측). 바깥 따옴표 한 쌍은 <c>cmd /c</c>의 인용 벗기기 규칙용이다.
    /// </summary>
    internal static string CommandLine(string installerPath, string currentExe) =>
        $"/c \"start /wait \"\" \"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS & start \"\" \"{currentExe}\"\"";

    /// <summary>
    /// <c>Process.Start</c>의 문서화된 실패 예외인가 — 설치 체인·설치 프로그램 직접 실행·릴리스 페이지 열기의 좁은 catch 필터가
    /// 공유한다 (AGENTS '좁은 catch 필터', A1-8). 예전의 맨 <c>catch</c>가 삼키던 실사용 경로는 전부 여기 든다.
    /// </summary>
    internal static bool IsLaunchFailure(Exception ex) =>
        ex is Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or ObjectDisposedException
            or FileNotFoundException;
}
