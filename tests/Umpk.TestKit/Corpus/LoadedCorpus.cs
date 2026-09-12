namespace Umpk.TestKit.Corpus;

/// <summary>A parsed <c>.umpkcap</c> capture plus its manifest (when present).</summary>
public sealed class LoadedCorpus
{
    internal LoadedCorpus(int protocol, long recordedAtUnixMs, IReadOnlyList<RecordedFrame> frames, CorpusManifest? manifest)
    {
        Protocol = protocol;
        RecordedAtUnixMs = recordedAtUnixMs;
        Frames = frames;
        Manifest = manifest;
    }

    /// <summary>The wire protocol number from the capture header.</summary>
    public int Protocol { get; }

    /// <summary>The header's recorded-at timestamp (informational).</summary>
    public long RecordedAtUnixMs { get; }

    /// <summary>The frames, in recorded order.</summary>
    public IReadOnlyList<RecordedFrame> Frames { get; }

    /// <summary>The manifest sidecar if it was loaded alongside the capture, otherwise null.</summary>
    public CorpusManifest? Manifest { get; }
}
