using System.Windows;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// R1: 그룹 프레임·핸들·소유 필터(SEL-LIM-5)·회전한 그룹 프레임(SEL-LIM-6)의 증인 — 대상 타입은
/// <see cref="SelectionGroup"/> 하나다.
///
/// 그룹 변형 수학(ScaleAbout/RotateAbout)과 배율 재단(ClampGroupFactor/ClampMagnitude, D5)은 대상 타입이
/// <see cref="TransformMath"/>라 리팩터링 19단계에서 <see cref="TransformMathTests"/>로 옮겼다 — R1의 핵심 증인
/// <see cref="TransformMathTests.ScaleAbout_RotatedElements_KeepsLocalAxesOrthogonal"/>도 거기 있다.
/// 같은 단계에서 SelectionRedTeamTests G절(회전 프레임의 극단 각도 히트)은 여기로 왔고, 헬퍼
/// <c>Stroke</c>/<c>AssertPointsEqual</c>은 <see cref="TestGeometry"/>로 승격했다 (<c>RotateAboutPivot</c>은 이 파일만 쓴다).
///
/// <see cref="Frame_ReturnType_IsAngleFreeRect_ByReflection"/>은 AGENTS.md가 이 파일명으로 지목하는
/// 설계 방화벽이므로 자리를 지킨다.
/// </summary>
public class SelectionGroupTests
{

    // ---- 프레임 ----

    [Fact]
    public void Frame_TwoElements_UnionsAxisAlignedBounds()
    {
        var a = Stroke(0, 0, 10, 10);
        var b = Stroke(100, 50, 20, 20);

        var frame = SelectionGroup.Frame([a, b]);

        Assert.NotNull(frame);
        Assert.Equal(Rect.Union(a.TransformedBounds, b.TransformedBounds), frame!.Value);
    }

    [Fact]
    public void Frame_Empty_ReturnsNull() => Assert.Null(SelectionGroup.Frame([]));

    [Fact]
    public void Frame_FadingElement_IsExcluded()
    {
        var solid = Stroke(0, 0, 10, 10);
        var fading = Stroke(500, 500, 10, 10);
        fading.IsFading = true;

        var frame = SelectionGroup.Frame([solid, fading]);

        Assert.Equal(solid.TransformedBounds, frame);
    }

    [Fact]
    public void Frame_OnlyFadingElements_ReturnsNull()
    {
        var fading = Stroke(0, 0, 10, 10);
        fading.IsFading = true;

        Assert.Null(SelectionGroup.Frame([fading]));
    }

    // ---- 핸들 ----

    [Theory]
    [InlineData(GroupHandleKind.TopLeft, GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.TopRight, GroupHandleKind.BottomLeft)]
    [InlineData(GroupHandleKind.BottomRight, GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.BottomLeft, GroupHandleKind.TopRight)]
    public void AnchorCorner_IsDiagonallyOpposite(GroupHandleKind handle, GroupHandleKind expected)
    {
        var frame = new Rect(10, 20, 100, 60);

        Assert.Equal(SelectionGroup.CornerCenter(frame, expected), SelectionGroup.AnchorCorner(frame, handle));
    }

    [Fact]
    public void HitHandle_OnCorner_ReturnsThatCorner()
    {
        var frame = new Rect(10, 20, 100, 60);

        var hit = SelectionGroup.HitHandle(frame, frame.BottomRight, Rect.Empty);

        Assert.Equal(GroupHandleKind.BottomRight, hit);
    }

    [Fact]
    public void HitHandle_OnRotateHandle_WinsOverCorners()
    {
        var frame = new Rect(10, 20, 100, 60);

        var hit = SelectionGroup.HitHandle(frame, SelectionGroup.RotateHandle(frame), Rect.Empty);

        Assert.Equal(GroupHandleKind.Rotate, hit);
    }

    [Fact]
    public void HitHandle_FrameInterior_IsNotAHandle()
    {
        var frame = new Rect(10, 20, 100, 60);

        Assert.Null(SelectionGroup.HitHandle(frame, SelectionGroup.Center(frame), Rect.Empty));
    }

    [Fact]
    public void ScaleFactor_CursorAtGripCorner_IsOne()
    {
        var frame = new Rect(0, 0, 100, 50);

        double factor = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, frame.BottomRight);

