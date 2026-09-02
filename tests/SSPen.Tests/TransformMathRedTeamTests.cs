using System.Windows;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// <see cref="TransformMath"/> 적대적/경계 케이스 — 리팩터링 19단계에서 SelectionRedTeamTests A절(A1~A5)을
/// 글자 그대로 옮겼다 (대상 타입 1:1: 기본 계약은 <see cref="TransformMathTests"/>, 레드팀은 여기).
///
/// 목적은 확인이 아니라 파괴다: 기존 스위트가 이미 통과시킨 계약을 다른 각도(퇴화 기하, 비-0도 회전,
/// R14 부호 보존, R21 앵커의 누적 드리프트, 360도 wrap, 180도 회전 핸들, 오프스크린 클램프)에서 다시 찌른다.
/// 헬퍼 <c>AnchorWorld</c>/<c>AssertPointsEqual</c>은 <see cref="TestGeometry"/>로 승격했고,
/// <c>Tol</c>은 이 파일만 쓰므로 여기 남겼다.
///
/// 참조 계약: <c>.gjc/_session-.../specs/deep-interview-selection-tool.md</c> (SEL-AC-1..18),
/// <c>.../plans/ralplan/.../stage-05-revision.md</c> (R1..R24, ARCH-17/18/19/20/21).
/// </summary>
public class TransformMathRedTeamTests
{
    private const double Tol = 1e-9;

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
}
