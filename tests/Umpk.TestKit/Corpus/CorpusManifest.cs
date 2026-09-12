using System.Text.Json.Serialization;

namespace Umpk.TestKit.Corpus;

/// <summary>The JSON sidecar manifest for a <c>.umpkcap</c> file. Records the provenance and summary statistics of a corpus recording: the version/protocol it was captured against, the scenario name, per-direction frame counts, timing bounds, and content hashes for tamper/round trip detection. The manifest is written next to the capture as <c>&lt;scenario&gt;.umpkcap.json</c>.</summary>
public sealed record CorpusManifest
{
    /// <summary>The on-disk format version of the capture this manifest describes.</summary>
    public int FormatVersion { get; init; } = UmpkCapFormat.Version;

    /// <summary>The Minecraft release name the corpus was recorded against (e.g. "1.21.5").</summary>
    public required string MinecraftVersion { get; init; }

    /// <summary>The wire protocol number (e.g. 770).</summary>
    public required int Protocol { get; init; }

    /// <summary>The scenario name (e.g. "login-config", "chunk-join").</summary>
    public required string Scenario { get; init; }

    /// <summary>Total frame count.</summary>
    public required int FrameCount { get; init; }

    /// <summary>Count of clientbound frames.</summary>
    public required int ClientboundCount { get; init; }

    /// <summary>Count of serverbound frames.</summary>
    public required int ServerboundCount { get; init; }

    /// <summary>UTC timestamp when recording began, ISO 8601.</summary>
    public required string RecordedAtUtc { get; init; }

    /// <summary>Wall-clock duration of the recording in milliseconds.</summary>
    public required long DurationMs { get; init; }

    /// <summary>SHA-256, lower-hex, over each frame's payload identity in order: direction, wire id, and body. Timing, sequence, and the phase label are deliberately EXCLUDED so two idempotent re-records of the same traffic produce the same value even when the recorder's phase context or timestamps differ. The loader verifies this hash on load. Caveat: because phase labels are outside the hash, phase-label corruption in a committed corpus is not caught by the integrity check even though the conformance latch/fallback machinery keys on phase labels.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Free-form note describing how the scenario was driven and any honesty caveats.</summary>
    public string? Notes { get; init; }
}

/// <summary>Source-generated JSON context for <see cref="CorpusManifest"/> (AOT-clean serialization).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CorpusManifest))]
public sealed partial class CorpusManifestJsonContext : JsonSerializerContext;
