using System.Buffers;
using System.Security.Cryptography;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>The read loop pauses at the encryption-request frame boundary until the consumer enables encryption, mirroring the set-compression gate. Over a zero-latency in-memory pipe the peer's first encrypted frame can arrive before <see cref="JavaConnection.EnableEncryption"/> runs; without the gate those bytes would be appended to the read buffer undecrypted. With the gate the read loop parks after the encryption-request frame and resumes, with the decryptor installed, only after EnableEncryption.</summary>
public class EncryptionGateTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    /// <summary>A connection reports whether encryption was ever enabled on it. That fact is the only way a caller can tell an online-mode server (which sends an encryption request) from an offline-mode one (which never does), and a client that signs chat needs it: an offline-mode server assigns an offline UUID, so a Mojang-signed profile key can never validate against it and the join is rejected.</summary>
    [Fact]
    public async Task Connection_ReportsWhetherEncryptionWasEnabled()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });

        Assert.False(conn.IsEncrypted);

        conn.EnableEncryption(RandomNumberGenerator.GetBytes(16));
        Assert.True(conn.IsEncrypted);
    }

    [Fact]
    public async Task EncryptionRequestFrame_PausesReader_UntilEnableEncryption()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(16);

        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        // Wire id 3 is the encryption-request frame; wire id 9 is the first encrypted frame.
        conn.BindCodec(new FakeCodecBinding([3, 9], encryptionWireId: 3), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Login);
        conn.Start();

        long observed = 0;
        conn.PacketObserved += _ => Interlocked.Increment(ref observed);

        // The encryption-request frame is sent in the clear (encryption not yet on).
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, [3]);

        // Consume the encryption-request frame. The read loop now parks at the encryption boundary.
        InboundItem request = await conn.ReceiveAsync(Ct());
        Assert.Equal(3, request.Frame.WireId);

        // The peer's first encrypted frame arrives now (as it would in the race: after the request is processed but before the consumer has enabled encryption). Produce the exact AES-CFB8 bytes with a sender seeded on the same secret. Because the read loop is parked at the gate, these bytes must stay in the pipe undecrypted, not get appended to the read buffer raw.
        byte[] encryptedFrame = await BuildEncryptedFrameBytesAsync(secret, wireId: 9, payloadLength: 32);
        await pair.Right.Output.WriteAsync(encryptedFrame, Ct());
        await pair.Right.Output.FlushAsync(Ct());

        await Task.Delay(150);
        Assert.Equal(1, Interlocked.Read(ref observed)); // encrypted frame not read while parked

        // Enabling encryption releases the gate; the encrypted frame now decrypts with the right cipher.
        conn.EnableEncryption(secret);
        InboundItem next = await conn.ReceiveAsync(Ct());
        Assert.Equal(9, next.Frame.WireId);
        Assert.Equal(32 - 1, next.Frame.Payload.Length); // 32-byte wire content minus the 1-byte wire id
    }

    // Produces the on-wire bytes of a single AES-CFB8-encrypted frame, as an encryption-enabled sender (seeded with the same secret) would emit, by driving a sender over a throwaway pipe.
    private static async Task<byte[]> BuildEncryptedFrameBytesAsync(byte[] secret, int wireId, int payloadLength)
    {
        var pair = DuplexPipePair.Create();
        await using var sender = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        sender.EnableEncryption(secret);

        byte[] payload = new byte[payloadLength - 1];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;

        await sender.SendFrameAsync(wireId, payload, Ct());
        await pair.Left.Output.CompleteAsync();

        System.IO.Pipelines.ReadResult read = await pair.Right.Input.ReadAsync(Ct());
        byte[] bytes = read.Buffer.ToArray();
        pair.Right.Input.AdvanceTo(read.Buffer.End);
        return bytes;
    }
}
