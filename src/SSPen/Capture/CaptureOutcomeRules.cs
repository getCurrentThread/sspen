using SSPen.Shell;

namespace SSPen.Capture;

/// <summary>캡처 결과 알림의 문구 식별자. <b>문장은 여기 없다</b> — 사용자 문자열은 <see cref="Strings"/>에만 산다.</summary>
public enum CaptureMessageId
{
    /// <summary>알릴 것이 없다 (취소·빈 영역). 아무 일도 하지 않은 조작은 말을 걸지 않는다.</summary>
    None,
    Saved,
    SaveFailed,
    Copied,
    CopyFailed,
    Pinned,
    PinFailed,
}

/// <summary>캡처 한 건의 알림 판정 결과. <c>Path</c>는 저장 성공일 때만 채워진다.</summary>
public readonly record struct CaptureOutcome(
    ToastKind Kind,
    CaptureMessageId Message,
    string? Path,
    bool OfferOpenFolder);

/// <summary>
/// 캡처 결과 → 사용자 알림 판정 (WI-11/WI-12의 순수 코어).
///
/// 왜 코어로 떼는가: 이전에는 저장이 성공하든 실패하든 <b>사용자에게 아무 말도 하지 않았고</b>
/// (<c>CaptureOutputs.SavePng</c>의 반환 경로를 버렸다), 실패는 잡히지 않은 채 <c>App.xaml.cs</c>의
/// 일반 치명적 오류 대화상자로 새어 나가 "무슨 작업이 실패했는지"조차 알 수 없었다.
/// 어느 결과가 어떤 등급으로 무엇을 말하는지는 창이 아니라 이 표가 정한다.
/// </summary>
public static class CaptureOutcomeRules
{
    public static CaptureOutcome Decide(
        CaptureAction action,
        bool regionEmpty,
        bool succeeded,
        string? savedPath = null,
        Exception? failure = null)
    {
        // 취소와 빈 영역은 결과물이 없다 — 성공도 실패도 아니므로 알리지 않는다.
        if (action == CaptureAction.Cancel || regionEmpty)
        {
            return new CaptureOutcome(ToastKind.Info, CaptureMessageId.None, null, false);
        }

        return action switch
        {
            // 저장 실패는 오류다: 이미지가 사라졌고 사용자가 다시 찍는 것 말고는 복구할 방법이 없다.
            CaptureAction.Save when !succeeded || failure is not null
                => new CaptureOutcome(ToastKind.Error, CaptureMessageId.SaveFailed, null, false),
            // 저장 성공만 액션(폴더 열기)을 준다 — 파일이 어디로 갔는지가 이 조작의 유일한 미해결 질문이다.
            CaptureAction.Save
                => new CaptureOutcome(ToastKind.Info, CaptureMessageId.Saved, savedPath, !string.IsNullOrEmpty(savedPath)),
            // 복사 실패는 경고다: 클립보드 경합은 일시적이고 다시 시도하면 대개 된다 (ARCH-9 재시도 소진 뒤).
            CaptureAction.Copy when !succeeded
                => new CaptureOutcome(ToastKind.Warning, CaptureMessageId.CopyFailed, null, false),
            CaptureAction.Copy
                => new CaptureOutcome(ToastKind.Info, CaptureMessageId.Copied, null, false),
            // 핀 실패는 눈에 보이는 결과가 통째로 없는 경우다 — 침묵하면 사용자는 캡처 자체가 안 된 줄 안다.
            CaptureAction.Pin when !succeeded
                => new CaptureOutcome(ToastKind.Warning, CaptureMessageId.PinFailed, null, false),
            CaptureAction.Pin
                => new CaptureOutcome(ToastKind.Info, CaptureMessageId.Pinned, null, false),
            _ => new CaptureOutcome(ToastKind.Info, CaptureMessageId.None, null, false),
        };
    }
}
