using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Protocol.Java;

/// <summary>The legacy server list ping (the 0xFE 0x01 ping used by clients through 1.6, still answered by modern servers). Returns the decoded fields plus latency. Hand-rolled over the raw pipe because it predates the framed protocol entirely; it is not length-prefixed and not routed through <see cref="JavaConnection"/>.</summary>
public static class JavaLegacyPing
{
    /// <summary>Performs a legacy 1.6-style ping against an already-resolved endpoint.</summary>
    public static async Task<LegacyServerStatus> QueryAsync(
        ServerEndpoint endpoint, IConnectionFactory factory, JavaStatusOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.Timeout);
        CancellationToken token = timeoutCts.Token;

        IDuplexPipe pipe = await factory.ConnectAsync(endpoint, token).ConfigureAwait(false);

        long sentAt = Stopwatch.GetTimestamp();
        byte[] request = BuildLegacyRequest(endpoint);
        await pipe.Output.WriteAsync(request, token).ConfigureAwait(false);
        await pipe.Output.FlushAsync(token).ConfigureAwait(false);

        byte[] response = await ReadKickPacketAsync(pipe.Input, token).ConfigureAwait(false);
        TimeSpan latency = Stopwatch.GetElapsedTime(sentAt);
        await pipe.Input.CompleteAsync().ConfigureAwait(false);
        await pipe.Output.CompleteAsync().ConfigureAwait(false);

        return Parse(response, latency);
    }

    private static byte[] BuildLegacyRequest(ServerEndpoint endpoint)
    {
        // 0xFE 0x01 0xFA "MC|PingHost" <len> <proto=0x4A> <hostlen><host UTF-16BE> <port int32>
        using var ms = new MemoryStream();
        ms.WriteByte(0xFE);
        ms.WriteByte(0x01);
        ms.WriteByte(0xFA);

        WriteUtf16(ms, "MC|PingHost");

        byte[] hostBytes = Encoding.BigEndianUnicode.GetBytes(endpoint.Host);
        int rest = 3 + hostBytes.Length + 4; // proto byte skipped-count math below
        // remaining length = 1 (proto) + 2 (host len) + hostBytes + 4 (port)
        int remaining = 1 + 2 + hostBytes.Length + 4;
        WriteShort(ms, (short)remaining);
        ms.WriteByte(0x4A); // protocol version 74
        WriteShort(ms, (short)(hostBytes.Length / 2));
        ms.Write(hostBytes);
        WriteInt(ms, endpoint.Port);
        _ = rest;
        return ms.ToArray();
    }

    private static async Task<byte[]> ReadKickPacketAsync(PipeReader reader, CancellationToken ct)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.Length >= 3)
            {
                byte[] head = buffer.Slice(0, 3).ToArray();
                if (head[0] != 0xFF)
                    throw new ProtocolViolationException("Legacy ping response did not start with a kick packet (0xFF).");

                int strLen = (head[1] << 8) | head[2];
                int totalNeeded = 3 + strLen * 2;
                if (buffer.Length >= totalNeeded)
                {
                    byte[] payload = buffer.Slice(3, strLen * 2).ToArray();
                    reader.AdvanceTo(buffer.GetPosition(totalNeeded));
                    return payload;
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
                throw new ConnectionClosedException(CloseReason.SocketEof, "Server closed before completing legacy ping.");

        }
    }

    private static LegacyServerStatus Parse(byte[] utf16Payload, TimeSpan latency)
    {
        string text = Encoding.BigEndianUnicode.GetString(utf16Payload);
        // Modern format: "§" "1" \0 protocol \0 mcVersion \0 motd \0 online \0 max
        if (text.StartsWith('§'))
        {
            string[] parts = text[1..].Split('\0');
            if (parts.Length >= 6)
                return new LegacyServerStatus(
                    parts[2], parts[3], ParseInt(parts[4]), ParseInt(parts[5]), latency);

        }

        // Very old format: motd § online § max
        string[] old = text.Split('§');
        if (old.Length >= 3)
            return new LegacyServerStatus(null, old[0], ParseInt(old[1]), ParseInt(old[2]), latency);

        return new LegacyServerStatus(null, text, 0, 0, latency);
    }

    private static int ParseInt(string s) =>
        int.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static void WriteUtf16(Stream s, string value)
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value);
        WriteShort(s, (short)value.Length);
        s.Write(bytes);
    }

    private static void WriteShort(Stream s, short value)
    {
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteInt(Stream s, int value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }
}

/// <summary>Fields returned by a legacy server list ping.</summary>
public sealed record LegacyServerStatus(
    string? MinecraftVersion, string Motd, int OnlinePlayers, int MaxPlayers, TimeSpan Latency);
