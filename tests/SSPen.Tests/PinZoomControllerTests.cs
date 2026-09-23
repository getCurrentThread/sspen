using SSPen.Interop;
using SSPen.Pin;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 핀 휠 확대/축소 적용 흐름의 헤드리스 증인 (82단계, 사용자 신고 2026-09-23: "휠로 확대/축소할 때마다 덜덜 떨린다").
/// 창·커서·디스패처는 가짜다 — 가짜 창은 <c>GetWindowRect</c>처럼 정수 사각형만 돌려주고, 적용은 그 사각형을 바꾼다.
/// 실제 HWND에서 칸마다 WM_WINDOWPOSCHANGED가 한 번인지는 통합 <c>PinZoomSmoothnessTests</c>가 본다.
/// </summary>
public class PinZoomControllerTests
{
    private static readonly PhysicalRect Region = new(760, 366, 400, 300);

    [Fact]
    public void Wheel_OneNotch_AppliesPositionAndSizeInOneCall()
    {
        var rig = new Rig(Region, cursor: (880, 576));

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        var call = Assert.Single(rig.Applies);
        Assert.NotEqual(Region.X, call.X);
        Assert.NotEqual(Region.Y, call.Y);
        Assert.Equal(440, call.Width);
        Assert.Equal(330, call.Height);
        Assert.Equal(1, rig.AppliedCallbacks);
    }

    /// <summary>대기 중인 휠 여러 칸은 예약 하나, 적용 하나로 묶인다 — 칸마다의 계산은 그대로 칸마다 한다.</summary>
    [Fact]
    public void Wheel_BurstBeforeTheApplyRuns_CoalescesIntoOneApply()
    {
        var rig = new Rig(Region, cursor: (880, 576));

        for (int i = 0; i < 10; i++)
        {
            rig.Zoom.Wheel(120);
        }
        Assert.Single(rig.Posted);
        rig.RunPosted();

        Assert.Single(rig.Applies);
        Assert.Equal(Math.Pow(PinZoom.StepFactor, 10), rig.Zoom.Scale, 9);
        Assert.Equal(PinZoom.ToPhysicalRect(rig.Zoom.Ideal), rig.Window);
        Assert.False(rig.Zoom.ApplyPending);
    }

    [Fact]
    public void Wheel_TenNotches_KeepsTheImagePointUnderTheCursorWithinOnePixel()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        double u = 880 - Region.X;
        double v = 576 - Region.Y;

        for (int i = 0; i < 10; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }
        var window = rig.Window!.Value;
        double drawnX = window.X + u * window.Width / Region.Width;
        double drawnY = window.Y + v * window.Height / Region.Height;

