using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="RenderTickController"/>의 증인 (45단계, ARCH-3, CRIT-1). 상시 구독 금지·틱 내 자기 해제·후광 팬아웃·페이드 마감 순서
/// (Remove → PurgeElement)를 FakeFrameSource·가짜 서피스·주입 시계로 헤드리스 구동한다 — 이 경로는 이전에 E2E 스모크뿐이었다.
/// </summary>
public class RenderTickControllerTests
{
    private static readonly DateTime T0 = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Rig
    {
        public AppState State { get; } = new();
        public FadeSchedulerCore Core { get; } = new();
        public AnnotationDocument Document { get; } = new("test");
        public SelectionModel Selection { get; } = new();
        public UndoLedger Ledger { get; }
        public FakeFrameSource Frames { get; } = new();
        public FakeFadeSurface Surface { get; }
        public DateTime Now { get; set; } = T0;
        public (int X, int Y)? Cursor { get; set; }
        public List<(int X, int Y)> HaloUpdates { get; } = [];
        public bool OwnerKnown { get; set; } = true;
        public RenderTickController Controller { get; }

        public Rig()
        {
            Ledger = new UndoLedger(e => Document.Elements.Contains(e) ? Document : null, Selection);
            Surface = new FakeFadeSurface(Document);
            Controller = new RenderTickController(
                State, Core, Ledger, Frames, () => Now, () => Cursor, (x, y) => HaloUpdates.Add((x, y)),
                _ => OwnerKnown ? Surface : null);
        }
    }

    private sealed class FakeFadeSurface(AnnotationDocument document) : IFadeSurface
    {
        public AnnotationDocument Document { get; } = document;
        public List<string> Calls { get; } = [];
        public void AnimateFadeOut(AnnotationElement element, TimeSpan fadeLength, Action onCompleted)
        {
            Calls.Add($"Animate:{fadeLength.TotalMilliseconds}");
            onCompleted(); // 시각물이 없는 창처럼 동기 완료 — 순서 관측이 목적
        }
    }

    private static StrokeElement NewStroke() => new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(true, false, 0, true)]
    [InlineData(false, true, 0, true)]
    [InlineData(false, false, 1, true)]
    public void Needed_Table(bool halo, bool fading, int pending, bool expected) =>
        Assert.Equal(expected, RenderTickController.Needed(halo, fading, pending));

    [Fact]
    public void Refresh_NotNeeded_DoesNotStart()
    {
        var rig = new Rig();

        rig.Controller.Refresh();

        Assert.Equal(0, rig.Frames.StartCount);
        Assert.False(rig.Controller.IsAttached);
    }

    [Fact]
    public void Refresh_Needed_StartsOnce_Idempotent()
    {
        var rig = new Rig();
        rig.State.HaloActive = true;

        rig.Controller.Refresh();
        rig.Controller.Refresh();

        Assert.Equal(1, rig.Frames.StartCount);
        Assert.Equal(1, rig.Frames.SubscriberCount);
        Assert.True(rig.Controller.IsAttached);
    }

    /// <summary>자기 해제: 붙일 조건의 정확한 부정에서 틱 첫 줄이 뗀다 — Refresh는 절대 떼지 않는다.</summary>
    [Fact]
    public void Frame_WhenNoLongerNeeded_StopsItself_ButRefreshNeverStops()
    {
        var rig = new Rig();
        rig.State.HaloActive = true;
        rig.Controller.Refresh();

        rig.State.HaloActive = false;
        rig.Controller.Refresh();
        Assert.True(rig.Controller.IsAttached); // Refresh는 attach-only

        rig.Frames.Fire();

        Assert.False(rig.Controller.IsAttached);
        Assert.Equal(1, rig.Frames.StopCount);
        Assert.Equal(0, rig.Frames.SubscriberCount);
    }

    [Fact]
    public void Frame_HaloActive_UpdatesHalosWithCursor_OnlyWhenCursorKnown()
    {
        var rig = new Rig();
        rig.State.HaloActive = true;
        rig.Controller.Refresh();

        rig.Cursor = (100, 200);
        rig.Frames.Fire();
        rig.Cursor = null;
        rig.Frames.Fire();

        Assert.Equal([(100, 200)], rig.HaloUpdates);
    }

    /// <summary>CRIT-1: 마감된 요소는 애니메이션 → 문서 제거 → 원장 정리 순서다.</summary>
    [Fact]
    public void Frame_DueElement_AnimatesThenRemovesThenPurges()
    {
        var rig = new Rig();
        var stroke = NewStroke();
        rig.Document.Add(stroke);
        rig.Ledger.RecordAdd(stroke);
        rig.Core.Schedule(stroke, T0.AddSeconds(2));
        rig.Controller.Refresh();
        Assert.True(rig.Controller.IsAttached);

        rig.Now = T0.AddSeconds(1);
        rig.Frames.Fire();
        Assert.Empty(rig.Surface.Calls); // 마감 전

        rig.Now = T0.AddSeconds(2);
        rig.Frames.Fire();

        Assert.Equal([$"Animate:{RenderTickController.FadeOutLength.TotalMilliseconds}"], rig.Surface.Calls);
        Assert.DoesNotContain(stroke, rig.Document.Elements);
        Assert.Equal(0, rig.Ledger.Count);
        Assert.Equal(0, rig.Core.PendingCount);
    }

    [Fact]
    public void Frame_DueElement_WithoutOwner_IsSkipped_AndLedgerUntouched()
    {
        var rig = new Rig { OwnerKnown = false };
        var stroke = NewStroke();
        rig.Ledger.RecordAdd(stroke);
        rig.Core.Schedule(stroke, T0);
        rig.Controller.Refresh();

        rig.Frames.Fire();

        Assert.Empty(rig.Surface.Calls);
        Assert.Equal(1, rig.Ledger.Count);
    }

    [Fact]
    public void Stop_IsIdempotent_AndUnsubscribes()
    {
        var rig = new Rig();
        rig.State.FadingInk = true;
        rig.Controller.Refresh();

        rig.Controller.Stop();
        rig.Controller.Stop();

        Assert.Equal(1, rig.Frames.StopCount);
        Assert.Equal(0, rig.Frames.SubscriberCount);
        Assert.False(rig.Controller.IsAttached);
    }

    [Fact]
    public void FadeOutLength_Is700ms_Today() => Assert.Equal(TimeSpan.FromMilliseconds(700), RenderTickController.FadeOutLength);
}
