using System.IO;
using SSPen.Capture;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="CaptureOutcomeRules"/>의 증인. 이전 동작(저장 성공·실패 모두 침묵, 실패는 일반 치명적 대화상자로 유출)의
/// 회귀를 막는 표다 — 어떤 결과가 어떤 등급으로 무엇을 말하는지는 창이 아니라 여기서 정해진다.
/// </summary>
public class CaptureOutcomeRulesTests
{
    [Fact]
    public void Decide_SaveSucceeded_IsInfoAndCarriesThePathWithAFolderAction()
    {
        var outcome = CaptureOutcomeRules.Decide(
            CaptureAction.Save, regionEmpty: false, succeeded: true, savedPath: @"C:\사진\SS Pen\a.png");

        Assert.Equal(ToastKind.Info, outcome.Kind);
        Assert.Equal(CaptureMessageId.Saved, outcome.Message);
        Assert.Equal(@"C:\사진\SS Pen\a.png", outcome.Path);
        Assert.True(outcome.OfferOpenFolder);
    }

    /// <summary>경로 없이 성공했다고 주장하는 결과에는 폴더 열기를 붙이지 않는다 (열 곳이 없다).</summary>
    [Fact]
    public void Decide_SaveSucceededWithoutPath_DoesNotOfferTheFolder()
    {
        var outcome = CaptureOutcomeRules.Decide(
            CaptureAction.Save, regionEmpty: false, succeeded: true, savedPath: null);

        Assert.False(outcome.OfferOpenFolder);
    }

    /// <summary>저장 실패는 오류다 — 이미지가 사라졌고 다시 찍는 것 말고 복구가 없다.</summary>
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void Decide_SaveThrew_IsErrorWithoutFolderAction(Type failureType)
    {
        var failure = (Exception)Activator.CreateInstance(failureType)!;

        var outcome = CaptureOutcomeRules.Decide(
            CaptureAction.Save, regionEmpty: false, succeeded: false, savedPath: null, failure);

        Assert.Equal(ToastKind.Error, outcome.Kind);
        Assert.Equal(CaptureMessageId.SaveFailed, outcome.Message);
        Assert.False(outcome.OfferOpenFolder);
    }

    /// <summary>예외가 실린 결과는 <c>succeeded</c>가 참이라고 주장해도 실패로 본다 (모순 시 예외가 이긴다).</summary>
    [Fact]
    public void Decide_SaveWithFailureButClaimingSuccess_StillReportsFailure()
    {
        var outcome = CaptureOutcomeRules.Decide(
            CaptureAction.Save, regionEmpty: false, succeeded: true, savedPath: @"C:\x.png", new IOException());

        Assert.Equal(CaptureMessageId.SaveFailed, outcome.Message);
    }

    [Fact]
    public void Decide_CopySucceeded_IsInfoWithoutAction()
    {
        var outcome = CaptureOutcomeRules.Decide(CaptureAction.Copy, regionEmpty: false, succeeded: true);

        Assert.Equal(ToastKind.Info, outcome.Kind);
        Assert.Equal(CaptureMessageId.Copied, outcome.Message);
        Assert.False(outcome.OfferOpenFolder);
    }

    /// <summary>복사 실패는 경고다 — 클립보드 경합은 일시적이라 다시 시도하면 대개 된다.</summary>
    [Fact]
    public void Decide_CopyFailed_IsWarning()
    {
        var outcome = CaptureOutcomeRules.Decide(CaptureAction.Copy, regionEmpty: false, succeeded: false);

        Assert.Equal(ToastKind.Warning, outcome.Kind);
        Assert.Equal(CaptureMessageId.CopyFailed, outcome.Message);
    }

    [Fact]
    public void Decide_PinSucceeded_IsInfo()
    {
        var outcome = CaptureOutcomeRules.Decide(CaptureAction.Pin, regionEmpty: false, succeeded: true);

        Assert.Equal(CaptureMessageId.Pinned, outcome.Message);
    }

    [Fact]
    public void Decide_PinFailed_IsWarningAndStillSpeaks()
    {
        var outcome = CaptureOutcomeRules.Decide(CaptureAction.Pin, regionEmpty: false, succeeded: false);

        Assert.Equal(ToastKind.Warning, outcome.Kind);
        Assert.Equal(CaptureMessageId.PinFailed, outcome.Message);
    }

    /// <summary>아무 일도 하지 않은 조작은 말을 걸지 않는다: 취소와 빈 영역은 성공도 실패도 아니다.</summary>
    [Theory]
    [InlineData(CaptureAction.Cancel, false)]
    [InlineData(CaptureAction.Save, true)]
    [InlineData(CaptureAction.Copy, true)]
    [InlineData(CaptureAction.Pin, true)]
    public void Decide_CancelOrEmptyRegion_SaysNothing(CaptureAction action, bool regionEmpty)
    {
        var outcome = CaptureOutcomeRules.Decide(action, regionEmpty, succeeded: true, savedPath: @"C:\x.png");

        Assert.Equal(CaptureMessageId.None, outcome.Message);
        Assert.False(outcome.OfferOpenFolder);
    }
}