        Assert.Equal(1, factor, 9);
    }

    [Fact]
    public void ScaleFactor_CursorAtDoubleDiagonal_IsTwo()
    {
        var frame = new Rect(0, 0, 100, 50);
        // 앵커(좌상 0,0)에서 잡은 모서리(100,50)까지의 대각을 2배 지점까지 끈다.
        double factor = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, new Point(200, 100));

        Assert.Equal(2, factor, 9);
    }

    [Fact]
    public void ScaleFactor_CursorOffAxis_UsesProjectionSoAspectNeverChanges()
    {
        var frame = new Rect(0, 0, 100, 50);
        // 대각과 수직인 방향으로 밀어도 정사영 성분이 그대로면 배율이 변하지 않는다.
        var diagonal = frame.BottomRight - frame.TopLeft;
        var perpendicular = new Vector(-diagonal.Y, diagonal.X);

        double onAxis = SelectionGroup.ScaleFactor(frame, GroupHandleKind.BottomRight, frame.BottomRight);
        double offAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, frame.BottomRight + perpendicular);

        Assert.Equal(onAxis, offAxis, 9);
    }

    // ---- SEL-LIM-5: '그리는 조건'과 '잡히는 조건'은 같은 술어여야 한다 ----

    /// <summary>
    /// 회귀 증인: 모니터에 걸친 선택에서 이 서피스가 요소를 <b>1개만</b> 소유하면 핸들을 그려서도,
    /// 잡아서도 안 된다. 예전에는 렌더가 <c>owned.Count &lt; MinGroupCount</c>라며 요소별 8핸들
    /// 경로로 빠져 핸들을 전부 그렸는데 히트 테스트는 막혀 있어, 회전 핸들 클릭이 빈 곳 분기로
    /// 떨어져 선택 전체가 날아가고 클릭 통과까지 켜졌다.
    /// </summary>
    [Fact]
    public void HandlesGrabbable_SingleOwnedOfCrossMonitorPair_IsFalse() =>
        Assert.False(SelectionGroup.HandlesGrabbable(ownedCount: 1, selectionCount: 2));

    [Fact]
    public void HandlesGrabbable_OwnsWholeSelection_IsTrue()
    {
        Assert.True(SelectionGroup.HandlesGrabbable(1, 1));
        Assert.True(SelectionGroup.HandlesGrabbable(3, 3));
    }

    [Fact]
    public void HandlesGrabbable_NothingOwned_IsFalse()
    {
        Assert.False(SelectionGroup.HandlesGrabbable(0, 0));
        Assert.False(SelectionGroup.HandlesGrabbable(0, 2));
    }

    // ---- 소유 필터 (SEL-LIM-5) ----
    //
    // 이 절은 <see cref="SelectionGroup.HandlesGrabbable"/>의 **입력**을 지킨다. 술어를 하나로 합쳐도
    // 소유 목록을 서피스마다 따로 만들면 SEL-LIM-5 회귀가 입력 쪽으로 자리만 옮긴다.
    // 테스트는 일부러 selection.AttachTo(document)를 부르지 않는다 — ElementRemoved 구독이 붙으면
    // "어느 문서에도 없는 요소"(이관 SuppressInvalidation 창)를 재현할 수 없다.

    /// <summary>
    /// 모니터에 걸친 선택: 서피스마다 자기 몫만 돌려주고, 두 몫의 합집합이 선택집합과 같아야 한다.
    /// </summary>
    [Fact]
    public void OwnedBy_CrossMonitorSelection_ReturnsOnlyThisDocumentsElements()
    {
        var docA = new AnnotationDocument("A");
        var docB = new AnnotationDocument("B");
        var a1 = Stroke(0, 0, 10, 10);
        var a2 = Stroke(20, 0, 10, 10);
        var b1 = Stroke(40, 0, 10, 10);
        docA.Add(a1);
        docA.Add(a2);
        docB.Add(b1);
        var selection = new SelectionModel();
        selection.Set([a1, a2, b1]);

        var ownedA = SelectionGroup.OwnedBy(docA, selection);
        var ownedB = SelectionGroup.OwnedBy(docB, selection);

        Assert.Equal([a1, a2], ownedA);
        Assert.Equal([b1], ownedB);
        Assert.Equal(selection.Count, ownedA.Count + ownedB.Count);
        Assert.Equal(selection.Elements, [.. ownedA, .. ownedB]);
    }

    /// <summary>
    /// 순회 방향 계약: 선택집합이 바깥 루프다. 문서를 바깥으로 뒤집는 "정리"를 잡는 유일한 증인이며,
    /// 단일 소유 경로의 <c>owned[0]</c>(어느 요소의 8핸들을 그리는가)을 지킨다.
    /// </summary>
    [Fact]
    public void OwnedBy_PreservesSelectionOrder()
    {
        var document = new AnnotationDocument("A");
        var e1 = Stroke(0, 0, 10, 10);
        var e2 = Stroke(20, 0, 10, 10);
        var e3 = Stroke(40, 0, 10, 10);
        document.Add(e1);
        document.Add(e2);
        document.Add(e3);
        var selection = new SelectionModel();
        selection.Set([e3, e1, e2]); // 문서 순서와 일부러 다르게

        var owned = SelectionGroup.OwnedBy(document, selection);

        Assert.Equal(3, owned.Count);
        Assert.Same(e3, owned[0]);
        Assert.Same(e1, owned[1]);
        Assert.Same(e2, owned[2]);
    }

    [Fact]
    public void OwnedBy_ForeignElement_IsExcluded()
    {
        var docA = new AnnotationDocument("A");
        var docB = new AnnotationDocument("B");
        var mine = Stroke(0, 0, 10, 10);
        var theirs = Stroke(40, 0, 10, 10);
        docA.Add(mine);
        docB.Add(theirs);
        var selection = new SelectionModel();
        selection.Set([mine, theirs]);

        var owned = SelectionGroup.OwnedBy(docA, selection);

        Assert.DoesNotContain(theirs, owned);
        Assert.Contains(mine, owned);
    }

    /// <summary>
    /// 이관 창(LD-5 / SEL-AC-5) 재현: <see cref="SelectionTransfer"/>의 SuppressInvalidation 구간에서는
    /// 요소가 <b>어느 문서에도 없는</b> 프레임이 존재한다. 소유 판정을 ownerLookup 계열 정의
    /// ("전 서피스 중 누가 소유하는가")로 바꾸면 그 한 프레임 동안 두 정의가 어긋나므로,
    /// 여기서 참조 동일성 정의를 못박아 둔다.
    /// </summary>
    [Fact]
    public void OwnedBy_ElementInNoDocument_IsExcluded()
    {
        var document = new AnnotationDocument("A");
        var staying = Stroke(0, 0, 10, 10);
        var inFlight = Stroke(20, 0, 10, 10);
        document.Add(staying);
        document.Add(inFlight);
        var selection = new SelectionModel();
        selection.Set([staying, inFlight]);
        document.Remove(inFlight); // 이관 중: 선택에는 남아 있지만 어느 문서에도 없다

        var owned = SelectionGroup.OwnedBy(document, selection);

        Assert.Equal([staying], owned);
        Assert.Contains(inFlight, selection.Elements);
    }

    /// <summary>
    /// 계약 고정: 빈 선택에서도 null이 아닌 빈 목록. 호출부가 <c>owned.Count</c> 단락 평가에 기대
    /// <c>owned[0]</c>을 인덱싱한다 (SurfaceInputController.IsInsideSelectionFrame).
    /// </summary>
    [Fact]
    public void OwnedBy_EmptySelection_ReturnsEmptyList()
    {
        var document = new AnnotationDocument("A");
        document.Add(Stroke(0, 0, 10, 10));

        var owned = SelectionGroup.OwnedBy(document, new SelectionModel());

        Assert.NotNull(owned);
        Assert.Empty(owned);
    }

    // ---- 회전한 그룹 프레임 (제스처 한정 렌더/히트 좌표, SEL-LIM-6) ----
    //
    // 증상: 다중 선택을 회전하면 점선 테두리와 5개 핸들이 시작 위치에 못 박혀 있었다.
    // 원인은 그려지는 프레임이 각도를 담을 수 없는 축 정렬 Rect였다는 것 —
    // 아래 증인들은 GroupFrame이 (a) 각도 0에서 예전과 정확히 같고,
    // (b) 각도가 붙으면 잉크와 같은 강체 운동을 겪으며,
    // (c) 그려지는 좌표와 잡히는 좌표가 끝까지 같은 계산에서 나온다는 것을 고정한다.

    /// <summary>프레임 로컬 점을 피벗 기준으로 회전시킨 월드 위치 (테스트가 독립적으로 계산하는 기준값).</summary>
    private static Point RotateAboutPivot(Point p, Point pivot, double degrees) =>
        pivot + TransformMath.RotateVector(p - pivot, degrees);

    // -- 하위 호환: 각도 0이면 수정 이전과 같은 좌표여야 한다 --

    /// <summary>
    /// 각도 0 단락이 실제로 동작해 피벗 왕복 (x−p)+p의 1ulp 표류가 없음을 못박는다.
    /// 허용오차 없이 정확히 같아야 한다 — 회전하지 않는 모든 경우가 수정 이전과 비트 동일이라는 뜻이다.
    /// </summary>
    [Fact]
    public void Corners_ZeroAngle_MatchesAxisAlignedRectCornersExactly()
    {
        var bounds = new Rect(10, 20, 100, 60);

        var corners = SelectionGroup.Corners(new GroupFrame(bounds, 0));

        Assert.Equal(bounds.TopLeft, corners[0]);
        Assert.Equal(bounds.TopRight, corners[1]);
        Assert.Equal(bounds.BottomRight, corners[2]);
        Assert.Equal(bounds.BottomLeft, corners[3]);
    }

    [Theory]
    [InlineData(GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.TopRight)]
    [InlineData(GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.BottomLeft)]
    public void CornerCenter_ZeroAngle_MatchesRectOverload(GroupHandleKind handle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        Assert.Equal(
            SelectionGroup.CornerCenter(bounds, handle),
            SelectionGroup.CornerCenter(new GroupFrame(bounds, 0), handle));
    }

    [Fact]
    public void TopCenter_ZeroAngle_MatchesRectOverload()
    {
        var bounds = new Rect(10, 20, 100, 60);

        Assert.Equal(SelectionGroup.TopCenter(bounds), SelectionGroup.TopCenter(new GroupFrame(bounds, 0)));
    }

    /// <summary>R5 그룹판: 각도 0에서 렌더 오버로드와 히트 오버로드가 같은 클램프 지점을 낸다.</summary>
    [Fact]
    public void RotateHandle_ZeroAngle_MatchesRectOverload_AndClampsIdentically()
    {
        var bounds = new Rect(800, 2, 100, 60); // 화면 최상단 — 회전 핸들이 서피스 밖으로 나간다.
        var surface = new Rect(0, 0, 1920, 1080);
        double reach = TransformMath.HandleScreenSize / 2;

        var fromRect = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(bounds), surface, reach);
        var fromFrame = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(new GroupFrame(bounds, 0)), surface, reach);

        AssertPointsEqual(fromRect, fromFrame);
        Assert.Equal(GroupHandleKind.Rotate, SelectionGroup.HitHandle(bounds, fromFrame, surface));
    }

    // -- 회전 기하 --

    /// <summary>원점이 아니라 <b>프레임 중심</b>을 축으로 돈다. 최빈 오구현(원점 회전)이면 즉시 실패한다.</summary>
    [Fact]
    public void Corners_RotatedFrame_RotateAboutFrameCenter_NotOrigin()
    {
        var bounds = new Rect(300, 200, 100, 60); // 원점에서 멀리 — 원점 회전이면 화면 밖으로 날아간다.
        var frame = new GroupFrame(bounds, 30);
        var pivot = SelectionGroup.Center(bounds);

        var corners = SelectionGroup.Corners(frame);

        AssertPointsEqual(RotateAboutPivot(bounds.TopLeft, pivot, 30), corners[0], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.TopRight, pivot, 30), corners[1], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.BottomRight, pivot, 30), corners[2], 1e-9);
        AssertPointsEqual(RotateAboutPivot(bounds.BottomLeft, pivot, 30), corners[3], 1e-9);
    }

    /// <summary>회전은 강체 운동이다 — 어느 각도에서도 직각과 변 길이가 보존되어야 한다(전단 금지, A3).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(47.5)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-120)]
    public void Corners_RotatedFrame_StaysRigid_AtManyAngles(double angle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        var c = SelectionGroup.Corners(new GroupFrame(bounds, angle));
        var top = c[1] - c[0];
        var right = c[2] - c[1];
        var bottom = c[2] - c[3];
        var left = c[3] - c[0];

        Assert.Equal(0, (top.X * right.X) + (top.Y * right.Y), 9);
        Assert.Equal(bounds.Width, top.Length, 9);
        Assert.Equal(bounds.Width, bottom.Length, 9);
        Assert.Equal(bounds.Height, right.Length, 9);
        Assert.Equal(bounds.Height, left.Length, 9);
    }

    /// <summary>
    /// <b>수정 완료의 정의</b>: 가이드 프레임이 잉크와 <b>같은 (pivot, delta)</b>로 도는가.
    /// 두 가지를 동시에 본다 — (a) 포즈된 4점이 시작 프레임의 강체 이동상이고,
    /// (b) 회전된 잉크의 꼭짓점을 −delta로 되돌리면 전부 시작 프레임 안에 들어온다
    /// (= 기울어진 가이드가 여전히 잉크를 정확히 감싼다).
    ///
    /// <b>하지 말 것</b>: 회전된 잉크 꼭짓점이 포즈된 사각형 "안"인지 볼록 판정으로 단언하기.
    /// 그 점들은 정의상 경계 <b>위</b>에 있고 볼록 판정에 엡실론이 없어 1e-14 잡음만으로 outside가 난다.
    /// </summary>
    [Fact]
    public void Corners_FrameRotatedByDelta_MatchesMembersRotatedByTheSameDelta()
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);
        const double delta = 40;

        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, delta);
        }

        var posed = SelectionGroup.Corners(new GroupFrame(frame0, delta));
        AssertPointsEqual(RotateAboutPivot(frame0.TopLeft, pivot, delta), posed[0], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.TopRight, pivot, delta), posed[1], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.BottomRight, pivot, delta), posed[2], 1e-9);
        AssertPointsEqual(RotateAboutPivot(frame0.BottomLeft, pivot, delta), posed[3], 1e-9);

        var slack = frame0;
        slack.Inflate(1e-6, 1e-6);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            foreach (var corner in element.TransformedCorners())
            {
                var pulledBack = RotateAboutPivot(corner, pivot, -delta);
                Assert.True(
                    slack.Contains(pulledBack),
                    $"−{delta}도로 되돌린 잉크 꼭짓점 {pulledBack}이 시작 프레임 {frame0} 밖이다 — 가이드가 잉크를 감싸지 못한다.");
            }
        }
    }

    /// <summary>
    /// 90도 배수에서는 포즈된 프레임과 마우스 업 이후의 살아있는 축 정렬 합집합이 같다 —
    /// SEL-LIM-6의 "90도 배수에서는 릴리스 스냅이 시각적으로 0"이라는 서술의 증인.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Corners_RotatedBy90Multiples_EqualPostReleaseAxisAlignedUnion(double angle)
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);

        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, angle);
        }

        var live = SelectionGroup.Frame([a, b])!.Value;
        var liveCorners = SelectionGroup.Corners(new GroupFrame(live, 0));
        var posed = SelectionGroup.Corners(new GroupFrame(frame0, angle));

        foreach (var corner in posed)
        {
            Assert.True(
                Array.Exists(liveCorners, other => (other - corner).Length <= 1e-6),
                $"{angle}도에서 포즈된 꼭짓점 {corner}이 릴리스 이후 합집합 {live}의 꼭짓점 집합에 없다.");
        }
    }

    /// <summary>
    /// 프레임이 180도 돌면 회전 핸들은 프레임 <b>아래</b>에 있어야 한다 (R5).
    /// 하드코딩 <c>frame.Top − offset</c>을 죽이는 증인 — 요소별
    /// <c>RotateHandleWorld_UpsideDownElement_SitsBelowCenterNotAboveIt</c>의 그룹판이다.
    /// </summary>
    [Fact]
    public void RotateHandle_FrameAt180Degrees_SitsBelowFrameCenter_NotAbove()
    {
        var bounds = new Rect(10, 20, 100, 60);
        var frame = new GroupFrame(bounds, 180);

        var handle = SelectionGroup.RotateHandle(frame);

        Assert.True(
            handle.Y > SelectionGroup.Center(frame).Y,
            $"180도에서 회전 핸들 {handle}이 중심 {SelectionGroup.Center(frame)} 위에 있다 — 화면 −Y 하드코딩이 남아 있다.");
    }

    /// <summary>회전 핸들은 회전한 상단 변에 <b>수직</b>으로 화면 거리만큼 떨어져 있어야 한다.</summary>
    [Fact]
    public void RotateHandle_RotatedFrame_KeepsScreenOffsetPerpendicularToTopEdge()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);
        var corners = SelectionGroup.Corners(frame);

        var stem = SelectionGroup.RotateHandle(frame) - SelectionGroup.TopCenter(frame);
        var topEdge = corners[1] - corners[0];

        Assert.Equal(TransformMath.RotateHandleScreenOffset, stem.Length, 6);
        Assert.Equal(0, (stem.X * topEdge.X) + (stem.Y * topEdge.Y), 9);
    }

    /// <summary>스템 시작점은 회전한 상단 변의 중점이어야 테두리·스템·핸들이 한 도형으로 보인다.</summary>
    [Fact]
    public void TopCenter_RotatedFrame_IsMidpointOfRotatedTopEdge()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);
        var corners = SelectionGroup.Corners(frame);

        var midpoint = new Point((corners[0].X + corners[1].X) / 2, (corners[0].Y + corners[1].Y) / 2);

        AssertPointsEqual(midpoint, SelectionGroup.TopCenter(frame), 1e-9);
    }

    // -- 렌더 == 히트 (R5): 그려지는 위치에서 잡혀야 한다 --

    /// <summary>
    /// 각도 축의 "보이지만 잡히지 않는 핸들" 방어선. 예전에 그 결함은 선택과 도구를 통째로 날렸다
    /// (SEL-LIM-5 회귀 서술 참고). 회전 <b>전</b> 축 정렬 위치에서는 잡히지 않아야 한다.
    /// </summary>
    [Theory]
    [InlineData(GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.TopRight)]
    [InlineData(GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.BottomLeft)]
    public void HitHandle_RotatedFrame_GrabsCornerAtDrawnPosition(GroupHandleKind handle)
    {
        var bounds = new Rect(10, 20, 100, 60);
        var frame = new GroupFrame(bounds, 35);

        Assert.Equal(
            handle,
            SelectionGroup.HitHandle(frame, SelectionGroup.CornerCenter(frame, handle), Rect.Empty));
        Assert.NotEqual(
            handle,
            SelectionGroup.HitHandle(frame, SelectionGroup.CornerCenter(bounds, handle), Rect.Empty));
    }

    [Fact]
    public void HitHandle_RotatedFrame_RotateHandleStillWinsOverCorners()
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);

        Assert.Equal(
            GroupHandleKind.Rotate,
            SelectionGroup.HitHandle(frame, SelectionGroup.RotateHandle(frame), Rect.Empty));
    }

    /// <summary>클램프된 회전 핸들도 렌더와 히트가 같은 지점이어야 한다 (R5).</summary>
    [Fact]
    public void HitHandle_RotatedFrame_ClampedRotateHandle_IsGrabbableAtClampedSpot()
    {
        var frame = new GroupFrame(new Rect(800, 2, 100, 60), 35);
        var surface = new Rect(0, 0, 1920, 1080);

        var drawn = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(frame), surface, TransformMath.HandleScreenSize / 2);

        Assert.Equal(GroupHandleKind.Rotate, SelectionGroup.HitHandle(frame, drawn, surface));
    }

    // -- 레드팀 G절 (리팩터링 19단계, SelectionRedTeamTests에서 이동): 극단 각도에서 네 모서리가 서로 다른 핸들로 잡히는가 --
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

    // -- 수학 불변 --

    /// <summary><see cref="GroupFrame"/>이 피벗을 별도 필드로 안 실어도 되는 근거.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(-120)]
    public void Center_RotatedFrame_IsInvariantUnderFrameAngle(double angle)
    {
        var bounds = new Rect(10, 20, 100, 60);

        AssertPointsEqual(SelectionGroup.Center(bounds), SelectionGroup.Center(new GroupFrame(bounds, angle)));
    }

    [Theory]
    [InlineData(GroupHandleKind.TopLeft, GroupHandleKind.BottomRight)]
    [InlineData(GroupHandleKind.TopRight, GroupHandleKind.BottomLeft)]
    [InlineData(GroupHandleKind.BottomRight, GroupHandleKind.TopLeft)]
    [InlineData(GroupHandleKind.BottomLeft, GroupHandleKind.TopRight)]
    public void AnchorCorner_RotatedFrame_IsStillDiagonallyOpposite(
        GroupHandleKind handle, GroupHandleKind expected)
    {
        var frame = new GroupFrame(new Rect(10, 20, 100, 60), 35);

        var anchor = SelectionGroup.AnchorCorner(frame, handle);
        var grip = SelectionGroup.CornerCenter(frame, handle);

        AssertPointsEqual(SelectionGroup.CornerCenter(frame, expected), anchor, 1e-9);
        AssertPointsEqual(
            frame.Pivot, new Point((anchor.X + grip.X) / 2, (anchor.Y + grip.Y) / 2), 1e-9);
    }

    /// <summary>등방 배율의 정사영 축이 프레임과 함께 돈다 — 본문을 고치지 않아도 되는 이유의 증인.</summary>
    [Fact]
    public void ScaleFactor_RotatedFrame_ProjectsOntoRotatedDiagonal()
    {
        var frame = new GroupFrame(new Rect(0, 0, 100, 50), 35);
        var anchor = SelectionGroup.AnchorCorner(frame, GroupHandleKind.BottomRight);
        var grip = SelectionGroup.CornerCenter(frame, GroupHandleKind.BottomRight);
        var diagonal = grip - anchor;
        var perpendicular = new Vector(-diagonal.Y, diagonal.X);

        double onAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, anchor + (diagonal * 2));
        double offAxis = SelectionGroup.ScaleFactor(
            frame, GroupHandleKind.BottomRight, anchor + (diagonal * 2) + perpendicular);

        Assert.Equal(2, onAxis, 9);
        Assert.Equal(onAxis, offAxis, 9);
    }

    // -- 제스처 계약 (컨트롤러에서 순수 코어로 내린 부분) --

    /// <summary>
    /// 밀어 넣는 것은 <b>동결된</b> 크기다. 여기에 살아있는 합집합을 넣도록 바꾸면
    /// "잡은 핸들이 커서 밑에서 빠져나간다"가 전 테스트 초록인 채 부활한다.
    /// </summary>
    [Fact]
    public void GestureFrame_Rotating_FreezesSizeAndCarriesDelta()
    {
        var frozen = new Rect(10, 20, 100, 60);

        var frame = SelectionGroup.GestureFrame(frozen, rotating: true, deltaDegrees: 40);

        Assert.NotNull(frame);
        Assert.Equal(frozen, frame!.Value.Bounds);
        Assert.Equal(40, frame.Value.AngleDegrees, 9);
    }

    /// <summary>등방 스케일·이동은 프레임을 밀지 않는다 — 살아있는 합집합이 그대로 정답이다.</summary>
    [Fact]
    public void GestureFrame_NotRotating_ReturnsNull() =>
        Assert.Null(SelectionGroup.GestureFrame(new Rect(10, 20, 100, 60), rotating: false, deltaDegrees: 40));

    /// <summary>
    /// <see cref="Rect.Empty"/>는 좌표가 ±무한대라 피벗이 NaN이 된다. 도달 불가능하더라도
    /// 타입 경계에서 막는다 — NaN은 범위 어서트를 조용히 통과한다 (R16).
    /// </summary>
    [Fact]
    public void GestureFrame_EmptyFrozenRect_ReturnsNull_NoNaN()
    {
        Assert.Null(SelectionGroup.GestureFrame(Rect.Empty, rotating: true, deltaDegrees: 40));
        Assert.True(
            double.IsNaN(new GroupFrame(Rect.Empty, 40).Pivot.X),
            "가드가 필요한 이유 자체가 사라졌다면 가드도 재검토할 것.");
    }

    /// <summary>
    /// Shift는 <b>증분</b>을 15도 배수로 스냅하고, 가이드와 잉크가 <b>그 값 하나</b>를 공유한다 —
    /// 두 번 계산하거나 프레임에 스냅 전 각을 쓰면 15도 경계마다 가이드가 잉크에서 떨어진다.
    /// </summary>
    [Fact]
    public void RotationDelta_WithShift_SnapsIncrement_AndGuideSharesTheSameNumber()
    {
        var a = Stroke(0, 0, 120, 60);
        var b = Stroke(200, 100, 60, 40);
        var frame0 = SelectionGroup.Frame([a, b])!.Value;
        var pivot = SelectionGroup.Center(frame0);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), 38); // 38도 → 45도로 스냅

        double delta = SelectionGroup.RotationDelta(pivot, from, to, shift: true);

        Assert.Equal(45, delta, 9);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            element.TransformState =
                TransformMath.RotateAbout(element.TransformState, element.LocalBounds, pivot, delta);
        }

        var posed = SelectionGroup.Corners(new GroupFrame(frame0, delta));
        AssertPointsEqual(RotateAboutPivot(frame0.TopLeft, pivot, delta), posed[0], 1e-9);

        var slack = frame0;
        slack.Inflate(1e-6, 1e-6);
        foreach (var element in new AnnotationElement[] { a, b })
        {
            foreach (var corner in element.TransformedCorners())
            {
                Assert.True(
                    slack.Contains(RotateAboutPivot(corner, pivot, -delta)),
                    $"스냅된 {delta}도에서 가이드와 잉크가 어긋났다.");
            }
        }
    }

    /// <summary>커서가 피벗과 겹치면 0이고 NaN이 아니다 (승격 과정에서 원본 퇴화 방어가 유실되지 않았음).</summary>
    [Fact]
    public void RotationDelta_DegenerateDrag_ReturnsZero()
    {
        var pivot = new Point(50, 50);

        double delta = SelectionGroup.RotationDelta(pivot, pivot, new Point(80, 20), shift: false);

        Assert.False(double.IsNaN(delta), "NaN이면 각도가 요소로 새어 나가 화면에서 증발한다 (R16).");
        Assert.Equal(0, delta, 9);
    }

    /// <summary>
    /// 시작 각이 제각각인 구성원도 <b>같은 증분</b>만큼 돈다 (R1). Shift 스냅이 결과 각이 아니라 증분에 걸리므로,
    /// 10도·70도·23도로 미리 돌려 둔 요소가 45도 증분을 나란히 받아 55도·115도·68도가 된다.
    ///
    /// 여기를 <see cref="TransformMath.Rotate"/>의 결과 각 스냅으로 갈아끼우면 구성원마다 스냅 잔차가 달라
    /// 증분이 35도/35도/37도로 갈라지고 그룹이 한 덩어리로 돌지 않는다 — 이 단언이 그 대체를 막는다.
    /// 시작 각도 결과 각도 15도 배수가 아니도록 골랐다.
    /// </summary>
    [Fact]
    public void RotationDelta_MixedAngleMembers_AdvanceByTheSameIncrement()
    {
        AnnotationElement[] members = [Stroke(0, 0, 120, 60), Stroke(200, 100, 60, 40), Stroke(-40, 160, 80, 80)];
        double[] startAngles = [10, 70, 23];
        for (int i = 0; i < members.Length; i++)
        {
            members[i].TransformState = members[i].TransformState with { AngleDegrees = startAngles[i] };
        }

        var frame0 = SelectionGroup.Frame(members)!.Value;
        var pivot = SelectionGroup.Center(frame0);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), 38); // 38도 → 45도로 스냅

        double delta = SelectionGroup.RotationDelta(pivot, from, to, shift: true);

        Assert.Equal(45, delta, 9);
        for (int i = 0; i < members.Length; i++)
        {
            members[i].TransformState = TransformMath.RotateAbout(
                members[i].TransformState, members[i].LocalBounds, pivot, delta);
            Assert.Equal(
                startAngles[i] + 45,
                members[i].TransformState.AngleDegrees,
                9);
        }
    }

    // -- 설계 방화벽 --

    /// <summary>
    /// 그룹 프레임의 <b>지속</b> 상태는 각도가 없는 <see cref="Rect"/>여야 한다.
    /// 각도를 여기로 올리려는 시도는 이 증인에서 걸린다.
    /// </summary>
    [Fact]
    public void Frame_ReturnType_IsAngleFreeRect_ByReflection()
    {
        var method = typeof(SelectionGroup).GetMethod(nameof(SelectionGroup.Frame));

        Assert.NotNull(method);
        Assert.True(
            method!.ReturnType == typeof(Rect?),
            "그룹 프레임에 각도를 영속시키려면 원장(TransformDelta)에 각도 자리를 먼저 만들어야 한다 (SEL-LIM-6). "
            + "실행취소가 되돌릴 수 없는 상태를 만들지 말 것.");
    }

    // -- GroupRotateStep: 가이드와 잉크가 어긋날 수 없음을 타입으로 고정 --

    /// <summary>
    /// <b>수정의 배선 증인</b>. 컨트롤러는 이 한 호출의 세 필드를 그대로 쓰므로,
    /// 여기서 각도가 일치하면 화면의 가이드와 잉크가 어긋날 방법이 없다.
    /// 이 단언이 깨지면 "그룹을 회전해도 테두리가 안 도는" 원래 증상이 그대로 돌아온다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(38)]
    [InlineData(90)]
    [InlineData(-137.5)]
    public void RotateStep_GuideAngleAndInkDelta_AreTheSameNumber(double angle)
    {
        var frozen = new Rect(300, 200, 260, 140);
        var pivot = SelectionGroup.Center(frozen);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), angle);

        var step = SelectionGroup.RotateStep(frozen, from, to, shift: false);

        Assert.NotNull(step.Guide);
        Assert.Equal(
            step.DeltaDegrees,
            step.Guide!.Value.AngleDegrees);
        Assert.Equal(angle, step.DeltaDegrees, 9);
    }

    /// <summary>가이드와 잉크는 <b>같은 피벗</b>을 써야 한다 — 동결 프레임의 중심 하나.</summary>
    [Fact]
    public void RotateStep_PivotIsFrozenFrameCenter_AndGuideSharesIt()
    {
        var frozen = new Rect(300, 200, 260, 140);

        var step = SelectionGroup.RotateStep(
            frozen, new Point(600, 270), new Point(430, 500), shift: false);

        AssertPointsEqual(SelectionGroup.Center(frozen), step.Pivot);
        AssertPointsEqual(step.Pivot, step.Guide!.Value.Pivot);
    }

    /// <summary>
    /// 회전 중에는 <b>항상</b> 가이드를 민다. null이 섞이면 창이 살아있는 축 정렬 합집합으로 되돌아가
    /// 잡은 핸들이 커서 밑에서 빠져나간다.
    /// </summary>
    [Fact]
    public void RotateStep_NonEmptyFrozen_AlwaysProducesGuideFrozenAtStartSize()
    {
        var frozen = new Rect(300, 200, 260, 140);

        var step = SelectionGroup.RotateStep(
            frozen, new Point(600, 270), new Point(430, 500), shift: false);

        Assert.NotNull(step.Guide);
        Assert.Equal(frozen, step.Guide!.Value.Bounds);
    }

    /// <summary>Shift 스냅은 가이드와 잉크에 <b>동시에</b> 걸려야 한다 (한쪽만 스냅되면 15도 경계마다 어긋난다).</summary>
    [Fact]
    public void RotateStep_WithShift_SnapsGuideAndInkTogether()
    {
        var frozen = new Rect(300, 200, 260, 140);
        var pivot = SelectionGroup.Center(frozen);
        var from = pivot + new Vector(100, 0);
        var to = pivot + TransformMath.RotateVector(new Vector(100, 0), 38);

        var step = SelectionGroup.RotateStep(frozen, from, to, shift: true);

        Assert.Equal(45, step.DeltaDegrees, 9);
        Assert.Equal(step.DeltaDegrees, step.Guide!.Value.AngleDegrees);
    }

    /// <summary>퇴화 입력에서 NaN이 잉크로 새어 나가지 않는다 (R16).</summary>
    [Fact]
    public void RotateStep_EmptyFrozen_IsInertAndNaNFree()
    {
        var step = SelectionGroup.RotateStep(Rect.Empty, new Point(10, 10), new Point(90, 40), shift: false);

        Assert.Null(step.Guide);
        Assert.Equal(0, step.DeltaDegrees);
        Assert.False(double.IsNaN(step.Pivot.X), "NaN 피벗은 요소를 화면에서 증발시킨다 (R16).");
        Assert.False(double.IsNaN(step.Pivot.Y), "NaN 피벗은 요소를 화면에서 증발시킨다 (R16).");
    }
}
