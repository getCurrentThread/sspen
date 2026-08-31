using Xunit;

// E2E UI 테스트는 STA 스레드와 창 자원을 사용하므로 순차 실행한다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
