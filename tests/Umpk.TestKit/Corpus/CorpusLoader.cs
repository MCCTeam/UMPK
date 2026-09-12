using System.Buffers.Binary;
using System.Text.Json;

namespace Umpk.TestKit.Corpus;

/// <summary>Reads <c>.umpkcap</c> captures (and their JSON manifests) back into <see cref="RecordedFrame"/> sequences. The conformance suite and the recorder self-tests consume this. Parsing is strict: a bad magic, truncated record, or over-long body raises <see cref="CorpusFormatException"/>.</summary>
public static class CorpusLoader
{
    /// <summary>Parses a capture from an in-memory blob, with an optional manifest blob.</summary>
    public static LoadedCorpus Parse(ReadOnlySpan<byte> capture, ReadOnlySpan<byte> manifest = default)
    {
        if (capture.Length < UmpkCapFormat.HeaderLength || !capture[..6].SequenceEqual(UmpkCapFormat.Magic))
            throw new CorpusFormatException("Not a .umpkcap capture (bad magic).");

        int formatVersion = capture[6];
        if (formatVersion != UmpkCapFormat.Version)
            throw new CorpusFormatException($"Unsupported .umpkcap format version {formatVersion}.");

        long recordedAtUnixMs = BinaryPrimitives.ReadInt64LittleEndian(capture[7..]);
        int protocol = BinaryPrimitives.ReadInt32LittleEndian(capture[15..]);
        int frameCount = BinaryPrimitives.ReadInt32LittleEndian(capture[19..]);
        if (frameCount < 0)
            throw new CorpusFormatException("Negative frame count.");

        var frames = new List<RecordedFrame>(frameCount);
        int offset = UmpkCapFormat.HeaderLength;
        for (int i = 0; i < frameCount; i++)
        {
            if (offset + UmpkCapFormat.RecordPrefixLength > capture.Length)
                throw new CorpusFormatException($"Truncated record {i}.");

            ReadOnlySpan<byte> prefix = capture[offset..];
            var direction = (CorpusDirection)prefix[0];
            var phase = (CorpusPhase)prefix[1];
            long sequence = BinaryPrimitives.ReadInt64LittleEndian(prefix[2..]);
            long timestampTicks = BinaryPrimitives.ReadInt64LittleEndian(prefix[10..]);
            int wireId = BinaryPrimitives.ReadInt32LittleEndian(prefix[18..]);
            int bodyLength = BinaryPrimitives.ReadInt32LittleEndian(prefix[22..]);
            offset += UmpkCapFormat.RecordPrefixLength;

            if (bodyLength < 0 || offset + bodyLength > capture.Length)
                throw new CorpusFormatException($"Record {i} body length {bodyLength} overruns the capture.");

            byte[] body = capture.Slice(offset, bodyLength).ToArray();
            offset += bodyLength;
            frames.Add(new RecordedFrame(sequence, direction, phase, wireId, body, timestampTicks));
        }

        CorpusManifest? manifestModel = null;
        if (!manifest.IsEmpty)
        {
            manifestModel = JsonSerializer.Deserialize(manifest, CorpusManifestJsonContext.Default.CorpusManifest)
                ?? throw new CorpusFormatException("Manifest deserialized to null.");
            VerifyManifest(manifestModel, protocol, frames);
        }

        return new LoadedCorpus(protocol, recordedAtUnixMs, frames, manifestModel);
    }

    /// <summary>Loads a capture file and its sidecar manifest (if it exists) from disk.</summary>
    public static async Task<LoadedCorpus> LoadFileAsync(string capturePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(capturePath);
        byte[] capture = await File.ReadAllBytesAsync(capturePath, ct).ConfigureAwait(false);
        string manifestPath = capturePath + ".json";
        byte[] manifest = File.Exists(manifestPath)
            ? await File.ReadAllBytesAsync(manifestPath, ct).ConfigureAwait(false)
            : [];
        return Parse(capture, manifest);
    }

    /// <summary>Cross-checks a loaded manifest against the capture it describes: the recomputed content hash must equal the manifest's, and the manifest's protocol and frame count must equal the capture header and body. The content hash covers direction, wire id, and body, but not phase labels or timing, so this check cannot detect phase-label corruption.</summary>
    private static void VerifyManifest(CorpusManifest manifest, int protocol, IReadOnlyList<RecordedFrame> frames)
    {
        if (manifest.Protocol != protocol)
            throw new CorpusIntegrityException(
                $"Manifest protocol {manifest.Protocol} does not match capture header protocol {protocol}.");

        if (manifest.FrameCount != frames.Count)
            throw new CorpusIntegrityException(
                $"Manifest frame count {manifest.FrameCount} does not match capture body frame count {frames.Count}.");

        string actual = UmpkCapWriter.ComputeContentHash(frames);
        if (!string.Equals(actual, manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new CorpusIntegrityException(
                $"Manifest content hash mismatch: manifest {manifest.ContentHash}, recomputed {actual}. " +
                "The capture bytes and manifest disagree (tamper or bad merge).");

    }

    /// <summary>Enumerates all <c>.umpkcap</c> files under a directory tree, sorted for determinism.</summary>
    public static IReadOnlyList<string> DiscoverCaptures(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!Directory.Exists(root))
            return [];

        string[] files = Directory.GetFiles(root, "*.umpkcap", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }
}

/// <summary>Raised when a <c>.umpkcap</c> capture or manifest is malformed.</summary>
public sealed class CorpusFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public CorpusFormatException(string message)
        : base(message)
    {
    }
}

/// <summary>Raised when a <c>.umpkcap</c> capture is well-formed but disagrees with its manifest: a content-hash mismatch, or a protocol/frame-count that does not match the capture. Distinct from <see cref="CorpusFormatException"/> (structural malformation) so tamper/bad-merge detection is catchable on its own.</summary>
public sealed class CorpusIntegrityException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public CorpusIntegrityException(string message)
        : base(message)
    {
    }
}
