using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 캡처 결과물에 선택 장식이 들어가지 않는지 실기 픽셀 검증 (SEL-17, SEL-AC-15).
///
/// as-seen 인텐트: 잉크는 **찍히고** 장식은 **안 찍힌다**. 서피스 창 자체를 숨기면 잉크까지
/// 사라지므로 장식 레이어만 숨기는 것이 계약이다 — 그래서 양방향으로 어서트한다.
/// </summary>
public class SelectionCaptureTests
{
    /// <summary>
    /// 장식 강조색. 값을 여기 다시 적지 않고 소유자(<see cref="SSPen.Shell.ShellPalette.Accent"/>)에서 읽는다 —
    /// 예전에는 #FF00ADEF를 하드코딩했다가 강조색이 대비 기준을 맞추려 #FF0071A8로 바뀌자
    /// 이 스위트가 "장식이 화면에 없다"며 죽었다(허용 오차 40 밖). 그때 실제로 바뀐 것은 색 하나뿐이고
    /// 검증하려던 계약(장식은 캡처에 안 찍힌다)은 그대로였다.
    /// 장식 색과 <c>ShellPalette.Accent</c>가 같다는 사실 자체는 유닛 스위트의
    /// <c>SelectionDecorationVisualTests</c>가 잠근다.
    /// 잉크는 이 색과 확실히 구분되도록 순수 빨강을 쓴다.
    /// </summary>
    private static readonly Color DecorationColor = SSPen.Shell.ShellPalette.Accent;
    private static readonly Color InkColor = Color.FromRgb(0xFF, 0x00, 0x00);

    private sealed record Rig(
        ContentSurfaceWindow Surface,
        AnnotationDocument Document,
        SelectionModel Selection,
        PhysicalRect Bounds);

    private static Rig CreateRig()
    {
        // 주 모니터를 쓴다: 캡처 좌표계가 가장 단순하고 음수 원점 변수를 배제한다.
        var monitor = MonitorTopology.Enumerate().First(m => m.IsPrimary);
        var state = new AppState { ActiveTool = ToolKind.Select };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(
            e => document.Elements.Contains(e) ? document : null, selection);
        var surface = SurfaceRigs.NewSurface(monitor, state, document, ledger, selection);
        return new Rig(surface, document, selection, monitor.Bounds);
    }

    /// <summary>굵고 긴 수평 획: 캡처 영역 안에서 확실히 픽셀을 남긴다.</summary>
    private static StrokeElement NewThickStroke() =>
        new([new Point(400, 400), new Point(700, 400)], InkColor, 24, isHighlighter: false);

    /// <summary>
    /// 장식 판정에 쓰는 여유. 장식이 화면 위 <b>다른 무엇</b>과 우연히 같은 색일 수 있으므로
    /// 존재 여부가 아니라 <see cref="PixelProbe.CountColor"/>의 개수 차이로 본다 (그 주석 참조).
    /// </summary>
    private const int DecorationTolerance = 40;

    /// <summary>
    /// 장식이 그려졌다고 인정하는 최소 픽셀 증가분. 장식은 핸들 9개(각 10x10 테두리 2px) +
    /// 점선 테두리라 수백 픽셀 단위로 늘어난다 — 50은 안티에일리어싱 흔들림보다 훨씬 크고
    /// 실제 장식보다 훨씬 작다.
    /// </summary>
    private const int DecorationPixelMargin = 50;

    /// <summary>장식 색 픽셀 수가 조건을 만족할 때까지 캡처를 재시도하고, 마지막 개수를 돌려준다.</summary>
    private static int CountUntil(PhysicalRect region, Func<int, bool> condition, int timeoutMs = 3000)
    {
        int last = -1;
        var shot = PixelProbe.CaptureUntil(
            region,
            image =>
            {
                last = PixelProbe.CountColor(image, DecorationColor, DecorationTolerance);
                return condition(last);
            },
            timeoutMs);
        // BitBlt가 막힌 세션(shot == null)은 픽셀 검증 자체가 불가능하다 — 호출부가 대체 경로로 간다.
        return shot is null ? -1 : last;
    }

