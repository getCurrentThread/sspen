using System.IO;

namespace SSPen.Tests;

/// <summary>
/// 설치 파일 다운로드 본문의 가짜 (103단계, FINAL-REVIEW-UPDATE-CANCEL). 처음 <c>length</c> 바이트를 돌려준 뒤,
/// <c>stallAfterBody</c>가 거짓이면 끝(0)을, 참이면 다음 <c>Read</c>에서 <b>멈춘다</b> — 네트워크가 본문 도중에 멈춘 상황이다.
/// <c>HttpClient.Timeout</c>은 헤더 수신까지만 걸리므로 그 멈춤은 실제 소켓처럼 <see cref="Stream.Dispose()"/>만이 깨운다
/// (깨어난 Read는 <see cref="ObjectDisposedException"/>). 테스트가 실패해도 스레드풀 스레드가 영영 묶이지 않도록
/// <see cref="StallLimit"/>이 지나면 <see cref="IOException"/>으로 스스로 깬다.
/// 신호 두 개로 작업 스레드의 진행을 관찰한다: 멈춤에 들어섰다(<see cref="WaitUntilStalled"/>), 누군가 스트림을 닫았다
/// (<see cref="WaitUntilDisposed"/> — 취소의 Dispose이거나, 끝까지 읽은 작업 스레드의 using 해제).
/// </summary>
internal sealed class FakeBodyStream(int length, bool stallAfterBody) : Stream
{
    /// <summary>멈춘 Read가 스스로 깨기까지의 한도 — 테스트 기한(5초)보다 길어야 '취소가 깨웠다'와 구별된다.</summary>
    public static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(10);

    private readonly ManualResetEventSlim _stalled = new();
    private readonly ManualResetEventSlim _disposed = new();
    private int _remaining = length;

    /// <summary>작업 스레드가 멈춘 Read에 들어설 때까지 기다린다.</summary>
    public bool WaitUntilStalled(TimeSpan timeout) => _stalled.Wait(timeout);

    /// <summary>스트림이 닫힐 때까지 기다린다.</summary>
    public bool WaitUntilDisposed(TimeSpan timeout) => _disposed.Wait(timeout);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed.IsSet, this);
        if (_remaining > 0)
        {
            var n = Math.Min(count, _remaining);
            Array.Fill(buffer, (byte)0x5A, offset, n);
            _remaining -= n;
            return n;
        }
        if (!stallAfterBody)
        {
            return 0;
        }

        _stalled.Set();
        if (!_disposed.Wait(StallLimit))
        {
            throw new IOException("가짜 본문이 한도까지 멈춰 있었다 — 아무도 스트림을 닫아 Read를 깨우지 않았다.");
        }
        throw new ObjectDisposedException(nameof(FakeBodyStream));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed.Set();
        }
        base.Dispose(disposing);
    }
}
