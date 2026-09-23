using SSPen.Annotation;
using SSPen.Interop;

namespace SSPen.IntegrationTests;

/// <summary>
/// 통합 스위트의 <see cref="ContentSurfaceWindow"/> 생성 한 곳 (61단계, A8-6).
/// 서피스 생성자의 12인자를 파일마다 복사해 두면 이음매가 하나 늘 때마다(54단계처럼) 다섯 파일을 모두 고쳐야 했다.
/// 인자 값은 옮기기 전 사본과 글자 그대로 같다: dpi 1.0, 변형 커밋 = <c>ledger.RecordTransform</c>(드롭 지점 무시),
/// 클릭 통과 요청 무동작, zAnchor 0(→ AnchorBelow 무동작 전제 — z-밴드 증인만 앵커를 넘긴다, 71단계), 표 배지 문구 <c>"{rows}x{columns}"</c>.
/// 각 파일의 Rig 레코드·모니터 선택 규칙·Show 순서는 호출부에 그대로 남는다 — 여기는 생성만 맡는다.
/// <c>Application</c>은 만들지 않는다 (LD-4/R24).
/// </summary>
internal static class SurfaceRigs
{
    /// <summary>
    /// 테스트용 서피스를 만든다. <paramref name="ownerOf"/>를 생략하면 "<paramref name="document"/>에 있으면 그 문서"이고,
    /// <paramref name="fading"/>을 생략하면 서피스마다 새 페이딩 컨트롤러를 둔다.
    /// <paramref name="zAnchor"/>를 생략하면 앵커 0이라 z-훅(AnchorBelow/KeepBelow)이 아무것도 하지 않는다.
    /// </summary>
    internal static ContentSurfaceWindow NewSurface(
        MonitorSurfaceInfo monitor,
        AppState state,
        AnnotationDocument document,
        UndoLedger ledger,
        SelectionModel selection,
        Func<AnnotationElement, AnnotationDocument?>? ownerOf = null,
        FadingInkController? fading = null,
        Func<nint>? zAnchor = null) => new ContentSurfaceWindow(
            monitor,
            state,
            document,
            ledger,
            fading ?? new FadingInkController(new FadeSchedulerCore()),
            selection,
            ownerOf ?? (e => document.Elements.Contains(e) ? document : null),
            _ => 1.0,
            (deltas, _) => ledger.RecordTransform(deltas),
            () => { },
            zAnchor ?? (() => 0),
            (rows, columns) => $"{rows}x{columns}");
}
