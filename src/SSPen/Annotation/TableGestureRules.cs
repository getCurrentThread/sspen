namespace SSPen.Annotation;

/// <summary>
/// 표 격자의 행·열 한계 (24단계). 837853a가 여덟 곳(컨트롤러 6, AppState 세터 2)에 뿌린 <c>Math.Clamp(…, 1, 10)</c>의
/// <b>단일 소유 지점</b>이다 (1단계 c2904ff가 리터럴 6/3을 이름 붙인 것과 같은 모양).
/// <c>TableElement</c> 생성자와 <c>AnnotationVisualFactory</c>의 <c>Math.Max(1, …)</c>은 0 방어(요소 불변식)이지
/// 정책 클램프가 아니므로 여기로 오지 않는다.
/// </summary>
public static class TableGridLimits
{
    public const int Min = 1;

    public const int Max = 10;

    public static int Clamp(int value) => Math.Clamp(value, Min, Max);
}

/// <summary>행 축 / 열 축 — 휠(Shift 없음/있음)과 방향키(상하/좌우)가 고르는 대상.</summary>
public enum TableAxis
{
    Rows,
    Columns,
}

/// <summary>진행 중 표의 크기. 드래그 중에는 컨트롤러 필드에만 살고 확정 시점에 <c>AppState</c>로 한 번 쓴다 (fix 57b043d).</summary>
public readonly record struct TableSize(int Rows, int Columns);

/// <summary>
/// 표 제스처의 순수 판정 (24단계, R2/D3). 휠·방향키가 어느 축을 얼마나 움직이는지만 정하고, 입력을 읽거나
/// 시각물을 만지지 않는다 — 그것은 <c>SurfaceInputController</c> 어댑터의 몫이다 (<c>ShapeGestureRules</c> 선례).
/// </summary>
public static class TableGestureRules
{
    /// <summary>휠은 행, Shift+휠은 열 (948b037의 동작 보존).</summary>
    public static TableAxis AxisForWheel(bool shift) => shift ? TableAxis.Columns : TableAxis.Rows;

    /// <summary>한 축만 <paramref name="delta"/>만큼 움직이고 <see cref="TableGridLimits"/> 안으로 재단한다.</summary>
    public static TableSize Adjust(TableSize size, TableAxis axis, int delta) => axis switch
    {
        TableAxis.Rows => size with { Rows = TableGridLimits.Clamp(size.Rows + delta) },
        TableAxis.Columns => size with { Columns = TableGridLimits.Clamp(size.Columns + delta) },
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null),
    };
}
