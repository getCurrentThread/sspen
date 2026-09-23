using SSPen.Capture;

namespace SSPen.Shell;

/// <summary>
/// 캡처 결과 → 사용자 문구 매핑 (68단계, C-2). 판정은 <see cref="CaptureOutcomeRules"/>, 문장은 <see cref="Strings"/>가
/// 소유하고, 여기는 식별자를 문장에 잇는 표뿐이다.
///
/// 왜 루트에서 떼는가: 이 표가 합성 루트(<c>AppController.ReportCaptureOutcome</c>) 안에 있을 때는 기본 팔이 빈 문자열이었고,
/// <see cref="ToastQueue"/>는 빈 문구를 조용히 버린다. 그래서 <see cref="CaptureMessageId"/>에 값을 더하고 표를 잊으면
/// 그 결과는 로그도 없이 사라졌다 — "침묵하면 사용자는 캡처가 안 된 줄 안다"는 알림 도입 이유가 그대로 무너진다.
/// 이제 모르는 식별자는 예외이고, 헤드리스 전수 증인(<c>CaptureOutcomeTextTests</c>)이 빈 문구를 잡는다.
/// </summary>
public static class CaptureOutcomeText
{
    /// <summary>
    /// 알림 본문. 저장 성공은 경로가 있으면 파일 이름만 덧붙인다(폴더까지 적으면 토스트가 넘친다).
    /// <see cref="CaptureMessageId.None"/>은 알릴 것이 없다는 뜻이라 빈 문자열이다 — 호출자가 먼저 걸러야 한다.
    /// </summary>
    public static string Text(CaptureOutcome outcome) => outcome.Message switch
    {
        CaptureMessageId.Saved => outcome.Path is { } path
            ? Strings.CaptureSavedDetail(System.IO.Path.GetFileName(path))
            : Strings.CaptureSaved,
        CaptureMessageId.SaveFailed => Strings.CaptureSaveFailed,
        CaptureMessageId.Copied => Strings.CaptureCopied,
        CaptureMessageId.CopyFailed => Strings.ClipboardCopyFailed,
        CaptureMessageId.Pinned => Strings.CapturePinned,
        CaptureMessageId.PinFailed => Strings.CapturePinFailed,
        CaptureMessageId.None => string.Empty,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Message, "문구가 매핑되지 않은 캡처 결과 식별자"),
    };

    /// <summary>
    /// 알림 액션 라벨('폴더 열기'). 판정이 폴더 열기를 제안했고(<see cref="CaptureOutcome.OfferOpenFolder"/>) 열 경로가
    /// 실제로 있을 때만 붙는다 — 둘 중 하나라도 없으면 <c>null</c>(액션 없음)이다.
    /// </summary>
    public static string? ActionLabel(CaptureOutcome outcome) =>
        outcome.OfferOpenFolder && outcome.Path is not null ? Strings.OpenFolder : null;
}
