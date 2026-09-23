using SSPen.Capture;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="CaptureOutcomeText"/>의 증인 (68단계, C-2). 판정(<see cref="CaptureOutcomeRules"/>)과 표시(<c>ToastQueue</c>) 사이의
/// 문구 연결을 잠근다. <c>ToastQueue</c>는 빈 문구를 조용히 버리므로, 새 식별자가 표에서 빠지면 알림이 로그도 없이
/// 사라진다 — 전수 트립와이어가 그 경로를 막는다.
/// </summary>
public class CaptureOutcomeTextTests
{
    public static TheoryData<CaptureMessageId> EveryMessageIdExceptNone()
    {
        var data = new TheoryData<CaptureMessageId>();
        foreach (var id in Enum.GetValues<CaptureMessageId>())
        {
            if (id != CaptureMessageId.None)
            {
                data.Add(id);
            }
        }
        return data;
    }

    private static CaptureOutcome Outcome(CaptureMessageId id, string? path = null, bool offerOpenFolder = false) =>
        new(ToastKind.Info, id, path, offerOpenFolder);

    /// <summary>새 식별자 트립와이어: <c>None</c>이 아닌 모든 값은 비지 않은 문구를 가져야 한다.</summary>
    [Theory]
    [MemberData(nameof(EveryMessageIdExceptNone))]
    public void Text_EveryMessageIdExceptNone_IsNonEmpty(CaptureMessageId id)
    {
        Assert.False(string.IsNullOrWhiteSpace(CaptureOutcomeText.Text(Outcome(id))));
    }

    /// <summary>루트에 있던 표와 바이트 단위로 같다 — 문장은 모두 <see cref="Strings"/>의 것이다.</summary>
    [Theory]
    [InlineData(CaptureMessageId.SaveFailed, Strings.CaptureSaveFailed)]
    [InlineData(CaptureMessageId.Copied, Strings.CaptureCopied)]
    [InlineData(CaptureMessageId.CopyFailed, Strings.ClipboardCopyFailed)]
    [InlineData(CaptureMessageId.Pinned, Strings.CapturePinned)]
    [InlineData(CaptureMessageId.PinFailed, Strings.CapturePinFailed)]
    public void Text_PathlessMessageIds_MapToTheirStringsEntry(CaptureMessageId id, string expected)
    {
        Assert.Equal(expected, CaptureOutcomeText.Text(Outcome(id)));
    }

    /// <summary><c>None</c>은 알릴 것이 없다 — 빈 문구다 (호출자가 먼저 걸러 토스트를 내지 않는다).</summary>
    [Fact]
    public void Text_None_IsEmpty() => Assert.Equal(string.Empty, CaptureOutcomeText.Text(Outcome(CaptureMessageId.None)));

    /// <summary>표에 없는 값은 조용한 빈 문구가 아니라 예외다 — 예전 기본 팔(<c>_ =&gt; string.Empty</c>)이 알림을 삼켰다.</summary>
    [Fact]
    public void Text_UndefinedMessageId_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CaptureOutcomeText.Text(Outcome((CaptureMessageId)999)));
    }

    /// <summary>저장 성공은 파일 이름만 덧붙인다 — 폴더 경로까지 적으면 토스트가 넘친다.</summary>
    [Fact]
    public void Text_SavedWithPath_UsesFileNameOnly()
    {
        var text = CaptureOutcomeText.Text(Outcome(CaptureMessageId.Saved, @"C:\a\b.png", offerOpenFolder: true));

        Assert.Equal(Strings.CaptureSavedDetail("b.png"), text);
    }

    [Fact]
    public void Text_SavedWithoutPath_IsGenericSaved()
    {
        Assert.Equal(Strings.CaptureSaved, CaptureOutcomeText.Text(Outcome(CaptureMessageId.Saved)));
    }

    /// <summary>'폴더 열기'는 판정이 제안했고 <b>그리고</b> 열 경로가 있을 때만 붙는다 — 네 조합.</summary>
    [Theory]
    [InlineData(true, @"C:\a\b.png", true)]
    [InlineData(true, null, false)]
    [InlineData(false, @"C:\a\b.png", false)]
    [InlineData(false, null, false)]
    public void ActionLabel_OnlyWhenOfferOpenFolderAndPath(bool offerOpenFolder, string? path, bool expectLabel)
    {
        var label = CaptureOutcomeText.ActionLabel(Outcome(CaptureMessageId.Saved, path, offerOpenFolder));

        Assert.Equal(expectLabel ? Strings.OpenFolder : null, label);
    }

    /// <summary>
    /// 판정 → 문구 왕복: 결과물이 있는 모든 조작(저장·복사·핀 × 성공·실패)은 비지 않은 문구로 끝나고, 폴더 열기 라벨은
    /// 저장 성공에만 붙는다. 판정 표와 문구 표가 서로 어긋나면 여기서 드러난다.
    /// </summary>
    [Theory]
    [InlineData(CaptureAction.Save, true)]
    [InlineData(CaptureAction.Save, false)]
    [InlineData(CaptureAction.Copy, true)]
    [InlineData(CaptureAction.Copy, false)]
    [InlineData(CaptureAction.Pin, true)]
    [InlineData(CaptureAction.Pin, false)]
    public void Text_Decide_RoundTrip_ForEveryActionAndSuccess(CaptureAction action, bool succeeded)
    {
        var outcome = CaptureOutcomeRules.Decide(
            action, regionEmpty: false, succeeded, savedPath: succeeded ? @"C:\사진\SS Pen\a.png" : null);

        Assert.NotEqual(CaptureMessageId.None, outcome.Message);
        Assert.False(string.IsNullOrWhiteSpace(CaptureOutcomeText.Text(outcome)));
        Assert.Equal(action == CaptureAction.Save && succeeded ? Strings.OpenFolder : null, CaptureOutcomeText.ActionLabel(outcome));
    }
}
