using System.Globalization;
using System.Text;
using Umpk.Data.Java;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The packet-shaped view of the registration timelines. For every packet it renders the runs of consecutive supported protocols that resolve one codec, which is the transpose of the codec-identity pins: those answer "what does protocol P bind" one protocol at a time, this answers "which protocols does this codec govern" and "what did this release change" in one line each.</summary>
/// <remarks>A run breaks whenever the resolved codec identity changes, whenever the identifier that resolved the binding changes, and wherever the packet is not registered at all, so a gap in a line's ranges is an absence rather than a continuation. Only supported protocols are counted, so every number printed is a protocol that exists.</remarks>
internal static class ProtocolTimeline
{
    internal const string BeginMarker = "BEGIN GENERATED BANDS";
    internal const string EndMarker = "END GENERATED BANDS";

    /// <summary>Column widths are constants rather than measured from the data. A measured width reflows every line of the file the day one identifier grows, and a pin whose value is a small reviewable diff must not have that failure mode. An identifier wider than its column pushes only its own bands.</summary>
    private const int PhaseWidth = 13;
    private const int FlowWidth = 11;
    private const int IdentifierWidth = 48;

    private static readonly string[] HeaderLines =
    [
        "# Timeline bands over the supported protocols, one line per (phase, flow, identifier).",
        "#",
        "# A band is a maximal run of consecutive SUPPORTED protocols resolving the same codec identity",
        "# and the same resolving identifier. A run reaching the highest supported protocol is written",
        "# open (\"775-\"), so adding a protocol that changes no wire is a zero-line diff. A protocol the",
        "# packet is not registered on breaks the run, so a gap between two ranges is an absence.",
        "#",
        "# \"marker\" is a packet registered with no codec. \"via:<id>\" is the identifier the version dataset",
        "# spelled the packet with, printed only where a curated alias resolved the binding.",
        "#",
        "# Generated from the built descriptors, and a transpose of fixtures/codec-identity. Regenerate",
        "# with UMPK_UPDATE_TIMELINE_BAND_PINS=1; never edit by hand.",
    ];

    /// <summary>The supported protocols in ascending order, which is the axis every band is a run over.</summary>
    internal static IReadOnlyList<int> Protocols { get; } =
        [.. JavaVersions.All.Select(static v => v.Version.Protocol).Distinct().Order()];

    /// <summary>A packet, identified the way the registration layer identifies one.</summary>
    internal readonly record struct PacketKey(ProtocolPhase Phase, PacketFlow Flow, string Identifier)
    {
        public override string ToString() => $"{Phase} {Flow} {Identifier}";
    }

    /// <summary>One run of protocols, and what they all resolve to.</summary>
    internal readonly record struct Band(int First, int Last, string Value);

