using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.E2ETests;

/// <summary>
/// E2E 시나리오 작성을 위한 플루언트 사용자 상호작용 시뮬레이터.
/// 툴바 클릭, 마우스 드로잉, 선택/변환/삭제, 단축키 입력 등 사용자의 모든 행위를 추상화하여 제공한다.
/// </summary>
public sealed class VirtualUserActor
{
    private readonly AppController _app;

    public VirtualUserActor(AppController app)
    {
        _app = app;
    }

    public AppController App => _app;
    public AppState State => _app.State;
    public SelectionModel Selection => _app.Selection;
    public UndoLedger Ledger => _app.Ledger;
    public IReadOnlyList<ContentSurfaceWindow> Surfaces => _app.Surfaces;

    public ContentSurfaceWindow Surface(int monitorIndex = 1) => _app.Surfaces[monitorIndex];
    public AnnotationDocument Document(int monitorIndex = 1) => _app.Surfaces[monitorIndex].Document;

    public VirtualUserActor Pump()
    {
        E2EAppFixture.PumpMessages();
        return this;
    }

    public VirtualUserActor SelectTool(ToolKind tool)
    {
        _app.State.ActiveTool = tool;
        Pump();
        return this;
    }

    public VirtualUserActor SetColor(Color color)
    {
        _app.State.CurrentColor = color;
        Pump();
        return this;
    }

    public VirtualUserActor SetThickness(ThicknessStep step)
    {
        _app.State.Thickness = step;
        Pump();
        return this;
    }

    public VirtualUserActor SetThickness(double thickness)
    {
        var step = thickness switch
        {
            <= 2 => ThicknessStep.XSmall,
            <= 4 => ThicknessStep.Small,
            <= 8 => ThicknessStep.Medium,
            <= 12 => ThicknessStep.Large,
            _ => ThicknessStep.XLarge,
        };
        return SetThickness(step);
    }

    public VirtualUserActor ToggleFading(bool? enabled = null)
    {
        _app.State.FadingInk = enabled ?? !_app.State.FadingInk;
        Pump();
        return this;
    }

    public VirtualUserActor SetBoardMode(BoardMode mode)
    {
        _app.State.Board = mode;
        Pump();
        return this;
    }

    /// <summary>지정된 서피스에서 마우스 스트로크 드로잉 시뮬레이션.</summary>
    public VirtualUserActor DrawStroke(Point start, Point end, int monitorIndex = 1, int steps = 5)
    {
        var input = Surface(monitorIndex).Input;
        input.PointerDown(start, shift: false);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            var cur = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            input.PointerMove(cur, shift: false, leftPressed: true);
        }
        input.PointerUp(end, shift: false);
        Pump();
        return this;
    }

    /// <summary>지정된 서피스에서 도형 그리기 시뮬레이션.</summary>
    public VirtualUserActor DrawShape(ShapeKind kind, Point start, Point end, int monitorIndex = 1, bool shift = false)
    {
        SelectTool(kind switch
        {
            ShapeKind.Line => ToolKind.Line,
            ShapeKind.Arrow => ToolKind.Arrow,
            ShapeKind.Rectangle => ToolKind.Rectangle,
            ShapeKind.Ellipse => ToolKind.Ellipse,
            _ => ToolKind.Rectangle,
        });

        var input = Surface(monitorIndex).Input;
        input.PointerDown(start, shift);
        input.PointerMove(end, shift, leftPressed: true);
        input.PointerUp(end, shift);
        Pump();
        return this;
    }

    /// <summary>지정된 서피스에서 텍스트 입력 시뮬레이션.</summary>
    public VirtualUserActor AddText(Point pos, string text, int monitorIndex = 1)
    {
        SelectTool(ToolKind.Text);
        var surface = Surface(monitorIndex);
        var input = surface.Input;

        input.PointerDown(pos, shift: false);
        Pump();

        // 캔버스에 생성된 텍스트박스를 찾아 텍스트 주입
        var textBox = surface.InkCanvas.Children.OfType<System.Windows.Controls.TextBox>().LastOrDefault();
        if (textBox is not null)
        {
            textBox.Text = text;
        }

        // ESC 또는 바깥 클릭으로 커밋
        input.Escape();
        Pump();
        return this;
    }

    /// <summary>지정된 위치 지우개 삭제 시뮬레이션.</summary>
    public VirtualUserActor EraseAt(Point pos, int monitorIndex = 1)
    {
        SelectTool(ToolKind.Eraser);
        var input = Surface(monitorIndex).Input;
        input.PointerDown(pos, shift: false);
        input.PointerUp(pos, shift: false);
        Pump();
        return this;
    }

    /// <summary>마우스 클릭 시뮬레이션 (단일 선택, 토글, 빈 영역 해제).</summary>
    public VirtualUserActor Click(Point pos, int monitorIndex = 1, bool shift = false)
    {
        var input = Surface(monitorIndex).Input;
        input.PointerDown(pos, shift);
        input.PointerUp(pos, shift);
        Pump();
        return this;
    }

    /// <summary>마우스 드래그 시뮬레이션 (선택 이동, 스케일링, 마키 영역 선택 등).</summary>
    public VirtualUserActor Drag(Point start, Point end, int monitorIndex = 1, bool shift = false, int steps = 5)
    {
        var input = Surface(monitorIndex).Input;
        input.PointerDown(start, shift);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            var cur = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            input.PointerMove(cur, shift, leftPressed: true);
        }
        input.PointerUp(end, shift);
        Pump();
        return this;
    }

    /// <summary>마우스 휠 스케일링 시뮬레이션.</summary>
    public VirtualUserActor Wheel(Point pos, int notches, int monitorIndex = 1)
    {
        var input = Surface(monitorIndex).Input;
        input.Wheel(pos, notches);
        Pump();
        return this;
    }

    /// <summary>실행 취소 (Undo).</summary>
    public VirtualUserActor Undo()
    {
        _app.Undo();
        Pump();
        return this;
    }

    /// <summary>전체 지우기 (Clear All).</summary>
    public VirtualUserActor ClearAll()
    {
        _app.ClearAll();
        Pump();
        return this;
    }

    /// <summary>선택 요소 삭제.</summary>
    public VirtualUserActor DeleteSelection()
    {
        _app.DeleteSelection();
        Pump();
        return this;
    }

    /// <summary>화면 캡처 세션 시작.</summary>
    public VirtualUserActor StartCapture()
    {
        _app.StartCapture();
        Pump();
        return this;
    }

    /// <summary>설정 창 열기.</summary>
    public VirtualUserActor OpenSettings()
    {
        _app.OpenSettings();
        Pump();
        return this;
    }
}
