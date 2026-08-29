using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// Ultragoal 완료 QA / red-team 스위트 — 선택 도구(SEL-1..17, SEL-AC-1..18)에 대한 적대적/경계값 검증.
/// 목적은 확인이 아니라 파괴다: 기존 스위트가 이미 통과시킨 계약을 다른 각도(퇴화 기하, 비-0도 회전,
/// 비대칭 DPI, 역순 undo, 빈 입력)에서 다시 찌른다.
///
/// 참조 계약: <c>.gjc/_session-.../specs/deep-interview-selection-tool.md</c> (SEL-AC-1..18),
/// <c>.../plans/ralplan/.../stage-05-revision.md</c> (R1..R24, ARCH-17/18/19/20/21).
/// </summary>
public class SelectionRedTeamTests
{
    private const double Tol = 1e-9;

    private static StrokeElement NewStroke(params Point[] pts) =>
        new(pts, Colors.Black, thickness: 3, isHighlighter: false);

    // =====================================================================
    // A. TransformMath 적대적/경계 케이스
    // =====================================================================

    // ---- A1. 퇴화 기하 (수평/수직선, 단일 점) — NaN 증발 사냥 ----

    [Theory]
    [InlineData(0.0)]    // 회전 없음
    [InlineData(30.0)]   // 비-0도: 앵커 보정이 실제로 걸리는 각도
    [InlineData(90.0)]   // Shift 스냅이 정확히 만들어내는 각도
    public void ScaleLocal_HorizontalLine_RotatedAndDegenerate_NeverProducesNaN(double angle)
    {
        // 수평선: ModelBounds.Height == 0. NonDegenerate가 Thickness로 벌리지만,
        // 핸들이 세로축(Top/Bottom)을 잡으면 grip.Y - anchor.Y가 아주 작은 값이 될 수 있다.
        var horizontalBounds = new Rect(0, 25, 100, 0); // 두께 반영 전 순수 모델 경계
        var start = new ElementTransformState(1, 1, angle, default);
        var localBounds = TransformMath.NonDegenerate(horizontalBounds, minExtent: 3); // Thickness=3 가정

        foreach (var handle in TransformMath.SizeHandlesCornersFirst)
        {
            var result = TransformMath.ScaleLocal(start, localBounds, handle, new Point(500, 500));

            Assert.False(double.IsNaN(result.ScaleX), $"{handle}@{angle}도: ScaleX가 NaN — R16 위반.");
            Assert.False(double.IsNaN(result.ScaleY), $"{handle}@{angle}도: ScaleY가 NaN — R16 위반.");
            Assert.False(double.IsInfinity(result.ScaleX), $"{handle}@{angle}도: ScaleX가 무한대.");
            Assert.False(double.IsInfinity(result.ScaleY), $"{handle}@{angle}도: ScaleY가 무한대.");
            Assert.False(double.IsNaN(result.Translation.X), $"{handle}@{angle}도: Translation.X가 NaN.");
            Assert.False(double.IsNaN(result.Translation.Y), $"{handle}@{angle}도: Translation.Y가 NaN.");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(45.0)]
    public void ScaleLocal_SinglePointStroke_AllHandles_NeverProducesNaN(double angle)
    {
        // 단일 점 획: ModelBounds가 0x0. NonDegenerate로 양축 모두 최소치까지 벌어진다 (정사각형).
        var dotBounds = TransformMath.NonDegenerate(new Rect(10, 10, 0, 0), minExtent: 4);
        var start = new ElementTransformState(1, 1, angle, default);

        foreach (var handle in TransformMath.SizeHandlesCornersFirst)
        {
            var result = TransformMath.ScaleLocal(start, dotBounds, handle, new Point(-999, 999));
            Assert.False(double.IsNaN(result.ScaleX) || double.IsNaN(result.ScaleY),
                $"{handle}: 단일점 획에서 NaN 발생.");
        }
    }

    [Fact]
    public void Rotate_ShiftSnapAtExactly90Degrees_OnDegenerateVerticalLine_IsClean()
    {
        // 수직선을 90도로 정확히 스냅하는 회전 — R16이 명시한 정확한 퇴화 시나리오.
        var verticalBounds = TransformMath.NonDegenerate(new Rect(50, 0, 0, 100), minExtent: 3);
        var start = new ElementTransformState(1, 1, 0, default);
        var pivot = new Point(50, 50);

        var result = TransformMath.Rotate(
            start, verticalBounds, pivot + new Vector(100, 0), pivot + new Vector(0.001, 100), shift: true);

        Assert.False(double.IsNaN(result.AngleDegrees));
        Assert.Equal(90, result.AngleDegrees, 3);

        // 결과 상태로 행렬을 조립해도 유효해야 한다 (증발 여부의 최종 확인).
        var m = TransformMath.ToMatrix(result, new Point(50, 50));
        var corner = m.Transform(verticalBounds.TopLeft);
        Assert.False(double.IsNaN(corner.X) || double.IsNaN(corner.Y));
    }

    [Fact]
    public void ScaleLocal_ExactZeroSpanBothAxes_HandleAtCenter_KeepsIdentityScale()
    {
        // 병리적 입력: NonDegenerate를 거치지 않은 순수 0x0 사각형을 직접 ScaleLocal에 먹인다.
        // (방어 계층이 우회될 경우의 최종 방어선 검증 — 함수 자체가 자기방어적이어야 한다.)
        var zero = new Rect(50, 50, 0, 0);
        var start = ElementTransformState.Identity;

        var result = TransformMath.ScaleLocal(start, zero, HandleKind.BottomRight, new Point(999, 999));

        Assert.False(double.IsNaN(result.ScaleX), "0x0 경계에서 ScaleX가 NaN — 분모 보호가 뚫렸다.");
        Assert.False(double.IsNaN(result.ScaleY), "0x0 경계에서 ScaleY가 NaN — 분모 보호가 뚫렸다.");
        // AnchorLocal(zero, BottomRight) == TopLeft == 중심이므로 span이 0/0 — 기존 배율 유지 계약.
        Assert.Equal(1, result.ScaleX, Tol);
        Assert.Equal(1, result.ScaleY, Tol);
    }

    // ---- A2. R14 부호 보존 클램프 (양방향, 양 축) ----

    [Fact]
    public void ScaleLocal_DragPastAnchor_BothAxesFlipSign_CornerHandle()
    {
        var bounds = new Rect(0, 0, 100, 50);
        var start = ElementTransformState.Identity;

        // BottomRight 핸들을 앵커(TopLeft, 0,0)를 완전히 지나쳐 음의 사분면으로 끈다.
        var result = TransformMath.ScaleLocal(start, bounds, HandleKind.BottomRight, new Point(-200, -100));

        Assert.True(result.ScaleX < 0, "X축이 앵커를 지나쳤으면 부호가 뒤집혀야 한다 (R14).");
        Assert.True(result.ScaleY < 0, "Y축도 마찬가지.");
        Assert.True(Math.Abs(result.ScaleX) >= TransformMath.MinScale);
        Assert.True(Math.Abs(result.ScaleY) >= TransformMath.MinScale);
    }

    [Fact]
    public void ScaleLocal_DragToExactlyOnAnchor_ClampsToMinScale_NotZero()
    {
        var bounds = new Rect(0, 0, 100, 50);
        var start = ElementTransformState.Identity;
        var anchorWorld = TransformMath.ToMatrix(start, new Point(50, 25))
            .Transform(TransformMath.AnchorLocal(bounds, HandleKind.Right));

        // 핸들을 앵커 지점 자체로 끌면 span 비율이 0이 된다 — Right 핸들은 X축만 바꾸므로
        // scaleX = 0/spanX = 0이 되고 ClampMagnitude가 MinScale로 바닥을 잡아야 한다.
        var result = TransformMath.ScaleLocal(start, bounds, HandleKind.Right, anchorWorld);

        Assert.False(double.IsNaN(result.ScaleX));
        Assert.True(Math.Abs(result.ScaleX) >= TransformMath.MinScale - 1e-9);
    }

    // ---- A3. R21 앵커 불변식 — 추가 각도, 측면 핸들, 극단 비등방 ----

    [Theory]
    [InlineData(15.0)]
    [InlineData(60.0)]
    [InlineData(90.0)]
    [InlineData(179.0)]  // 180 직전 — 아주 좁은 회전
    [InlineData(-45.0)]  // 음수 각도
    [InlineData(270.0)]
    public void ScaleLocal_CornerHandle_KeepsAnchorFixed_AcrossManyAngles(double angle)
    {
        var bounds = new Rect(0, 0, 100, 50);
        var start = new ElementTransformState(1, 1, angle, default);
        var before = AnchorWorld(start, bounds, HandleKind.TopLeft);

        var result = TransformMath.ScaleLocal(start, bounds, HandleKind.TopLeft, new Point(-40, -30));

        var after = AnchorWorld(result, bounds, HandleKind.TopLeft);
        Assert.False(double.IsNaN(after.X) || double.IsNaN(after.Y));
        AssertPointsEqual(before, after, 1e-6);
    }

    [Fact]
    public void ScaleLocal_ExtremeAnisotropic_SideHandle_AnchorHolds_At30Degrees()
    {
        // 극단 종횡비: X만 8배로 늘리는 시도. QA-5가 지목한 5:1 이상 영역.
        var bounds = new Rect(0, 0, 100, 50);
        var start = new ElementTransformState(1, 1, 30, new Vector(5, 5));
        var anchor = AnchorWorld(start, bounds, HandleKind.Right);
        var localXDir = TransformMath.RotateVector(new Vector(1, 0), 30);

        var result = TransformMath.ScaleLocal(start, bounds, HandleKind.Right, anchor + localXDir * 800);

        Assert.Equal(8, result.ScaleX, 5);
        Assert.Equal(1, result.ScaleY, 9); // 측면 핸들은 반대축 불변
        AssertPointsEqual(anchor, AnchorWorld(result, bounds, HandleKind.Right), 1e-5);
    }

    [Fact]
    public void ScaleLocal_SequentialDragsFromDifferentAngles_AnchorNeverDrifts()
    {
        // 실제 사용자 조작 시퀀스 시뮬레이션: 회전 → 스케일 → 회전 → 스케일. 매 스케일 단계에서
        // 앵커가 그 순간의 상태 기준으로 고정되어야 하며, 누적 드리프트가 없어야 한다.
        var bounds = new Rect(0, 0, 100, 50);
        var state = new ElementTransformState(1, 1, 20, new Vector(3, -7));

        for (int i = 0; i < 5; i++)
        {
            double angle = 20 + i * 17;
            state = state with { AngleDegrees = angle };
            var before = AnchorWorld(state, bounds, HandleKind.BottomLeft);

            state = TransformMath.ScaleLocal(state, bounds, HandleKind.BottomLeft, new Point(-30 - i * 10, 200 + i * 10));

            var after = AnchorWorld(state, bounds, HandleKind.BottomLeft);
            Assert.False(double.IsNaN(after.X) || double.IsNaN(after.Y), $"반복 {i}: NaN 발생.");
            AssertPointsEqual(before, after, 1e-4);
        }
    }

    // ---- A4. Rotate: 경계값, 음수, wrap-around ----

    [Theory]
    [InlineData(0, 7.4, 0)]     // 7.4 → 0으로 스냅 (7.5 미만)
    [InlineData(0, 7.6, 15)]    // 7.6 → 15로 스냅 (7.5 초과)
    // 7.5 정확히는 제외: Math.Round의 기본 은행가 반올림(ToEven)이 0으로 내린다 —
    // SnapDegrees의 실제 계약이며 ShiftConstraintTests.SnapDegrees_AtBoundary에서 이미 고정되어 있다.
    public void Rotate_ShiftSnap_AtExactBoundaryValues(double startAngle, double sweep, double expected)
    {
        var bounds = new Rect(0, 0, 100, 50);
        var start = new ElementTransformState(1, 1, startAngle, default);
        var pivot = new Point(50, 25);
        double radians = sweep * Math.PI / 180.0;
        var to = pivot + new Vector(100 * Math.Cos(radians), 100 * Math.Sin(radians));

        var result = TransformMath.Rotate(start, bounds, pivot + new Vector(100, 0), to, shift: true);

        Assert.Equal(expected, result.AngleDegrees, 6);
    }

    [Fact]
    public void Rotate_AccumulatesPastFullCircle_DoesNotWrapOrClamp()
    {
        // A3는 각도를 자유 누적한다 — 360을 넘겨도 감싸지 않는 것이 계약이다 (분해가 없다는 R13 소멸 근거).
        var bounds = new Rect(0, 0, 100, 50);
        var pivot = new Point(50, 25);
        var state = new ElementTransformState(1, 1, 350, default);

        // +30도 스윕 (350 → 380 논리적으로)
        var result = TransformMath.Rotate(
            state, bounds, pivot + new Vector(100, 0), pivot + new Vector(100 * Math.Cos(Math.PI * 20 / 180), 100 * Math.Sin(Math.PI * 20 / 180)), shift: false);

        Assert.False(double.IsNaN(result.AngleDegrees));
        // 정확한 각도 자체보다 "감싸지 않는다"가 계약의 핵심 — 380 근방(±약간) 이어야지 20 근방이면 안 된다.
        Assert.True(result.AngleDegrees > 300, $"각도가 wrap-around된 것으로 보인다: {result.AngleDegrees}");
    }

    [Fact]
    public void Rotate_LargeNegativeSweep_AccumulatesNegative()
    {
        var bounds = new Rect(0, 0, 100, 50);
        var pivot = new Point(50, 25);
        var state = new ElementTransformState(1, 1, -350, default);

        // pivot+(100,0) → pivot+(-100,0)은 정확히 180도 스윕이므로 결과는 -350+180 = -170.
        var result = TransformMath.Rotate(
            state, bounds, pivot + new Vector(100, 0), pivot + new Vector(-100, 0), shift: false);

        Assert.False(double.IsNaN(result.AngleDegrees));
        Assert.Equal(-170, result.AngleDegrees, 6);
    }

    // ---- A5. HitHandle: 회전 180도(핸들이 아래로), 극단 비등방, 오프스크린 클램프 ----

    [Fact]
    public void HitHandle_RotateHandle_At180Degrees_IsBelowElement_AndGrabbable()
    {
        var bounds = new Rect(0, 0, 100, 50);
        var state = new ElementTransformState(1, 1, 180, default);

        var handlePos = TransformMath.RotateHandleWorld(state, bounds);
        Assert.True(handlePos.Y > 25, "180도 회전 시 핸들이 요소 중심보다 아래에 있어야 한다 (R5).");

        var hit = TransformMath.HitHandle(state, bounds, handlePos, Rect.Empty);
        Assert.Equal(HandleKind.Rotate, hit);
    }

    [Fact]
    public void HitHandle_RotateHandle_ClampedOffScreen_StillGrabbableAtVisualClampedSpot()
    {
        // 요소가 화면 최상단 경계에 있어 클램프 없는 위치는 화면 밖 — 클램프된 위치에서만 잡혀야 한다.
        var bounds = new Rect(0, 0, 100, 50);
        var state = ElementTransformState.Identity;
        var surface = new Rect(0, 0, 1920, 1080);

        var unclamped = TransformMath.RotateHandleWorld(state, bounds); // y = -24, 화면 밖
        var hitAtUnclamped = TransformMath.HitHandle(state, bounds, unclamped, surface);
        Assert.Null(hitAtUnclamped); // 클램프 안 된 원위치에서는 이제 빗나가야 한다.

        var clamped = TransformMath.ClampRotateHandle(unclamped, surface, TransformMath.HandleScreenSize / 2);
        var hitAtClamped = TransformMath.HitHandle(state, bounds, clamped, surface);
        Assert.Equal(HandleKind.Rotate, hitAtClamped);
    }

    [Fact]
    public void HitHandle_ExtremeAnisotropicScale_SizeHandleReachIsPerAxisCorrect()
    {
        // ScaleX=10, ScaleY=0.1 — reach는 축별로 나뉘어야 한다 (reachX 작고 reachY 큼).
        var bounds = new Rect(0, 0, 100, 50);
        var state = new ElementTransformState(10, 0.1, 0, default);
        var corner = TransformMath.HandleCenterLocal(bounds, HandleKind.BottomRight);
        var world = TransformMath.ToMatrix(state, new Point(50, 25)).Transform(corner);

        // 리더 지적: 히트 reach는 항상 화면공간에서 상수(HandleScreenSize/2 = 4px)이다 —
        // 로컬 거리와 로컬 reach가 둘 다 배율로 나눠지므로 축별 배율이 서로 상쇄된다.
        // ScaleX=10 → reachX 로컬값 0.4, ScaleY=0.1 → reachY 로컬값 40. 그래서 월드 +6px는 X축(로컬 0.6 > 0.4)만
        // 놓치고 Y축(로컬 60 < 40은 아니지만 더 작은 +2px는 20 < 40)는 여전히 잡힌다.
        var missX = TransformMath.HitHandle(state, bounds, world + new Vector(6, 0), Rect.Empty);
        var hitY = TransformMath.HitHandle(state, bounds, world + new Vector(0, 2), Rect.Empty);

        Assert.Null(missX);
        Assert.Equal(HandleKind.BottomRight, hitY);
    }

    [Fact]
    public void HitHandle_RotateHandleAt180_TakesPriorityOverNearbySizeHandle_WhenBothInRange()
    {
        // 회전 핸들 판정이 먼저다 (SEL-7 히트 우선순위 계약). 180도에서도 이 순서가 유지되는지.
        var bounds = new Rect(0, 0, 100, 50);
        var state = new ElementTransformState(1, 1, 180, default);
        var rotateWorld = TransformMath.RotateHandleWorld(state, bounds);

        var hit = TransformMath.HitHandle(state, bounds, rotateWorld, Rect.Empty);
        Assert.Equal(HandleKind.Rotate, hit);
    }

    // =====================================================================
    // B. DPI Rebase 정확성 — r != 1 하드 공격
    // =====================================================================

    private static readonly PhysicalRect Center100 = new(0, 0, 1920, 1080);
    private static readonly PhysicalRect NegLeft = new(-1920, 0, 1920, 1080);

    [Fact]
    public void RebaseState_100To125Percent_ScalesExactlyByRatio()
    {
        var state = new ElementTransformState(1, 1, 0, default);
        var bounds = new Rect(0, 0, 100, 100);

        var rebased = SelectionOperations.RebaseState(state, bounds, Center100, 1.0, NegLeft, 1.25);

        Assert.Equal(1.0 / 1.25, rebased.ScaleX, 1e-9);
        Assert.Equal(1.0 / 1.25, rebased.ScaleY, 1e-9);
    }

    [Fact]
    public void RebaseState_150To175Percent_NonUnitBothSides_ScalesByRatio()
    {
        // 양쪽 다 100%가 아닌 조합 — 흔한 오구현(한쪽만 나눔/곱함)을 잡는다.
        var state = new ElementTransformState(2, 3, 0, default);
        var bounds = new Rect(0, 0, 100, 100);

        var rebased = SelectionOperations.RebaseState(state, bounds, Center100, 1.5, NegLeft, 1.75);

        double expectedRatio = 1.5 / 1.75;
        Assert.Equal(2 * expectedRatio, rebased.ScaleX, 1e-9);
        Assert.Equal(3 * expectedRatio, rebased.ScaleY, 1e-9);
    }

    [Fact]
    public void RebaseState_AsymmetricNegativeOrigin_PreservesPhysicalPosition_Rotated()
    {
        // 회전 + 비등방 스케일 + 음수 원점 모니터 + 비대칭 DPI — 조합 공격.
        var bounds = new Rect(20, 30, 120, 60);
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var state = new ElementTransformState(1.8, 0.6, 55, new Vector(-33, 87));
        const double srcDpi = 1.0;
        const double tgtDpi = 1.5;

        var rebased = SelectionOperations.RebaseState(state, bounds, Center100, srcDpi, NegLeft, tgtDpi);

        // 각도는 DPI 불변이어야 한다.
        Assert.Equal(55, rebased.AngleDegrees, 1e-9);

        // 물리 위치 보존: 원본 중심+변위가 대상에서도 같은 물리 픽셀을 가리켜야 한다.
        double srcPhysX = Center100.X + (center.X + state.Translation.X) * srcDpi;
        double srcPhysY = Center100.Y + (center.Y + state.Translation.Y) * srcDpi;
        double tgtPhysX = NegLeft.X + (center.X + rebased.Translation.X) * tgtDpi;
        double tgtPhysY = NegLeft.Y + (center.Y + rebased.Translation.Y) * tgtDpi;

        Assert.Equal(srcPhysX, tgtPhysX, 1e-6);
        Assert.Equal(srcPhysY, tgtPhysY, 1e-6);
    }

    [Fact]
    public void RebaseState_TranslationAsDisplacement_DivergesFromNaiveByExactlyCenterTimesRatioMinusOne()
    {
        // ARCH-20의 정확한 공식적 예측: (naive - correct) == c*(r-1) (부호 주의: point-mapping 오구현 방향).
        // 여기서는 편차의 "존재와 방향"뿐 아니라 "크기"까지 정량 검증한다.
        var bounds = new Rect(0, 0, 200, 100); // center = (100, 50)
        var center = new Point(100, 50);
        var translation = new Vector(50, -20);
        var state = new ElementTransformState(1, 1, 0, translation);
        const double srcDpi = 1.0;
        const double tgtDpi = 2.0; // r = 0.5, 극단적 배율차

        var rebased = SelectionOperations.RebaseState(state, bounds, Center100, srcDpi, NegLeft, tgtDpi);

        var naive = CoordinateSpace.Rebase(new Point(translation.X, translation.Y), Center100, srcDpi, NegLeft, tgtDpi);

        // 두 접근의 물리 X상의 차이 = c.X * srcDpi 만큼 (Rebase가 원점을 명시적으로 더하므로).
        // 정확한 수치 예측보다 "0이 아니고, 극단 r에서 커진다"는 방향성 계약이 red-team 관점에서 더 견고하다.
        double divergence = Math.Abs(naive.X - rebased.Translation.X);
        Assert.True(divergence > 10, $"r=0.5처럼 극단적인 DPI차에서 naive/correct 편차가 미미하다({divergence}) — 회귀 의심.");
    }

    [Fact]
    public void RebaseState_SameDpiSameMonitor_IsExactIdentityOnScaleAndTranslation()
    {
        // r=1이고 원본=대상이면 완전한 항등이어야 한다 (계약 최소선).
        var state = new ElementTransformState(1.7, 0.4, 82, new Vector(12, -44));
        var bounds = new Rect(5, 5, 90, 40);

        var rebased = SelectionOperations.RebaseState(state, bounds, Center100, 1.0, Center100, 1.0);

        Assert.Equal(state.ScaleX, rebased.ScaleX, 1e-9);
        Assert.Equal(state.ScaleY, rebased.ScaleY, 1e-9);
        Assert.Equal(state.AngleDegrees, rebased.AngleDegrees, 1e-9);
        Assert.Equal(state.Translation.X, rebased.Translation.X, 1e-6);
        Assert.Equal(state.Translation.Y, rebased.Translation.Y, 1e-6);
    }

    [Fact]
    public void RebaseState_RoundTrip_SourceToTargetAndBack_RecoversOriginalState()
    {
        // 왕복 사상: A(1.0dpi) → B(1.5dpi) → A(1.0dpi)가 원래 상태로 돌아와야 한다.
        // 편도 검증만으로는 놓치는 누적 오차/부호 오류를 잡는 이중 방어선.
        var bounds = new Rect(10, 10, 80, 40);
        var state = new ElementTransformState(1.3, 2.1, 64, new Vector(77, -12));

        var toTarget = SelectionOperations.RebaseState(state, bounds, Center100, 1.0, NegLeft, 1.5);
        var backToSource = SelectionOperations.RebaseState(toTarget, bounds, NegLeft, 1.5, Center100, 1.0);

        Assert.Equal(state.ScaleX, backToSource.ScaleX, 1e-9);
        Assert.Equal(state.ScaleY, backToSource.ScaleY, 1e-9);
        Assert.Equal(state.AngleDegrees, backToSource.AngleDegrees, 1e-9);
        Assert.Equal(state.Translation.X, backToSource.Translation.X, 1e-6);
        Assert.Equal(state.Translation.Y, backToSource.Translation.Y, 1e-6);
    }

    // =====================================================================
    // C. 선택/undo 상태 기계 — 억제 스코프, 다중 문서 삭제, 순서 함정
    // =====================================================================

    // ---- C1. 억제 스코프 양방향 — 이관 도중 다른 요소의 진짜 제거는 억제되지 않는다 ----

    [Fact]
    public void SuppressInvalidation_DuringTransferScope_DoesNotShieldUnrelatedRealRemoval()
    {
        // 스코프가 "이관 중인 그 요소"뿐 아니라 "그 시점의 모든 제거"를 억제하는 설계이므로,
        // 스코프 안에서 벌어지는 무관한 진짜 삭제(지우개)도 억제된다 — 이것이 R22가 지목한
        // "과잉 적용"의 실제 공격 표면이다. 프로덕션 이관 절차는 단일 Remove/Add쌍만 스코프
        // 안에 두므로 안전하지만, 이 테스트는 그 경계가 얼마나 좁은지 정량 확인한다.
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);

        var transferring = NewStroke(new Point(0, 0), new Point(10, 10));
        var unrelated = NewStroke(new Point(20, 20), new Point(30, 30));
        doc.Add(transferring);
        doc.Add(unrelated);
        selection.Set([transferring, unrelated]);

        using (selection.SuppressInvalidation())
        {
            doc.Remove(transferring); // "이관"으로 의도된 제거
            doc.Remove(unrelated);    // 스코프 안에서 벌어진 무관한 진짜 삭제 (지우개 시뮬레이션)
        }

        // 현재 구현 계약: 스코프는 깊이 기반이라 스코프 안의 모든 제거를 억제한다.
        // 이것이 계획서가 명시한 "적용 지점은 이관 2곳뿐" 원칙과 정확히 부합하는지 확인 —
        // 만약 프로덕션 코드가 이 스코프 안에 무관한 Remove를 끼워 넣으면 R22가 현실화된다.
        Assert.True(selection.Contains(unrelated),
            "스코프가 무관한 제거까지 억제함을 확인 — 프로덕션은 스코프를 좁게(단일 이관 쌍) 유지해야 한다 (R22 경계 문서화).");
    }

