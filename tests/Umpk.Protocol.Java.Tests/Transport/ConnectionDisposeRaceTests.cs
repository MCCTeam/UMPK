using System.Buffers;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Concurrency coverage for a send racing <see cref="JavaConnection.DisposeAsync"/>. The write path is serialized through a <c>SemaphoreSlim</c> that dispose can tear down while a send is still touching it (either blocked acquiring it, or between acquiring and releasing it while <c>WriteFrameAsync</c> is still in flight). This is a genuine race, not a deterministic sequence, so these tests drive it under real concurrency and repeatedly rather than asserting a single interleaving.</summary>
public class ConnectionDisposeRaceTests
{
    private readonly ITestOutputHelper _output;

    public ConnectionDisposeRaceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    static ConnectionDisposeRaceTests()
    {
        // A wave of concurrent senders competes with whatever else this process is doing for thread pool capacity. Raising the floor once, for the whole test run, keeps the wave's timing (and therefore how well it exercises the race) from being at the mercy of the pool's slow, throttled growth under load.
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(Math.Max(workerThreads, 128), completionPortThreads);
    }

    /// <summary>Fires a wave of concurrent sends (mixing both write-path entry points, <see cref="JavaConnection.SendAsync"/> and <see cref="JavaConnection.SendFrameAsync"/>, and using <see cref="CancellationToken.None"/> like the real per-tick and keep-alive send sites do) against a connection that gets disposed out from under them.</summary>
    /// <remarks>The test does not add artificial delay or force pipe backpressure, because either would exercise a different synchronization path. It uses exactly two senders, one per public write entry point, so the real per-tick-send-versus-disposal race is repeated without making runtime depend on thread-pool load.</remarks>
    /// <returns>The number of send results, across this one attempt, that came back as a bare <see cref="ObjectDisposedException"/> instead of the typed <see cref="ConnectionClosedException"/> (or a clean success). Zero is required.</returns>
    private static async Task<int> RunOneRaceAttemptAsync()
    {
        DuplexPipePair pair = DuplexPipePair.Create();

        var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.BindCodec(new PlainEncodeBinding(), PacketFlow.Clientbound);
        conn.Start();

        byte[] body = [1, 2, 3, 4];

        const int concurrentSenders = 2;
        var sendTasks = new Task<Exception?>[concurrentSenders];
        for (int i = 0; i < concurrentSenders; i++)
        {
            bool useSendAsync = i % 2 == 0;
            sendTasks[i] = Task.Run(async () =>
            {
                try
                {
                    if (useSendAsync)
                        await conn.SendAsync(body, CancellationToken.None).ConfigureAwait(false);

                    else
                        await conn.SendFrameAsync(1, body, CancellationToken.None).ConfigureAwait(false);

                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });
        }

        // Let the wave actually start contending for the write lock before disposal lands on top of it, mirroring TeardownSessionAsync disposing the connection while a per-tick or keep-alive send that used CancellationToken.None is still in flight (or still queued behind one).
        await Task.Yield();
        await conn.DisposeAsync().ConfigureAwait(false);

        // The watchdog turns a connection or harness deadlock into a bounded test failure. This attempt contains no expected unbounded wait, so the window only absorbs ordinary scheduling delay and unrelated contention on a busy machine.
        Task<Exception?[]> whenAll = Task.WhenAll(sendTasks);
        Task completed = await Task.WhenAny(whenAll, Task.Delay(TimeSpan.FromSeconds(45))).ConfigureAwait(false);
        Assert.True(ReferenceEquals(completed, whenAll), "Sends did not complete within the watchdog window; likely deadlock.");
        Exception?[] results = await whenAll.ConfigureAwait(false);

        int leaked = 0;
        foreach (Exception? ex in results)
        {
            if (ex is ObjectDisposedException)
            {
                leaked++;
                continue;
            }

            if (ex is not null)
            {
                // Every non-null result must be the typed disconnect signal, asserted by type, not by absence of an error.
                Assert.IsType<ConnectionClosedException>(ex);
            }
        }

        return leaked;
    }

    /// <summary>Concurrent send/dispose attempts must never leak <see cref="ObjectDisposedException"/>.</summary>
    [Fact]
    public async Task SendRacingDispose_NeverLeaksObjectDisposedException()
    {
        const int attempts = 10;
        int totalLeaked = 0;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int leaked = await RunOneRaceAttemptAsync();
            totalLeaked += leaked;
        }

        _output.WriteLine($"ObjectDisposedException leaks across {attempts} internal attempts: {totalLeaked}");
        Assert.Equal(0, totalLeaked);
    }