    /// <summary>선택 상태로 캡처했을 때 잉크는 있고 장식은 없다 — 한 번의 캡처로 양쪽을 동시에 본다.</summary>
    [Fact]
    public void Capture_WithActiveSelection_HasInkButNoDecorationPixels() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewThickStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);

            // 배경 기준선: 장식을 끈 상태의 같은 영역. 서피스가 투명이라 이 영역에는 사용자 화면이
            // 함께 찍히므로, 장식의 몫은 '있다/없다'가 아니라 이 기준선과의 차이다 (PixelProbe.CountColor 주석).
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int ambient = CountUntil(region, _ => true);

            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();
            int shown = ambient < 0 ? -1 : CountUntil(region, count => count > ambient + DecorationPixelMargin);

            if (ambient < 0 || shown <= ambient + DecorationPixelMargin)
            {
                // 비대화형 세션: 장식 레이어 Visibility 전이와 선택 유지 상태를 직접 단언
                var layer = rig.Surface.DecorationLayer;
                Assert.Equal(Visibility.Visible, layer.Visibility);

                rig.Surface.SetDecorationsVisible(false);
                StaRunner.PumpMessages();
                Assert.Equal(Visibility.Collapsed, layer.Visibility);
                Assert.True(rig.Selection.Contains(element));

                rig.Surface.SetDecorationsVisible(true);
                StaRunner.PumpMessages();
                Assert.Equal(Visibility.Visible, layer.Visibility);
                Assert.True(rig.Selection.Contains(element));
                return;
            }

            // 여기까지 왔다면 장식이 실제로 픽셀을 남겼다 — '장식 없음' 검증이 의미를 갖는 전제다.

            // SEL-17 경로: 장식만 숨기고 캡처. 장식 몫이 사라진 프레임이 합성될 때까지 기다린다
            // (고정 슬립이 아니라 조건 충족).
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int hidden = CountUntil(region, count => count <= ambient + DecorationPixelMargin);

            var captured = PixelProbe.CaptureUntil(region, _ => true);
            Assert.NotNull(captured);
            Assert.True(
                PixelProbe.ContainsColor(captured!, InkColor, tolerance: 60),
                "as-seen 인텐트: 잉크는 캡처에 남아야 한다.");
            Assert.True(
                hidden <= ambient + DecorationPixelMargin,
                $"SEL-AC-15: 선택 장식은 캡처 결과물에 들어가면 안 된다 (배경 {ambient}, 장식 표시 {shown}, 숨긴 뒤 {hidden}).");
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    /// <summary>R12: 캡처 후 장식이 복원되고 선택은 그대로다 (QA-6 체감의 기계 절반).</summary>
    [Fact]
    public void Capture_AfterSession_DecorationsRestoredAndSelectionKept() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewThickStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);

            // 캡처 세션 흉내: 장식을 끈 동안의 배경 기준선을 재고, 다시 켠 뒤 그보다 늘었는지 본다.
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int ambient = CountUntil(region, _ => true);

            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();

            Assert.True(rig.Selection.Contains(element), "캡처 왕복이 선택을 해제하면 안 된다.");

            int restored = ambient < 0 ? -1 : CountUntil(region, count => count > ambient + DecorationPixelMargin);
            if (ambient >= 0 && restored > ambient + DecorationPixelMargin)
            {
                return; // 복원된 장식이 실제 픽셀로 확인됐다 (R12).
            }

            // 픽셀을 볼 수 없는 세션(BitBlt 차단·비대화형)에서는 레이어 상태로 대신 확인한다.
            var layer = rig.Surface.DecorationLayer;
            Assert.Equal(Visibility.Visible, layer.Visibility);
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