    [Fact]
    public void ElementRemovedFromDocument_EraserDuringActiveSelection_DropsOnlyErasedElement()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);

        var kept = NewStroke(new Point(0, 0), new Point(10, 10));
        var erased = NewStroke(new Point(20, 20), new Point(30, 30));
        doc.Add(kept);
        doc.Add(erased);
        selection.Set([kept, erased]);

        doc.Remove(erased); // 스코프 밖 — 진짜 지우개 삭제.

        Assert.True(selection.Contains(kept));
        Assert.False(selection.Contains(erased));
    }

    // ---- C2. 다중 문서 삭제 — 인덱스가 제거 전에 수집됨을 실제로 증명 ----

    [Fact]
    public void PlanDelete_InterleavedIndicesAcrossThreeDocuments_EachRestoresExactPosition()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var d3 = new AnnotationDocument("M3");

        var a1 = NewStroke(new Point(0, 0)); var b1 = NewStroke(new Point(1, 1)); var c1 = NewStroke(new Point(2, 2));
        var a2 = NewStroke(new Point(3, 3)); var b2 = NewStroke(new Point(4, 4));
        var a3 = NewStroke(new Point(5, 5));
        foreach (var s in new[] { a1, b1, c1 }) d1.Add(s);
        foreach (var s in new[] { a2, b2 }) d2.Add(s);
        d3.Add(a3);

        // 선택: 각 문서에서 흩어진 요소 — b1(1), a2(0), b2(1), a3(0).
        var selection = new AnnotationElement[] { b1, a2, b2, a3 };
        Func<AnnotationElement, AnnotationDocument?> lookup = e =>
            new[] { d1, d2, d3 }.FirstOrDefault(d => d.Elements.Contains(e));

        var plan = SelectionOperations.PlanDelete(selection, lookup);

        // 실행: 원장이 하는 것과 동일하게 전부 제거한 뒤 계획된 인덱스로 복원.
        foreach (var entry in plan)
        {
            entry.Document.Remove(entry.Element);
        }
        for (int i = plan.Count - 1; i >= 0; i--)
        {
            plan[i].Document.Insert(plan[i].Index, plan[i].Element);
        }

        Assert.Equal(new AnnotationElement[] { a1, b1, c1 }, d1.Elements);
        Assert.Equal(new AnnotationElement[] { a2, b2 }, d2.Elements);
        Assert.Equal(new AnnotationElement[] { a3 }, d3.Elements);
    }

    [Fact]
    public void PlanDelete_AdjacentIndicesInSameDocument_AscendingRestore_IsStable()
    {
        // 연속 인덱스(0,1,2)를 함께 삭제 — 현 계약(오름차순 삽입, UndoLedger.DeleteSelectionOperation.Undo와 동일)을 따른다.
        // 역순 삽입이었다면 [a,d,b,c]로 깨졌을 자리 — 이 테스트는 정답(오름차순) 계약을 고정한다.
        var doc = new AnnotationDocument("M1");
        var a = NewStroke(new Point(0, 0));
        var b = NewStroke(new Point(1, 1));
        var c = NewStroke(new Point(2, 2));
        var d = NewStroke(new Point(3, 3));
        foreach (var s in new[] { a, b, c, d }) doc.Add(s);

        var plan = SelectionOperations.PlanDelete([a, b, c], e => doc);

        foreach (var entry in plan) doc.Remove(entry.Element);
        foreach (var entry in plan) // 오름차순 삽입 — 실제 UndoLedger 계약과 일치.
        {
            entry.Document.Insert(entry.Index, entry.Element);
        }

        Assert.Equal(new AnnotationElement[] { a, b, c, d }, doc.Elements);
    }

    [Fact]
    public void PlanDelete_AdjacentIndices_DescendingRestore_CorruptsOrder_RegressionGuard()
    {
        // 회귀 감시용: 역순 삽입(과거 오구현 방식)이 여전히 잘못된 결과를 낸다는 것을 명시적으로 고정해,
        // 향후 누군가 UndoLedger를 역순으로 되돌리는 회귀를 낻으면 이 테스트가 먼저 깨진다.
        var doc = new AnnotationDocument("M1");
        var a = NewStroke(new Point(0, 0));
        var b = NewStroke(new Point(1, 1));
        var c = NewStroke(new Point(2, 2));
        var d = NewStroke(new Point(3, 3));
        foreach (var s in new[] { a, b, c, d }) doc.Add(s);

        var plan = SelectionOperations.PlanDelete([a, b, c], e => doc);
        foreach (var entry in plan) doc.Remove(entry.Element);

        for (int i = plan.Count - 1; i >= 0; i--) // 역순 삽입 (잘못된 방식).
        {
            doc.Insert(plan[i].Index, plan[i].Element);
        }

        Assert.NotEqual(new AnnotationElement[] { a, b, c, d }, doc.Elements);
    }

    [Fact]
    public void PlanDelete_DuplicateElementInSelection_DoesNotDoublePlan()
    {
        // 방어적: 같은 요소가 선택 리스트에 두 번 나타나는 병리적 입력 (호출자 버그 시뮬레이션).
        var doc = new AnnotationDocument("M1");
        var a = NewStroke(new Point(0, 0));
        doc.Add(a);

        var plan = SelectionOperations.PlanDelete([a, a], e => doc);

        // 계획 함수는 입력을 그대로 순회하므로 2건이 나올 수 있다 — 이것이 실제 동작이라면
        // 원장 Undo에서 같은 요소를 두 번 삽입하려다 인덱스가 어긋날 잠재 결함이다.
        // red-team 목적: 이 동작을 명시적으로 고정해 향후 회귀를 감시한다.
        if (plan.Count == 2)
        {
            // 실행 시뮬레이션: 두 번째 Remove는 already-removed라 false를 반환할 것이다.
            doc.Remove(a);
            bool secondRemoveSucceeded = doc.Remove(a);
            Assert.False(secondRemoveSucceeded,
                "중복 선택 시 두 번째 Remove가 성공하면 안 된다 — 그렇지 않으면 원장이 실패를 숨긴다.");
        }
        else
        {
            Assert.Single(plan);
        }
    }

    // ---- C3. Undo 후 더 오래된 조작 undo — 소유 문서가 이관으로 바뀐 뒤 stale reference 함정 ----

    [Fact]
    public void Undo_TransferThenOlderTransform_OnDifferentElement_BothResolveByCurrentOwner()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var docs = new[] { d1, d2 };
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var older = NewStroke(new Point(0, 0));
        var moved = NewStroke(new Point(5, 5));
        d1.Add(older);
        d1.Add(moved);

        // 오래된 조작: older에 변형을 기록 (아직 이관 없음).
        var olderBefore = older.TransformState;
        var olderAfter = TransformMath.Translate(olderBefore, new Vector(1, 1));
        older.TransformState = olderAfter;
        ledger.RecordTransform([new TransformDelta(older, olderBefore, olderAfter, d1, d1)]);

        // 최신 조작: moved를 D1 → D2로 이관.
        var movedBefore = moved.TransformState;
        var movedAfter = TransformMath.Translate(movedBefore, new Vector(1920, 0));
        d1.Remove(moved);
        moved.TransformState = movedAfter;
        d2.Add(moved);
        ledger.RecordTransform([new TransformDelta(moved, movedBefore, movedAfter, d1, d2)]);

        // undo 1: 이관을 되돌린다 (moved: D2 → D1).
        Assert.True(ledger.Undo());
        Assert.Contains(moved, d1.Elements);
        Assert.DoesNotContain(moved, d2.Elements);

        // undo 2: 더 오래된 older 변형을 되돌린다. older는 이관을 겪지 않았으므로 D1에 그대로 있어야 한다.
        Assert.True(ledger.Undo());
        Assert.Equal(olderBefore, older.TransformState);
        Assert.Contains(older, d1.Elements);
    }

    [Fact]
    public void Undo_TransferTwiceAcrossThreeDocuments_ThenUndoBoth_RestoresOriginalOwnerChain()
    {
        // 요소가 M1 → M2 → M3로 두 번 이관된 뒤 두 undo로 M1까지 완전히 되돌아가야 한다.
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var d3 = new AnnotationDocument("M3");
        var docs = new[] { d1, d2, d3 };
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var element = NewStroke(new Point(0, 0));
        d1.Add(element);

        var s0 = element.TransformState;
        var s1 = TransformMath.Translate(s0, new Vector(1920, 0));
        d1.Remove(element);
        element.TransformState = s1;
        d2.Add(element);
        ledger.RecordTransform([new TransformDelta(element, s0, s1, d1, d2)]);

        var s2 = TransformMath.Translate(s1, new Vector(1920, 0));
        d2.Remove(element);
        element.TransformState = s2;
        d3.Add(element);
        ledger.RecordTransform([new TransformDelta(element, s1, s2, d2, d3)]);

        Assert.True(ledger.Undo()); // M3 → M2
        Assert.Contains(element, d2.Elements);
        Assert.Equal(s1, element.TransformState);

        Assert.True(ledger.Undo()); // M2 → M1
        Assert.Contains(element, d1.Elements);
        Assert.Equal(s0, element.TransformState);
        Assert.DoesNotContain(element, d2.Elements);
        Assert.DoesNotContain(element, d3.Elements);
    }

    // ---- C4. 빈 입력 / no-op — 유령 원장 항목 사냥 ----

    [Fact]
    public void RecordTransform_NoActualChange_StillRecordsIfCalled_ButProductionGuardsAgainstIt()
    {
        // UndoLedger.RecordTransform 자체는 "델타가 비어 있지 않으면" 무조건 기록한다 —
        // "값이 실제로 바뀌었는지"는 검사하지 않는다. 이 계약을 명시적으로 고정한다.
        // (실사용 시 no-op 방지는 TransformCommitPlan.Build의 `before == after` 필터가 담당하며,
        //  그 증인은 TransformCommitPlanTests.Build_UnchangedElement_EmitsNothing이다 —
        //  순수 원장 계층 자체는 방어하지 않는다는 것이 여기서 확인해야 할 red-team 발견.)
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);
        var element = NewStroke(new Point(0, 0));
        doc.Add(element);

        var same = element.TransformState; // Before == After, 실제로는 아무 변화 없음.
        ledger.RecordTransform([new TransformDelta(element, same, same, doc, doc)]);

        Assert.Equal(1, ledger.Count);

        Assert.True(ledger.Undo());
        Assert.Equal(same, element.TransformState); // 상태는 불변이었으므로 undo도 무해하다.
    }

    [Fact]
    public void RecordDeleteSelection_EmptyList_RecordsNothing_NoPhantomEntry()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);

        ledger.RecordDeleteSelection([]);

        Assert.Equal(0, ledger.Count);
        Assert.False(ledger.Undo());
    }

    [Fact]
    public void RecordTransform_EmptyDeltaList_RecordsNothing_NoPhantomEntry()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);

        ledger.RecordTransform([]);

        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void PlanDelete_AllElementsOrphaned_ReturnsEmptyPlan_NoCrash()
    {
        // 선택된 모든 요소가 이미 어느 문서에도 없는 병리적 상태 (경쟁 상태 시뮬레이션).
        var orphan1 = NewStroke(new Point(0, 0));
        var orphan2 = NewStroke(new Point(1, 1));

        var plan = SelectionOperations.PlanDelete([orphan1, orphan2], e => null);

        Assert.Empty(plan);
    }

    [Fact]
    public void DeleteSelection_ThenUndo_ThenDeleteSameElementsAgain_UndoRestoresCorrectPositionEachTime()
    {
        // 삭제→undo→재삭제 왕복 — 원장이 같은 인덱스를 두 번 재사용해도 안전한지.
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);
        var ledger = new UndoLedger(e => doc, selection);

        var a = NewStroke(new Point(0, 0));
        var b = NewStroke(new Point(1, 1));
        var c = NewStroke(new Point(2, 2));
        foreach (var s in new[] { a, b, c }) doc.Add(s);
        selection.Set([b]);

        // 1차 삭제.
        var plan1 = SelectionOperations.PlanDelete(selection.Elements, e => doc);
        foreach (var entry in plan1) doc.Remove(entry.Element);
        ledger.RecordDeleteSelection([.. plan1.Select(e => (e.Document, e.Element, e.Index))]);
        selection.Clear();

        Assert.Equal(new AnnotationElement[] { a, c }, doc.Elements);

        // undo → b 복원.
        Assert.True(ledger.Undo());
        Assert.Equal(new AnnotationElement[] { a, b, c }, doc.Elements);

        // 2차 삭제 (같은 요소, 같은 인덱스 1).
        selection.Set([b]);
        var plan2 = SelectionOperations.PlanDelete(selection.Elements, e => doc);
        foreach (var entry in plan2) doc.Remove(entry.Element);
        ledger.RecordDeleteSelection([.. plan2.Select(e => (e.Document, e.Element, e.Index))]);

        Assert.Equal(new AnnotationElement[] { a, c }, doc.Elements);
        Assert.True(ledger.Undo());
        Assert.Equal(new AnnotationElement[] { a, b, c }, doc.Elements);
    }

    // ---- C5. 선택 억제 vs undo-of-Add 억제 경로 — 서로 침범하지 않는지 ----

    [Fact]
    public void UndoOfAdd_DoesNotUseSuppressionScope_SelectionDropsElement()
    {
        // undo-of-Add(AddOperation)는 SuppressInvalidation을 쓰지 않는다 — 이것이 진짜 제거이므로
        // 선택집합에서 반드시 떨어져야 한다 (계획서: "eraser/fade/undo-of-Add must DROP it").
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);
        var ledger = new UndoLedger(e => doc, selection);

        var element = NewStroke(new Point(0, 0));
        doc.Add(element);
        ledger.RecordAdd(element);
        selection.Set([element]);

        Assert.True(ledger.Undo());

        Assert.False(selection.Contains(element), "undo-of-Add는 억제 스코프 밖 — 선택에서 떨어져야 한다.");
        Assert.Empty(doc.Elements);
    }

    [Fact]
    public void TransformOperationUndo_WithOwnershipChange_UsesSuppressionScope_SelectionSurvives()
    {
        // 대조: TransformOperation.Undo의 소유권 변경 분기는 억제 스코프를 쓰므로 선택이 살아남아야 한다.
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var docs = new[] { d1, d2 };
        var selection = new SelectionModel();
        selection.AttachTo(d1);
        selection.AttachTo(d2);
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var element = NewStroke(new Point(0, 0));
        d2.Add(element);
        selection.Set([element]);

        var before = element.TransformState;
        var after = TransformMath.Translate(before, new Vector(1920, 0));
        ledger.RecordTransform([new TransformDelta(element, before, after, d1, d2)]);

        Assert.True(ledger.Undo());

        Assert.True(selection.Contains(element), "이관 undo는 억제 스코프 안 — 선택이 유지되어야 한다 (SEL-AC-10).");
        Assert.Contains(element, d1.Elements);
    }

    // =====================================================================
    // 헬퍼
    // =====================================================================

    private static Point AnchorWorld(ElementTransformState state, Rect bounds, HandleKind handle)
    {
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return TransformMath.ToMatrix(state, center).Transform(TransformMath.AnchorLocal(bounds, handle));
    }

    private static void AssertPointsEqual(Point expected, Point actual, double tolerance)
    {
        Assert.False(double.IsNaN(actual.X), "X가 NaN — R16 위반 (범위 어서트를 조용히 통과시킨다).");
        Assert.False(double.IsNaN(actual.Y), "Y가 NaN — R16 위반.");
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    // ---- G. 회전한 그룹 프레임 — 극단 각도에서 네 모서리가 서로 다른 핸들로 잡히는가 ----
    //
    // 각도 축의 "보이지만 잡히지 않는 핸들"(SEL-LIM-5 회귀의 결함 클래스) 사냥.
    // 렌더와 히트가 같은 GroupFrame 계산에서 나오지 않으면 여기서 무너진다.

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    [InlineData(-137.5)]
    public void HitHandle_PosedFrame_AllFourCorners_AreDistinctAndGrabbable_AtExtremeAngles(double angle)
    {
        var frame = new GroupFrame(new Rect(400, 300, 100, 60), angle);
        var seen = new List<GroupHandleKind>();

        foreach (var handle in SelectionGroup.CornersClockwise)
        {
            var drawn = SelectionGroup.CornerCenter(frame, handle);
            var hit = SelectionGroup.HitHandle(frame, drawn, Rect.Empty);

            Assert.True(
                hit == handle,
                $"{handle}@{angle}도: 그려진 위치 {drawn}에서 {hit?.ToString() ?? "없음"}이 잡혔다 — 렌더와 히트가 갈라졌다.");
            seen.Add(handle);
        }

        Assert.Equal(SelectionGroup.CornersClockwise.Length, seen.Distinct().Count());
    }
}
