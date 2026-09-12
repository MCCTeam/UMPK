using System.Text.Json;
using Umpk.TestKit.Corpus;
using Xunit;

namespace Umpk.PacketRecorder.Tests;

/// <summary>Round-trip and integrity tests for the.umpkcap serializer/loader.</summary>
public sealed class UmpkCapFormatTests
{
    private static RecordedFrame Frame(long seq, CorpusDirection dir, CorpusPhase phase, int wireId, byte[] body) =>
        new(seq, dir, phase, wireId, body, timestampTicks: seq * 100);

    private static IReadOnlyList<RecordedFrame> SampleFrames() =>
    [
        Frame(0, CorpusDirection.Clientbound, CorpusPhase.Login, 0x02, [1, 2, 3]),
        Frame(1, CorpusDirection.Serverbound, CorpusPhase.Configuration, 0x03, []),
        Frame(2, CorpusDirection.Clientbound, CorpusPhase.Play, 0x27, [0xAA, 0xBB, 0xCC, 0xDD]),
    ];

    [Fact]
    public void Serialize_Then_Load_RoundTripsAllFields()
    {
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] capture, byte[] manifest, CorpusManifest model) = UmpkCapWriter.Serialize(
            frames, "1.21.5", 770, "unit", DateTimeOffset.UnixEpoch, durationMs: 42);

        LoadedCorpus loaded = CorpusLoader.Parse(capture, manifest);

        Assert.Equal(770, loaded.Protocol);
        Assert.Equal(frames.Count, loaded.Frames.Count);
        Assert.NotNull(loaded.Manifest);
        Assert.Equal(model.ContentHash, loaded.Manifest!.ContentHash);
        Assert.Equal(2, loaded.Manifest.ClientboundCount);
        Assert.Equal(1, loaded.Manifest.ServerboundCount);

        for (int i = 0; i < frames.Count; i++)
        {
            RecordedFrame a = frames[i];
            RecordedFrame b = loaded.Frames[i];
            Assert.Equal(a.Sequence, b.Sequence);
            Assert.Equal(a.Direction, b.Direction);
            Assert.Equal(a.Phase, b.Phase);
            Assert.Equal(a.WireId, b.WireId);
            Assert.Equal(a.TimestampTicks, b.TimestampTicks);
            Assert.Equal(a.Body, b.Body);
        }
    }

    [Fact]
    public void ContentHash_IsStable_AcrossHeaderTimestamp()
    {
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (_, _, CorpusManifest a) = UmpkCapWriter.Serialize(frames, "1.21.5", 770, "unit", DateTimeOffset.UnixEpoch, 1);
        (_, _, CorpusManifest b) = UmpkCapWriter.Serialize(frames, "1.21.5", 770, "unit", DateTimeOffset.UtcNow, 9999);

        // Different header timestamps and durations, same frames: the content hash is identical.
        Assert.Equal(a.ContentHash, b.ContentHash);
    }

    [Fact]
    public void Serialize_IsByteDeterministic_ForFixedTimestamp()
    {
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] first, _, _) = UmpkCapWriter.Serialize(frames, "26.2", 776, "unit", DateTimeOffset.UnixEpoch, 5);
        (byte[] second, _, _) = UmpkCapWriter.Serialize(frames, "26.2", 776, "unit", DateTimeOffset.UnixEpoch, 5);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Parse_RejectsBadMagic()
    {
        byte[] garbage = new byte[64];
        Assert.Throws<CorpusFormatException>(() => CorpusLoader.Parse(garbage));
    }

    [Fact]
    public void Parse_RejectsTruncatedBody()
    {
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] capture, _, _) = UmpkCapWriter.Serialize(frames, "1.8", 47, "unit", DateTimeOffset.UnixEpoch, 1);
        byte[] truncated = capture[..^2];
        Assert.Throws<CorpusFormatException>(() => CorpusLoader.Parse(truncated));
    }

    [Fact]
    public void Parse_ValidCaptureAndManifest_PassesIntegrityCheck()
    {
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] capture, byte[] manifest, _) =
            UmpkCapWriter.Serialize(frames, "1.21.5", 770, "unit", DateTimeOffset.UnixEpoch, 1);

        // A faithful capture/manifest pair verifies cleanly (: the loader now checks the hash).
        LoadedCorpus loaded = CorpusLoader.Parse(capture, manifest);
        Assert.Equal(frames.Count, loaded.Frames.Count);
    }

    [Fact]
    public async Task LoadFile_TamperedCaptureBody_ThrowsIntegrityException()
    {
        // a corpus whose bytes were tampered after the manifest was written must be rejected on load. The tampered file is built in this test's own temp dir, never in fixtures/.
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] capture, byte[] manifest, _) =
            UmpkCapWriter.Serialize(frames, "1.21.5", 770, "unit", DateTimeOffset.UnixEpoch, 1);

        // Flip one byte inside the first frame's body (after the header + first record prefix). This keeps the file structurally valid but changes the content hash.
        byte[] tampered = (byte[])capture.Clone();
        int firstBodyOffset = UmpkCapFormatProbe.HeaderLength + UmpkCapFormatProbe.RecordPrefixLength;
        tampered[firstBodyOffset] ^= 0xFF;

        string dir = Path.Combine(Path.GetTempPath(), "umpk-tamper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string capturePath = Path.Combine(dir, "tampered.umpkcap");
            await File.WriteAllBytesAsync(capturePath, tampered);
            await File.WriteAllBytesAsync(capturePath + ".json", manifest);

            CorpusIntegrityException ex = await Assert.ThrowsAsync<CorpusIntegrityException>(
                () => CorpusLoader.LoadFileAsync(capturePath));
            Assert.Contains("content hash", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Parse_ManifestProtocolMismatch_ThrowsIntegrityException()
    {
        // a manifest whose protocol disagrees with the capture header is a bad merge; reject it.
        IReadOnlyList<RecordedFrame> frames = SampleFrames();
        (byte[] capture, _, CorpusManifest model) =
            UmpkCapWriter.Serialize(frames, "1.21.5", 770, "unit", DateTimeOffset.UnixEpoch, 1);

        byte[] wrongProtocol = JsonSerializer.SerializeToUtf8Bytes(
            model with { Protocol = 999 }, CorpusManifestJsonContext.Default.CorpusManifest);

        CorpusIntegrityException ex = Assert.Throws<CorpusIntegrityException>(
            () => CorpusLoader.Parse(capture, wrongProtocol));
        Assert.Contains("protocol", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Mirrors the internal UmpkCapFormat header/prefix lengths so the tamper test can target a body byte without depending on internals visibility. Kept in lockstep by the round-trip tests above.
    private static class UmpkCapFormatProbe
    {
        public const int HeaderLength = 6 + 1 + 8 + 4 + 4;
        public const int RecordPrefixLength = 1 + 1 + 8 + 8 + 4 + 4;
    }
}