        Assert.InRange(drawnX - 880, -1, 1);
        Assert.InRange(drawnY - 576, -1, 1);
    }

    /// <summary>한 칸씩 적용해도 반올림이 되먹지 않는다 — N칸 확대 뒤 N칸 축소는 정확히 제자리다.</summary>
    [Fact]
    public void Wheel_TenInThenTenOutOneByOne_ReturnsToTheRegionExactly()
    {
        var rig = new Rig(Region, cursor: (880, 576));

        for (int i = 0; i < 10; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }
        for (int i = 0; i < 10; i++)
        {
            rig.Zoom.Wheel(-120);
            rig.RunPosted();
        }

        Assert.Equal(Region, rig.Window);
        Assert.Equal(1.0, rig.Zoom.Scale, 9);
    }

    [Fact]
    public void Reset_AfterZooming_ReturnsToThePhysicalBaseSizeAroundTheCenter()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        for (int i = 0; i < 3; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }
        var zoomed = rig.Ideal();
        rig.Applies.Clear();

        rig.Zoom.Reset();
        rig.RunPosted();

        var call = Assert.Single(rig.Applies);
        Assert.Equal(Region.Width, call.Width);
        Assert.Equal(Region.Height, call.Height);
        Assert.Equal(1.0, rig.Zoom.Scale);
        Assert.Equal(zoomed.Left + zoomed.Width / 2, rig.Zoom.Ideal.Left + rig.Zoom.Ideal.Width / 2, 9);
        Assert.Equal(zoomed.Top + zoomed.Height / 2, rig.Zoom.Ideal.Top + rig.Zoom.Ideal.Height / 2, 9);
    }

    /// <summary>
    /// 결과가 DPI와 무관하다 — 어느 배율의 모니터에서 잡은 캡처든 흐름 전체가 물리 픽셀이다. 옛 PinWindow는 물리 px인
    /// 캡처 크기를 DIP Width로 써서 150%에서는 첫 칸에 물리 폭이 1.1이 아니라 1.65배로 튀었고 원래 크기 복귀도 어긋났다.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Wheel_FirstNotchAndReset_ArePhysicalAtAnyDpi(double dpiScale)
    {
        // 논리 (100, 80, 320, 240)에 해당하는 캡처 영역 — 이 배율의 모니터에서 사용자가 끈 사각형이다.
        var region = CoordinateSpace.ToPhysical(new System.Windows.Rect(100, 80, 320, 240), dpiScale);
        var rig = new Rig(region, cursor: (region.X + region.Width / 2, region.Y + region.Height / 2));

        rig.Zoom.Wheel(120);
        rig.RunPosted();
        var first = rig.Window!.Value;
        rig.Zoom.Reset();
        rig.RunPosted();

        Assert.Equal(PinZoom.RoundEdge(region.Width * PinZoom.StepFactor), first.Width);
        Assert.Equal(PinZoom.RoundEdge(region.Height * PinZoom.StepFactor), first.Height);
        Assert.Equal(region, rig.Window);
    }

    /// <summary>드래그 이동 뒤 첫 칸: 옮겨진 실제 창에서 다시 잡아 커서 아래 점을 지킨다 (소수부는 보존).</summary>
    [Fact]
    public void Wheel_AfterAnExternalMove_AnchorsOnTheMovedWindow()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        rig.Zoom.Wheel(120);
        rig.RunPosted();
        var before = rig.Ideal();
        var moved = rig.Window!.Value with { X = rig.Window!.Value.X + 37, Y = rig.Window!.Value.Y - 23 };
        rig.Window = moved;

        rig.Zoom.Wheel(120);

        // 이동량만큼 옮겨진 이상적 사각형에서 커서 고정 확대를 한 것과 같다.
        var translated = before with { Left = before.Left + 37, Top = before.Top - 23 };
        var expected = PinZoom.ZoomAtCursor(
            translated, 120, Region.Width, Region.Height, 880 - translated.Left, 576 - translated.Top);
        Assert.Equal(expected, rig.Zoom.Ideal);
    }

    /// <summary>줌 밖의 크기 변화(DPI가 다른 모니터로 옮길 때 등): 보이는 폭에서 배율을 다시 잡아 튀지 않고 이어진다.</summary>
    [Fact]
    public void Wheel_AfterAnExternalResize_ContinuesFromTheVisibleSize()
    {
        var rig = new Rig(Region, cursor: (2100, 300));
        rig.Window = new PhysicalRect(1900, 80, 600, 450);

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        Assert.Equal(1.5 * PinZoom.StepFactor, rig.Zoom.Scale, 9);
        Assert.Equal(660, rig.Window!.Value.Width);
        Assert.Equal(495, rig.Window!.Value.Height);
    }

    /// <summary>
    /// OS가 크기를 제한해도 이상적 사각형을 버리지 않는다 — 결과를 마지막 적용으로 삼으므로 다음 칸이 그 차이를
    /// "밖에서 바뀜"으로 오판해 배율을 보이는 폭으로 되돌리지 않는다.
    /// </summary>
    [Fact]
    public void Apply_OsClampsTheSize_KeepsTheIdealScale()
    {
        var rig = new Rig(Region, cursor: (880, 576)) { MaxWidth = 500 };

        for (int i = 0; i < 4; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }

        Assert.Equal(Math.Pow(PinZoom.StepFactor, 4), rig.Zoom.Scale, 9);
        Assert.Equal(500, rig.Window!.Value.Width);
    }

    // ---- 95단계 (최종 리뷰: 82단계 회귀) — DPI 전환과 OS 크기 제한 구분, 범위 밖 크기에서의 칸 ----

    /// <summary>
    /// 한 칸 적용이 핀을 150% 모니터로 넘기면 WPF가 창을 1.5배로 다시 잡는다(SetWindowPos 안의 동기 WM_DPICHANGED).
    /// 그 크기는 OS 제한이 아니라 보이는 배율이다 — 이상적 사각형을 보이는 창에서 다시 잡고, 크롬 새로 그리기는 그 배율을 본다.
    /// 옛 코드는 결과 사각형을 마지막 적용으로만 적어 배율이 1.1에 남았고 라벨은 110%를 보였다.
    /// </summary>
    [Fact]
    public void Apply_DpiChangesInsideTheMove_AdoptsTheVisibleScaleBeforeTheChromeRefresh()
    {
        var rig = new Rig(Region, cursor: (880, 576)) { DpiRatioOnNextMove = 1.5 };

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        Assert.Equal(660, rig.Window!.Value.Width);
        Assert.Equal(660.0 / Region.Width, rig.Zoom.Scale, 9);
        Assert.Equal(rig.Window, PinZoom.ToPhysicalRect(rig.Zoom.Ideal));
        Assert.Equal(660.0 / Region.Width, Assert.Single(rig.ScalesSeenByChrome), 9);
    }

    /// <summary>
    /// DPI 전환 뒤의 확대 칸은 핀을 키운다 — 드래그로 같은 경계를 넘은 경로(다음 입력의 Resync가 보이는 배율을 채택)와
    /// 배율·창이 같다. 옛 코드는 낡은 배율 1.1에서 1.21로 걸어 660px 핀을 484px로 줄였다.
    /// </summary>
    [Fact]
    public void Wheel_ZoomInAfterADpiChangingApply_GrowsLikeTheDragPath()
    {
        var viaApply = new Rig(Region, cursor: (880, 576)) { DpiRatioOnNextMove = 1.5 };
        viaApply.Zoom.Wheel(120);
        viaApply.RunPosted();
        var crossed = viaApply.Window!.Value;
        // 같은 칸을 같은 모니터에서 적용한 뒤 드래그가 같은 보이는 사각형으로 넘긴다.
        var viaDrag = new Rig(Region, cursor: (880, 576));
        viaDrag.Zoom.Wheel(120);
        viaDrag.RunPosted();
        viaDrag.Window = crossed;

        viaApply.Zoom.Wheel(120);
        viaApply.RunPosted();
        viaDrag.Zoom.Wheel(120);
        viaDrag.RunPosted();

        Assert.True(viaApply.Window!.Value.Width > crossed.Width,
            $"확대 칸이 핀을 줄였다: {crossed.Width} → {viaApply.Window!.Value.Width}");
        Assert.Equal(viaDrag.Zoom.Scale, viaApply.Zoom.Scale, 9);
        Assert.Equal(viaDrag.Window, viaApply.Window);
    }

    /// <summary>
    /// DPI가 그대로인 적용에서 크기가 달라지면 OS 크기 제한이다 — 같은 1.5배 재조정이라도 DPI 알림이 없으면 이상적 배율을
    /// 지킨다(<see cref="Apply_OsClampsTheSize_KeepsTheIdealScale"/>와 같은 판정). DPI 알림 유무만이 둘을 가른다.
    /// </summary>
    [Fact]
    public void Apply_ResizedWithoutADpiChange_KeepsTheIdealAsAnOsLimit()
    {
        var rig = new Rig(Region, cursor: (880, 576)) { DpiRatioOnNextMove = 1.5, ReportsDpiChange = false };

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        Assert.Equal(660, rig.Window!.Value.Width);
        Assert.Equal(PinZoom.StepFactor, rig.Zoom.Scale, 9);
        Assert.Equal(PinZoom.StepFactor, Assert.Single(rig.ScalesSeenByChrome), 9);
    }

    /// <summary>
    /// 8배 핀을 150% 모니터로 드래그하면 보이는 폭이 기준의 12배다. 그 자리의 확대 칸은 핀을 줄이지 않고(이미 한계),
    /// 라벨의 배율은 800%다 — 옛 코드는 배율 12(1200%)에서 8로 걸어 핀을 1/3 줄였다.
    /// </summary>
    [Fact]
    public void Wheel_ZoomInFromAVisibleSizeAboveMaxScale_NeverShrinks()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        for (int i = 0; i < 40; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }
        var crossed = new PhysicalRect(1900, 80, 4800, 3600);
        rig.Window = crossed;
        rig.Cursor = (crossed.X + 2400, crossed.Y + 1800);
        rig.Applies.Clear();

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        Assert.Empty(rig.Applies);
        Assert.Equal(crossed, rig.Window);
        Assert.Equal(PinZoom.MaxScale, rig.Zoom.Scale);
    }

    /// <summary>대칭: 최소 배율 핀을 DPI가 낮은 모니터로 옮긴 자리의 축소 칸은 핀을 키우지 않는다.</summary>
    [Fact]
    public void Wheel_ZoomOutFromAVisibleSizeBelowMinScale_NeverGrows()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        for (int i = 0; i < 40; i++)
        {
            rig.Zoom.Wheel(-120);
            rig.RunPosted();
        }
        var crossed = new PhysicalRect(1900, 80, 27, 20);
        rig.Window = crossed;
        rig.Cursor = (crossed.X + 13, crossed.Y + 10);
        rig.Applies.Clear();

        rig.Zoom.Wheel(-120);
        rig.RunPosted();

        Assert.Empty(rig.Applies);
        Assert.Equal(crossed, rig.Window);
        Assert.Equal(PinZoom.MinScale, rig.Zoom.Scale);
    }

    /// <summary>범위 밖 크기에서의 원래 크기 복귀도 보이는 창의 중심을 고정한다 — 배율을 클램프해도 중심은 실제 폭에서 잰다.</summary>
    [Fact]
    public void Reset_FromAVisibleSizeAboveMaxScale_KeepsTheVisibleCenter()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        var crossed = new PhysicalRect(1900, 80, 4800, 3600);
        rig.Window = crossed;

        rig.Zoom.Reset();
        rig.RunPosted();

        Assert.Equal(new PhysicalRect(1900 + 2400 - 200, 80 + 1800 - 150, Region.Width, Region.Height), rig.Window);
    }

    [Fact]
    public void Wheel_AtMaxScale_DoesNotTouchTheWindow()
    {
        var rig = new Rig(new PhysicalRect(0, 0, 10, 10), cursor: (5, 5));
        for (int i = 0; i < 40; i++)
        {
            rig.Zoom.Wheel(120);
            rig.RunPosted();
        }
        rig.Applies.Clear();

        rig.Zoom.Wheel(120);
        rig.RunPosted();

        Assert.Equal(PinZoom.MaxScale, rig.Zoom.Scale, 9);
        Assert.Empty(rig.Applies);
    }

    /// <summary>창이 아직(또는 더는) 없으면 아무것도 하지 않는다 — Show 전 PinWindow의 휠이 그 경우다.</summary>
    [Fact]
    public void Wheel_NoWindow_SchedulesNothing()
    {
        var rig = new Rig(Region, cursor: (880, 576)) { Window = null };

        rig.Zoom.Wheel(120);
        rig.Zoom.Reset();

        Assert.Empty(rig.Posted);
        Assert.Equal(1.0, rig.Zoom.Scale);
    }

    [Fact]
    public void Wheel_CursorUnreadable_DropsTheNotch()
    {
        var rig = new Rig(Region, cursor: null);

        rig.Zoom.Wheel(120);

        Assert.Empty(rig.Posted);
        Assert.Equal(1.0, rig.Zoom.Scale);
    }

    [Fact]
    public void Apply_WindowClosedBeforeTheApplyRuns_DoesNotMove()
    {
        var rig = new Rig(Region, cursor: (880, 576));
        rig.Zoom.Wheel(120);
        rig.Window = null;

        rig.RunPosted();

        Assert.Empty(rig.Applies);
        Assert.Equal(0, rig.AppliedCallbacks);
        Assert.False(rig.Zoom.ApplyPending);
    }

    /// <summary>1px짜리 캡처도 배율 1.0의 기준 크기는 최소 변 길이다 (옛 PinWindow의 Math.Max(…, 8)과 같은 값).</summary>
    [Fact]
    public void Constructor_TinyRegion_UsesTheMinimumBaseExtent()
    {
        var rig = new Rig(new PhysicalRect(10, 10, 3, 5), cursor: (11, 12));

        Assert.Equal(PinZoom.MinBaseExtent, rig.Zoom.BaseWidth);
        Assert.Equal(PinZoom.MinBaseExtent, rig.Zoom.BaseHeight);
    }

    /// <summary>
    /// 가짜 창·커서·디스패처. 적용은 창 사각형을 바꾸고(<see cref="MaxWidth"/>면 OS처럼 폭을 자른다) 기록한다.
    /// <see cref="DpiRatioOnNextMove"/>가 있으면 다음 적용 한 번이 DPI가 다른 모니터로 넘어간다 — WPF가 권장 사각형을
    /// 적용하듯 크기에 그 비율을 곱하고, <see cref="ReportsDpiChange"/>대로 DPI 변화를 알린다.
    /// </summary>
    private sealed class Rig
    {
        public Rig(PhysicalRect region, (int X, int Y)? cursor)
        {
            Window = region;
            Cursor = cursor;
            Zoom = new PinZoomController(
                region,
                windowRect: () => Window,
                cursor: () => Cursor,
                moveResize: bounds =>
                {
                    Applies.Add(bounds);
                    var landed = bounds with { Width = Math.Min(bounds.Width, MaxWidth) };
                    if (DpiRatioOnNextMove is not { } ratio)
                    {
                        Window = landed;
                        return false;
                    }
                    DpiRatioOnNextMove = null;
                    Window = landed with
                    {
                        Width = PinZoom.RoundEdge(landed.Width * ratio),
                        Height = PinZoom.RoundEdge(landed.Height * ratio),
                    };
                    return ReportsDpiChange;
                },
                postCoalesced: Posted.Add,
                applied: () =>
                {
                    AppliedCallbacks++;
                    ScalesSeenByChrome.Add(Zoom!.Scale); // 콜백은 생성자가 끝난 뒤에만 불린다.
                });
        }

        /// <summary>다음 적용 한 번이 핀을 DPI가 다른 모니터로 넘긴다 — 새 DPI / 옛 DPI (예: 100% → 150%면 1.5).</summary>
        public double? DpiRatioOnNextMove { get; set; }

        /// <summary>그 적용이 DPI 변화를 알리는가. false면 같은 크기 변화가 DPI 없이 온다 — OS 크기 제한과 같은 모양이다.</summary>
        public bool ReportsDpiChange { get; init; } = true;

        /// <summary>적용 완료 콜백(크롬 새로 그리기)이 읽은 배율 — 라벨이 보일 %다.</summary>
        public List<double> ScalesSeenByChrome { get; } = [];

        public PinZoomController Zoom { get; }

        public PhysicalRect? Window { get; set; }

        public (int X, int Y)? Cursor { get; set; }

        public int MaxWidth { get; init; } = int.MaxValue;

        public List<PhysicalRect> Applies { get; } = [];

        public List<Action> Posted { get; } = [];

        public int AppliedCallbacks { get; private set; }

        public PinZoomResult Ideal() => Zoom.Ideal;

        /// <summary>디스패처 한 바퀴 — 예약된 적용을 돌린다.</summary>
        public void RunPosted()
        {
            var pending = Posted.ToArray();
            Posted.Clear();
            foreach (var action in pending)
            {
                action();
            }
        }
    }
}
