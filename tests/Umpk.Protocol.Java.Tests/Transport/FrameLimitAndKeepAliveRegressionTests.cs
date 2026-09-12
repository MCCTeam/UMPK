using System;
using System.Threading;
using System.Threading.Tasks;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Pins maximum-frame-length enforcement and the 15-second keep-alive timeout window.</summary>
public class FrameLimitAndKeepAliveRegressionTests
{
    private static CancellationToken Ct() =>
        new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    // Frames larger than JavaConnectionOptions.MaxFrameLength are protocol violations and must fail the connection rather than be delivered.
    [Fact]
    public async Task R3_1_MaxFrameLength_IsEnforced()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            MaxFrameLength = 1024,               // small cap
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.Start();

        // wire id (1 byte) + 5000-byte body -> frame body 5001 bytes, well over the 1024 cap.
        var frameContent = new byte[5001];
        frameContent[0] = 0x2A;
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, frameContent);

        // Per the option's documented contract, an oversized frame must fail the connection.
        await Assert.ThrowsAsync<ConnectionClosedException>(
            async () => await conn.ReceiveAsync(Ct()));
    }

    // Guards the vanilla 15-second keep-alive window. A keep-alive issued at T is a disconnect at T+15s if still pending. The default service now uses that same 15s timeout, so an unanswered keep-alive must be considered timed out at 15s.
    [Fact]
    public void R3_4_KeepAlive_TimesOutAtVanilla15s()
    {
        var svc = new KeepAliveService(); // default ctor: interval 15s, timeout 15s
        var t0 = DateTimeOffset.UnixEpoch;

        Assert.True(svc.TryIssue(t0, out _));                 // issue a keep-alive at T0
        // Vanilla disconnects at T0 + 15s when still pending.
        Assert.True(svc.IsTimedOut(t0 + TimeSpan.FromSeconds(15)));
    }
}
