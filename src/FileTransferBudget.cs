using System.Collections.Concurrent;
using System.Diagnostics;

namespace XingyuConnect;

// ADB multiplexes Sync and interactive streams onto the same device connection.
// Bound and pace every file chunk, and stop bulk traffic when a latency probe is delayed.
internal sealed class FileTransferBudget(DeviceBridge bridge, string serial)
{
    private static readonly ConcurrentDictionary<string, FileTransferBudget> devices = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim scheduling = new(1);
    private int interactiveSessions;
    private bool wasInteractive;
    private double baselineMilliseconds;
    private long nextProbe;
    private long nextChunk;
    private int bytesPerSecond = 4 * 1024 * 1024;

    public static FileTransferBudget ForDevice(DeviceBridge bridge, string serial) => devices.GetOrAdd(serial, _ => new(bridge, serial));

    public IDisposable ProtectInteractive()
    {
        Interlocked.Increment(ref interactiveSessions);
        return new InteractiveLease(this);
    }

    public async ValueTask WaitAsync(int bytes, CancellationToken token)
    {
        await scheduling.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var interactive = Volatile.Read(ref interactiveSessions) > 0;
            if (interactive != wasInteractive)
            {
                bytesPerSecond = interactive ? 32 * 1024 : 4 * 1024 * 1024;
                baselineMilliseconds = 0;
                nextProbe = nextChunk = 0;
                wasInteractive = interactive;
            }
            if (interactive && Stopwatch.GetTimestamp() >= nextProbe)
            {
                while (true)
                {
                    var probe = Stopwatch.StartNew();
                    var responsive = false;
                    try
                    {
                        await bridge.RunAsync(["-s", serial, "shell", "echo", "xingyu-link-probe"], token, 2).ConfigureAwait(false);
                        var elapsed = probe.Elapsed.TotalMilliseconds;
                        if (baselineMilliseconds == 0) baselineMilliseconds = elapsed;
                        baselineMilliseconds = Math.Min(baselineMilliseconds, elapsed);
                        responsive = elapsed <= baselineMilliseconds * 1.5 + 20;
                    }
                    catch (TimeoutException) { }
                    if (responsive)
                    {
                        bytesPerSecond = Math.Min(256 * 1024, bytesPerSecond + 16 * 1024);
                        nextProbe = Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency;
                        break;
                    }
                    bytesPerSecond = 32 * 1024;
                    await Task.Delay(750, token).ConfigureAwait(false);
                    if (Volatile.Read(ref interactiveSessions) == 0) break;
                }
            }
            var now = Stopwatch.GetTimestamp();
            if (nextChunk > now) await Task.Delay(TimeSpan.FromSeconds((double)(nextChunk - now) / Stopwatch.Frequency), token).ConfigureAwait(false);
            nextChunk = Stopwatch.GetTimestamp() + (long)((double)bytes / bytesPerSecond * Stopwatch.Frequency);
        }
        finally { scheduling.Release(); }
    }

    private sealed class InteractiveLease(FileTransferBudget budget) : IDisposable
    {
        private FileTransferBudget? owner = budget;
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref owner, null);
            if (previous is not null) Interlocked.Decrement(ref previous.interactiveSessions);
        }
    }
}

internal sealed class BackgroundTransferStream(Stream inner, FileTransferBudget budget) : Stream
{
    private const int ChunkSize = 16 * 1024;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("后台传输须使用异步读取。");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("后台传输须使用异步写入。");
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var count = Math.Min(buffer.Length, ChunkSize);
        await budget.WaitAsync(count, token).ConfigureAwait(false);
        return await inner.ReadAsync(buffer[..count], token).ConfigureAwait(false);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        for (var offset = 0; offset < buffer.Length; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, buffer.Length - offset);
            await budget.WaitAsync(count, token).ConfigureAwait(false);
            await inner.WriteAsync(buffer.Slice(offset, count), token).ConfigureAwait(false);
        }
    }
    // Ownership stays with the caller so download staging/commit happens after Sync completes.
}
