namespace SSPen.Shell;

/// <summary>
/// 업데이트 대화상자의 진행 문구 판정 (77단계, C-5). 창 람다 안에 있던 "퍼센트 문구를 쓰고 1.0 이상이면 '설치 중'으로 덮어쓰기"를
/// 한 번의 선택으로 옮겼다 — 최종 문구는 같다. 백분율은 반올림이 아니라 <b>버림</b>(<c>(int)(p * 100)</c>)이라 0.999는 99%다.
/// 받은 바이트가 Content-Length보다 많아 1.0을 넘어도 '설치 중'이다. Updates 계층이 Shell(<see cref="Strings"/>)을 참조하지 않도록
/// <c>UpdateCheckPresentation</c>이 아니라 여기에 둔다. 증인은 <c>UpdateProgressTextTests</c>.
/// </summary>
public static class UpdateProgressText
{
    public static string For(double progress) =>
        progress >= 1.0
            ? Strings.UpdateInstalling
            : Strings.UpdateDownloadingPercent((int)(progress * 100));
}