    /// <summary>Renders the frozen text form of the whole band table.</summary>
    internal static string Render()
    {
        var sb = new StringBuilder();
        foreach (string header in HeaderLines)
            sb.Append(header).Append('\n');

        sb.Append('\n');
        foreach ((PacketKey key, IReadOnlyList<Band> bands) in Build())
        {
            sb.Append(key.Phase.ToString().PadRight(PhaseWidth)).Append(' ');
            sb.Append(key.Flow.ToString().PadRight(FlowWidth)).Append(' ');
            sb.Append(key.Identifier.PadRight(IdentifierWidth)).Append(' ');
            sb.Append(string.Join(" | ", bands.Select(static b => $"{RangeOf(b)} {b.Value}"))).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Every packet's bands, ordered the way the file orders them.</summary>
    internal static IReadOnlyList<(PacketKey Key, IReadOnlyList<Band> Bands)> Build()
    {
        Dictionary<PacketKey, Dictionary<int, string>> resolved = [];
        foreach (int protocol in Protocols)
            foreach ((PacketKey key, string value) in Resolve(protocol))
            {
                if (!resolved.TryGetValue(key, out Dictionary<int, string>? byProtocol))
                {
                    byProtocol = [];
                    resolved[key] = byProtocol;
                }

                byProtocol[protocol] = value;
            }

        return
        [
            .. resolved
                .OrderBy(static e => e.Key.Phase)
                .ThenBy(static e => e.Key.Flow)
                .ThenBy(static e => e.Key.Identifier, StringComparer.Ordinal)
                .Select(static e => (e.Key, BandsOf(e.Value))),
        ];
    }

    /// <summary>What one protocol resolves for every packet it registers.</summary>
    internal static IEnumerable<(PacketKey Key, string Value)> Resolve(int protocol)
    {
        if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
            throw new InvalidOperationException($"Protocol {protocol} is not in the version catalog.");

        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                {
                    if (!registry.TryGetInbound(wireId, out BoundPacketCodec entry))
                        continue;

                    string via = entry.DatasetIdentifier == entry.Type.Id
                        ? string.Empty
                        : $" via:{entry.DatasetIdentifier}";
                    yield return (new PacketKey(phase, flow, entry.Type.Id.ToString()), entry.CodecIdentity + via);
                }
            }

    }

    /// <summary>The protocols one band covers, which is what a reader of a range is entitled to assume.</summary>
    internal static IEnumerable<int> Covered(Band band) =>
        Protocols.Where(p => p >= band.First && p <= band.Last);

    /// <summary>Parses a rendered band table back into bands, so the file itself can be checked.</summary>
    internal static IReadOnlyList<(PacketKey Key, IReadOnlyList<Band> Bands)> Parse(string text)
    {
        List<(PacketKey, IReadOnlyList<Band>)> parsed = [];
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0 || line[0] == '#')
                continue;

            int cursor = 0;
            string phase = NextField(line, ref cursor);
            string flow = NextField(line, ref cursor);
            string identifier = NextField(line, ref cursor);
            string rest = line[cursor..].TrimStart();
            if (rest.Length == 0)
                throw new FormatException($"Malformed band line: '{line}'.");

            var key = new PacketKey(Enum.Parse<ProtocolPhase>(phase), Enum.Parse<PacketFlow>(flow), identifier);
            parsed.Add((key, [.. rest.Split(" | ", StringSplitOptions.None).Select(ParseBand)]));
        }

        return parsed;
    }

    /// <summary>Renders one band's protocol range.</summary>
    internal static string RangeOf(Band band) =>
        band.Last == Protocols[^1] ? $"{band.First}-"
        : band.First == band.Last ? $"{band.First}"
        : $"{band.First}-{band.Last}";

    /// <summary>Reads one space-delimited field, leaving the cursor just past it.</summary>
    private static string NextField(string line, ref int cursor)
    {
        while (cursor < line.Length && line[cursor] == ' ')
            cursor++;

        int start = cursor;
        while (cursor < line.Length && line[cursor] != ' ')
            cursor++;

        if (start == cursor)
            throw new FormatException($"Malformed band line: '{line}'.");

        return line[start..cursor];
    }

    private static Band ParseBand(string text)
    {
        int split = text.IndexOf(' ', StringComparison.Ordinal);
        if (split < 0)
            throw new FormatException($"Band '{text}' carries a range and no value.");

        string range = text[..split];
        string value = text[(split + 1)..].Trim();
        int dash = range.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            int only = int.Parse(range, CultureInfo.InvariantCulture);
            return new Band(only, only, value);
        }

        int first = int.Parse(range[..dash], CultureInfo.InvariantCulture);
        string tail = range[(dash + 1)..];
        return new Band(first, tail.Length == 0 ? Protocols[^1] : int.Parse(tail, CultureInfo.InvariantCulture), value);
    }

    private static IReadOnlyList<Band> BandsOf(Dictionary<int, string> byProtocol)
    {
        List<Band> bands = [];
        int previousIndex = -2;
        for (int i = 0; i < Protocols.Count; i++)
        {
            if (!byProtocol.TryGetValue(Protocols[i], out string? value))
                continue;

            if (bands.Count > 0 && previousIndex == i - 1 && bands[^1].Value == value)
                bands[^1] = bands[^1] with { Last = Protocols[i] };

            else
                bands.Add(new Band(Protocols[i], Protocols[i], value));

            previousIndex = i;
        }

        return bands;
    }
}
