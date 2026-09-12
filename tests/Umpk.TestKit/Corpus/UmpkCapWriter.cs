using System.Security.Cryptography;
using System.Text.Json;

namespace Umpk.TestKit.Corpus;

/// <summary>Serializes a set of <see cref="RecordedFrame"/>s to the <c>.umpkcap</c> binary format plus its JSON manifest sidecar. Used by the recorder tool and by recorder self-tests. Writing is a pure function of the frames (given a fixed recorded-at timestamp), so re-serializing the same frames yields byte-identical output; the manifest content hash covers only the record bytes, so it is stable even across different header timestamps.</summary>
public static class UmpkCapWriter
{
    /// <summary>Serializes frames into a self-contained capture and its manifest. Returns both byte blobs so callers can write files or hold them in memory (self-tests do the latter).</summary>
    public static (byte[] Capture, byte[] Manifest, CorpusManifest ManifestModel) Serialize(
        IReadOnlyList<RecordedFrame> frames,
        string minecraftVersion,
        int protocol,
        string scenario,
        DateTimeOffset recordedAtUtc,
        long durationMs,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(minecraftVersion);
        ArgumentNullException.ThrowIfNull(scenario);

        byte[] records = SerializeRecords(frames);
        string contentHash = ComputeContentHash(frames);

        int clientbound = 0;
        int serverbound = 0;
        foreach (RecordedFrame f in frames)
            if (f.Direction == CorpusDirection.Clientbound)
                clientbound++;

            else
                serverbound++;

        byte[] capture = new byte[UmpkCapFormat.HeaderLength + records.Length];
        UmpkCapFormat.WriteHeader(capture, recordedAtUtc.ToUnixTimeMilliseconds(), protocol, frames.Count);
        records.CopyTo(capture.AsSpan(UmpkCapFormat.HeaderLength));

        var manifest = new CorpusManifest
        {
            MinecraftVersion = minecraftVersion,
            Protocol = protocol,
            Scenario = scenario,
            FrameCount = frames.Count,
            ClientboundCount = clientbound,
            ServerboundCount = serverbound,
            RecordedAtUtc = recordedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DurationMs = durationMs,
            ContentHash = contentHash,
            Notes = notes,
        };

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, CorpusManifestJsonContext.Default.CorpusManifest);
        return (capture, manifestBytes, manifest);
    }

    /// <summary>Serializes frames and writes both the capture and its manifest sidecar to disk.</summary>
    public static async Task WriteFilesAsync(
        string capturePath,
        IReadOnlyList<RecordedFrame> frames,
        string minecraftVersion,
        int protocol,
        string scenario,
        DateTimeOffset recordedAtUtc,
        long durationMs,
        string? notes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(capturePath);
        (byte[] capture, byte[] manifest, _) =
            Serialize(frames, minecraftVersion, protocol, scenario, recordedAtUtc, durationMs, notes);

        string? dir = Path.GetDirectoryName(capturePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(capturePath, capture, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(capturePath + ".json", manifest, ct).ConfigureAwait(false);
    }

    private static byte[] SerializeRecords(IReadOnlyList<RecordedFrame> frames)
    {
        int total = 0;
        foreach (RecordedFrame f in frames)
            total += UmpkCapFormat.RecordPrefixLength + f.Body.Length;

        byte[] buffer = new byte[total];
        int offset = 0;
        foreach (RecordedFrame f in frames)
        {
            UmpkCapFormat.WriteRecordPrefix(buffer.AsSpan(offset), f);
            offset += UmpkCapFormat.RecordPrefixLength;
            f.Body.CopyTo(buffer.AsSpan(offset));
            offset += f.Body.Length;
        }

        return buffer;
    }

    /// <summary>Computes the manifest content hash (SHA-256, lower-hex) over the payload identity of the frames. Shared by <see cref="Serialize"/> and by the loader's integrity check so both compute the hash the same way. See <see cref="ContentHashBytes"/> for exactly what the hash covers.</summary>
    internal static string ComputeContentHash(IReadOnlyList<RecordedFrame> frames) =>
        Convert.ToHexStringLower(SHA256.HashData(ContentHashBytes(frames)));

    // The content hash covers the payload identity of each recorded unit in order: direction, wire id, and body. Timing/sequence and the contextual phase are deliberately EXCLUDED, so re-records of identical payload traffic are idempotent by hash even when driven so the phase context or timestamps differ. Consequence tracked as: phase-label corruption in a committed corpus is NOT detectable by this hash (nor by the loader's integrity check that verifies it), even though the latch/fallback machinery keys on phase labels. Extending the hash to cover phase would break the recorder's idempotency contract (the re-record path drives a bare frame client whose phase context differs from the original), so the exclusion stands and is documented rather than closed here.
    private static byte[] ContentHashBytes(IReadOnlyList<RecordedFrame> frames)
    {
        int total = 0;
        foreach (RecordedFrame f in frames)
            total += 1 + 4 + f.Body.Length;

        byte[] buffer = new byte[total];
        int offset = 0;
        foreach (RecordedFrame f in frames)
        {
            buffer[offset++] = (byte)f.Direction;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), f.WireId);
            offset += 4;
            f.Body.CopyTo(buffer.AsSpan(offset));
            offset += f.Body.Length;
        }

        return buffer;
    }
}