    /// <summary>Minimal codec binding for this test only: encodes any <c>byte[]</c> packet as wire id 1 with the bytes as the body. No artificial delay - see the remarks on <see cref="RunOneRaceAttemptAsync"/> for why. Decoding is irrelevant here (nothing reads back), so it always reports unknown.</summary>
    private sealed class PlainEncodeBinding : IFrameCodecBinding
    {
        public bool TryDecode(in FrameDecodeContext context, out object packet)
        {
            packet = null!;
            return false;
        }

        public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, IBufferWriter<byte> output)
        {
            if (packet is not byte[] body)
                return false;

            output.GetSpan(1)[0] = 1;
            output.Advance(1);
            body.CopyTo(output.GetSpan(body.Length));
            output.Advance(body.Length);
            return true;
        }

        public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase)
        {
            nextPhase = context.Phase;
            return false;
        }

        public bool IsCompressionEnablePoint(in FrameDecodeContext context) => false;
    }

    /// <summary>When the read loop closes first, queued senders still need a cancellation path before disposal tears down the semaphore. This test drives the read side to EOF before disposing and verifies the queued sender completes.</summary>
    [Fact]
    public async Task SendRacingReadLoopEofFirst_NeverHangs()
    {
        DuplexPipePair pair = DuplexPipePair.Create();
        var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });

        using var aEntered = new ManualResetEventSlim(initialState: false);
        using var releaseA = new ManualResetEventSlim(initialState: false);
        conn.BindCodec(new BlockingEncodeBinding(aEntered, releaseA), PacketFlow.Clientbound);
        conn.Start();

        byte[] body = [1, 2, 3, 4];

        // A acquires the write lock and parks inside TryEncode, holding it - the shape a per-tick send racing teardown always has: SOMETHING is either holding or about to be queued on the one write lock when the connection closes.
        Task<Exception?> aTask = Task.Run(async () =>
        {
            try
            {
                await conn.SendAsync(body, CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });
        Assert.True(aEntered.Wait(TimeSpan.FromSeconds(5)), "A never reached TryEncode; test setup is broken.");

        // B queues behind A on the write lock (A holds the only permit, so once B's task has been scheduled at all it is deterministically blocked in WaitAsync). B is the one this test is actually about: it must not hang.
        Task<Exception?> bTask = Task.Run(async () =>
        {
            try
            {
                await conn.SendFrameAsync(1, body, CancellationToken.None).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        // Give B a beat to actually reach _writeLock.WaitAsync before EOF lands - this is about giving the scheduler the chance to run B's task at all, not a race signal (A already holds the only permit, so B blocking there is deterministic once it runs).
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // Drive the READ side to EOF: the read loop's Complete() sets the connection closed BEFORE any CloseAsync/DisposeAsync call, reproducing the ordering seen live.
        await pair.Right.Output.CompleteAsync();

        // Block until the read loop has actually run Complete() - a deterministic synchronization point (Complete() completes the inbound channel), not a timing guess, that proves the connection closed via the READ LOOP's own path before any dispose call below.
        await Assert.ThrowsAsync<ConnectionClosedException>(
            async () => await conn.ReceiveAsync(CancellationToken.None));

        await conn.DisposeAsync();

        // Let A finish encoding now that disposal has happened. A's own outcome (clean completion, or an exception) is a documented, accepted residual of this ordering and is not what this test asserts on - only B is.
        releaseA.Set();

        // The watchdog proves B has a wake-up path for this ordering.
        Task<Exception?[]> both = Task.WhenAll(aTask, bTask);
        Task completed = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(
            ReferenceEquals(completed, both),
            "B (queued behind A on the write lock) never completed. Regression: a " +
            "read-loop-first close never armed the cancellation linkage, so a queued sender has no " +
            "wake-up path once the semaphore is disposed.");

        Exception?[] results = await both;

        // B never held the lock and never got out any bytes: its failure must be the typed disconnect signal, asserted by type, not by mere absence of a hang.
        Assert.NotNull(results[1]);
        Assert.IsType<ConnectionClosedException>(results[1]);
    }

    /// <summary><see cref="JavaConnection.CloseAsync"/>'s compare-and-swap makes its own body idempotent, but a concurrent <see cref="JavaConnection.DisposeAsync"/> caller must also wait for teardown. Otherwise the pooled frame-reader buffer can be returned while the read loop still uses it.</summary>
    [Fact]
    public async Task ConcurrentDisposeAsync_SecondCallerWaitsForTeardown()
    {
        DuplexPipePair pair = DuplexPipePair.Create();
        var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });

        using var readLoopEntered = new ManualResetEventSlim(initialState: false);
        using var releaseReadLoop = new ManualResetEventSlim(initialState: false);
        conn.BindCodec(new BlockingDecodeBinding(readLoopEntered, releaseReadLoop), PacketFlow.Clientbound);
        conn.Start();

        // Feed one frame so the read loop reaches TryDecode and blocks there - the read loop is genuinely "in progress" (still the FrameReader instance actively in use) when disposal below races it.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 2, 3]);
        Assert.True(
            readLoopEntered.Wait(TimeSpan.FromSeconds(5)), "Read loop never reached TryDecode; test setup is broken.");

        Task dispose1 = Task.Run(async () => await conn.DisposeAsync());
        Task dispose2 = Task.Run(async () => await conn.DisposeAsync());

        // Give both callers a beat to actually start racing CloseAsync's closed-flag CAS.
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // Neither may have returned yet: the read loop (and hence teardown) is still blocked inside TryDecode. The CAS-losing caller must not return here regardless, before the actual teardown (including FrameReader.Release()) had run.
        Assert.False(dispose1.IsCompleted, "First DisposeAsync returned before the read loop finished.");
        Assert.False(
            dispose2.IsCompleted,
            "Second DisposeAsync returned before the read loop finished. Regression.");

        releaseReadLoop.Set();

        Task both = Task.WhenAll(dispose1, dispose2);
        Task completed = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(
            ReferenceEquals(completed, both),
            "DisposeAsync calls did not complete after the read loop was released.");

        await both; // rethrow if either call faulted
    }

    /// <summary>This case covers an explicit <see cref="JavaConnection.CloseAsync"/> call racing a concurrent <c>DisposeAsync</c>. <c>CloseAsync</c> is public and does not participate in the <c>_disposeStarted</c> guard, so when it wins the closed-flag transition, a concurrent <c>DisposeAsync</c>'s own internal <c>CloseAsync</c> call short-circuits immediately (never waiting for the read loop) and could proceed straight to <c>_frameReader.Release()</c> regardless. This is exactly the shape <c>UmpkClient.DisconnectAsync</c> produces: it calls <c>CloseAsync</c> then <c>DisposeAsync</c> in sequence, and can race an independent reconnect-path <c>DisposeAsync</c> against either half. Disposal must join the read loop before release, regardless of which method wins the close.</summary>
    [Fact]
    public async Task CloseAsyncRacingDisposeAsync_DoesNotReleasePooledBufferEarly()
    {
        DuplexPipePair pair = DuplexPipePair.Create();
        var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });

        using var readLoopEntered = new ManualResetEventSlim(initialState: false);
        using var releaseReadLoop = new ManualResetEventSlim(initialState: false);
        conn.BindCodec(new BlockingDecodeBinding(readLoopEntered, releaseReadLoop), PacketFlow.Clientbound);
        conn.Start();

        // Feed one frame so the read loop reaches TryDecode and blocks there, still using FrameReader's pooled scratch buffer.
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [1, 2, 3]);
        Assert.True(
            readLoopEntered.Wait(TimeSpan.FromSeconds(5)), "Read loop never reached TryDecode; test setup is broken.");

        // Caller 1: an explicit CloseAsync() call (mirrors UmpkClient.DisconnectAsync's own CloseAsync call). Caller 2: a concurrent DisposeAsync() call (mirrors DisconnectAsync's own follow-up dispose, or an independent reconnect-path dispose racing it). Which of the two actually wins the closed-flag transition is a genuine, immaterial race - CloseAsync is idempotent by design, so whichever one loses legitimately returns immediately, and that is not what this test is about. What matters is DisposeAsync specifically, because it is the only one of the two that calls _frameReader.Release(). It must wait for the read loop before returning that pooled buffer, regardless of which closer wins the state transition.
        Task closeTask = Task.Run(async () => await conn.CloseAsync(CloseReason.Local, CancellationToken.None));
        Task disposeTask = Task.Run(async () => await conn.DisposeAsync());

        // Give both callers a beat to actually start racing the closed-flag transition.
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        // DisposeAsync specifically may not have returned yet: the read loop is still blocked inside TryDecode, still using the pooled buffer, and DisposeAsync is the only one of the two that ever releases it. (closeTask is deliberately NOT asserted on here: whichever of the two calls loses the closed-flag race legitimately returns immediately by design, and that can be either one - it does not indicate a problem.)
        Assert.False(
            disposeTask.IsCompleted,
            "DisposeAsync returned before the read loop finished while racing a concurrent " +
            "CloseAsync call - the pooled-buffer use-after-return this test targets.");

        releaseReadLoop.Set();

        Task both = Task.WhenAll(closeTask, disposeTask);
        Task completed = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(
            ReferenceEquals(completed, both),
            "CloseAsync/DisposeAsync did not complete after the read loop was released.");

        await both; // rethrow if either call faulted
    }

    /// <summary>Codec binding for <see cref="SendRacingReadLoopEofFirst_NeverHangs"/>: <see cref="TryEncode"/> signals <paramref name="entered"/> then blocks synchronously on <paramref name="release"/>, so the caller can park a sender holding the write lock for as long as needed. Decoding is irrelevant (nothing is read back in that test).</summary>
    private sealed class BlockingEncodeBinding(ManualResetEventSlim entered, ManualResetEventSlim release)
        : IFrameCodecBinding
    {
        public bool TryDecode(in FrameDecodeContext context, out object packet)
        {
            packet = null!;
            return false;
        }

        public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, IBufferWriter<byte> output)
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));

            if (packet is not byte[] body)
                return false;

            output.GetSpan(1)[0] = 1;
            output.Advance(1);
            body.CopyTo(output.GetSpan(body.Length));
            output.Advance(body.Length);
            return true;
        }

        public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase)
        {
            nextPhase = context.Phase;
            return false;
        }

        public bool IsCompressionEnablePoint(in FrameDecodeContext context) => false;
    }

    /// <summary>Codec binding for <see cref="ConcurrentDisposeAsync_SecondCallerWaitsForTeardown"/>: <see cref="TryDecode"/> signals <paramref name="entered"/> then blocks synchronously on <paramref name="release"/>, parking the read loop mid-frame so disposal can race it. Encoding is irrelevant (nothing is sent in that test).</summary>
    private sealed class BlockingDecodeBinding(ManualResetEventSlim entered, ManualResetEventSlim release)
        : IFrameCodecBinding
    {
        public bool TryDecode(in FrameDecodeContext context, out object packet)
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            packet = null!;
            return false;
        }

        public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow outboundFlow, IBufferWriter<byte> output) => false;

        public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase)
        {
            nextPhase = context.Phase;
            return false;
        }

        public bool IsCompressionEnablePoint(in FrameDecodeContext context) => false;
    }
}
