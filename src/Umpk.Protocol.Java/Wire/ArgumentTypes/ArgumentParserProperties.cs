namespace Umpk.Protocol.Java.Packets;

/// <summary>The typed property payload of a command argument parser. Most vanilla parsers are singletons with an empty payload (<see cref="Empty"/>); a handful carry structured data (the brigadier numerics and string, entity, score_holder, time, and the resource*/resource_or_tag* family that names a registry). This hierarchy models exactly the payloads the serializers write, so a decoded tree re-encodes byte-identically. Unknown parsers are never decoded into this type: the codec rejects a parser id outside the version's argument-type table (its payload has no length prefix and cannot be skipped), so there is no opaque-bytes fallback.</summary>
/// <remarks>The structured payloads match the 1.21.5 numeric, string, entity, score-holder, time, and resource argument serializers.</remarks>
public abstract record ArgumentParserProperties
{
    private protected ArgumentParserProperties()
    {
    }

    /// <summary>The shared empty payload for every singleton parser (no property bytes on the wire).</summary>
    public static EmptyArgumentProperties Empty { get; } = new();
}

/// <summary>A parser with no property bytes.</summary>
public sealed record EmptyArgumentProperties : ArgumentParserProperties;

/// <summary>The brigadier string type (<c>brigadier:string</c>): a single VarInt enum (single-word=0, quotable-phrase=1, greedy-phrase=2).</summary>
public sealed record StringArgumentProperties(BrigadierStringKind Kind) : ArgumentParserProperties;

/// <summary>The Brigadier string parser mode: word, quotable phrase, or greedy phrase.</summary>
public enum BrigadierStringKind
{
    /// <summary>A single unquoted word.</summary>
    SingleWord = 0,

    /// <summary>A word or a double-quoted phrase.</summary>
    QuotablePhrase = 1,

    /// <summary>The greedy remainder of the input.</summary>
    GreedyPhrase = 2,
}

/// <summary>A brigadier integer range (<c>brigadier:integer</c>): a flags byte then the present bounds (bit 1 = has-min, bit 2 = has-max). Absence is encoded as <c>Int32.MinValue</c>/<c>Int32.MaxValue</c>.</summary>
public sealed record IntegerArgumentProperties(int Min, int Max) : ArgumentParserProperties;

/// <summary>A brigadier long range (<c>brigadier:long</c>): flags byte then present bounds.</summary>
public sealed record LongArgumentProperties(long Min, long Max) : ArgumentParserProperties;

/// <summary>A brigadier float range (<c>brigadier:float</c>): flags byte then present bounds.</summary>
public sealed record FloatArgumentProperties(float Min, float Max) : ArgumentParserProperties;

/// <summary>A brigadier double range (<c>brigadier:double</c>): flags byte then present bounds.</summary>
public sealed record DoubleArgumentProperties(double Min, double Max) : ArgumentParserProperties;

/// <summary>The <c>minecraft:entity</c> parser: a single flags byte (bit 1 = single, bit 2 = players-only).</summary>
public sealed record EntityArgumentProperties(bool SingleTarget, bool PlayersOnly) : ArgumentParserProperties;

/// <summary>The <c>minecraft:score_holder</c> parser: a single flags byte (bit 1 = allows-multiple).</summary>
public sealed record ScoreHolderArgumentProperties(bool AllowsMultiple) : ArgumentParserProperties;

/// <summary>The <c>minecraft:time</c> parser: a big-endian int minimum from 1.19.4.</summary>
public sealed record TimeArgumentProperties(int Min) : ArgumentParserProperties;

/// <summary>A registry-scoped parser (<c>minecraft:resource</c>, <c>resource_key</c>, <c>resource_or_tag</c>, <c>resource_or_tag_key</c>, <c>resource_selector</c>): a single resource-location naming the registry key.</summary>
public sealed record RegistryArgumentProperties(Identifier Registry) : ArgumentParserProperties;
