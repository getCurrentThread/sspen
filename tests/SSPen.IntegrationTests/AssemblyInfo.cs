using Xunit;

// 이 스위트는 **실제 화면**이라는 단일 공유 자원을 두고 경합한다: BitBlt 픽셀 단언은
// 캡처 영역 위에 다른 테스트의 창이 떠 있으면 조용히 틀린 색을 읽는다.
// xUnit은 기본적으로 테스트 클래스를 병렬 실행하므로, 전체 모니터를 덮는 topmost 서피스를
// 띄우는 클래스(MonitorTransferTests·SelectionCaptureTests·DecorationRenderTests)와
// 마커 픽셀을 읽는 클래스(BitBltCaptureTests)가 겹치면 후자가 검은 픽셀을 읽는다.
//
// 개별 실행에서는 통과하고 전체 실행에서만 실패하는 전형적인 격리 결함이라,
// 어셈블리 단위로 병렬화를 끈다. 통합 스위트는 원래 실기 리그 전용이라 실행 시간보다
// 결정성이 중요하다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
