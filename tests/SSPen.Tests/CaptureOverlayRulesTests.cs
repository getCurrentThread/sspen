using SSPen.Annotation;
using SSPen.Capture;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="CaptureOverlayRules"/>의 증인 (WI-11). 지키는 것과 고치는 것을 함께 잠근다:
/// 도구모음 밖 <b>제자리 클릭</b>은 여전히 기본 동작(핀)으로 끝나고(사용자 요청 15차),
/// <b>드래그</b>는 다시 고르는 것이다 — 예전에는 둘 다 즉시 핀으로 확정됐다.
/// </summary>
public class CaptureOverlayRulesTests
{
    /// <summary>기본 동작의 단일 소유자 — 배지·Enter·바깥 클릭이 이 값을 함께 본다.</summary>
    [Fact]
    public void DefaultAction_IsPin() => Assert.Equal(CaptureAction.Pin, CaptureOverlayRules.DefaultAction);

    /// <summary>
    /// 정지 임계값은 선택 계층과 같은 3px이다. 같은 손동작을 두 계층이 다르게 부르면
    /// 사용자는 그 차이를 학습할 방법이 없다.
    /// </summary>
    [Fact]
    public void ClickThreshold_MatchesTheSelectionLayer() =>
        Assert.Equal(SelectionGestureRules.ClickThresholdPixels, CaptureOverlayRules.ClickThresholdPixels);

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void PointerVerdict_StationaryClickOutsideBar_CommitsDefault(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.CommitDefault, verdict);
    }

    /// <summary>영역을 잘못 잡아 다시 끄는 것이 원치 않는 핀 창으로 끝나지 않는다.</summary>
    [Theory]
    [InlineData(3.5)]
    [InlineData(200.0)]
    public void PointerVerdict_DragOutsideBar_RestartsSelection(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.RestartSelection, verdict);
    }

    /// <summary>도구모음 안은 버튼 자신의 몫이다 — 기본 동작이 복사·저장·취소를 삼키면 누를 수 없다.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    public void PointerVerdict_InsideBar_IsIgnored(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: true, moved);

        Assert.Equal(CapturePointerVerdict.Ignore, verdict);
    }

    /// <summary>아직 고르는 중이면 무엇을 해도 새 선택이다 (기본 동작으로 샐 경로가 없다).</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    public void PointerVerdict_BeforeTheBarAppears_IsAlwaysANewSelection(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: false, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.RestartSelection, verdict);
    }
}
