using System.Text;
using System.Text.RegularExpressions;
using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>A codec whose name carries a version suffix must carry the version its band actually starts at.</summary>
/// <remarks>
/// <para>The suffix is the only era signal a reader gets while editing a codec body, and it was wrong on 96 of the 489 members that carried one: the worst named 1.21.5 while answering for all 48 protocols from 1.9 up, so an edit meant for the newest wire form reached every one of them. The convention is only worth having if it cannot drift back, which is what this pins.</para>
/// <para>The band start is computed from the built descriptors, so the expectation (the name) and the fact (the lowest protocol the name is bound at) come from different places. The suffix is resolved through <see cref="JavaVersions.TryGetByName"/> rather than through <c>JavaProtocols</c>, so a protocol with several release names accepts any of them, and the two vocabularies are compared numerically once per codec rather than by spelling.</para>
/// </remarks>
public sealed class CodecNameMatchesTimelineStartTests
{
    /// <summary>A <c>Class.Member</c> reference inside a bind-site source expression.</summary>
    private static readonly Regex MemberReference =
        new(@"(?<![A-Za-z0-9_.])[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary>A trailing version suffix: <c>SwingV1_9</c>, <c>ChunkCodecs.V1_20_2</c>, <c>FinishedV26_2</c>.</summary>
    private static readonly Regex EraSuffix =
        new(@"^(?<stem>.*?)V(?<version>[0-9]+(?:_[0-9]+)*)$", RegexOptions.Compiled);

    [Fact]
    public void WireLayoutSuffixedCodecs_NameTheProtocolTheirBandStartsAt()
    {
        var bandStart = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JavaVersion version in JavaVersions.All.OrderBy(static v => v.Version.Protocol))
            foreach (BoundPacketCodec entry in Implemented(version.Protocol))
                foreach (Match reference in MemberReference.Matches(entry.CodecIdentity))
                    if (!bandStart.ContainsKey(reference.Value))
                        bandStart[reference.Value] = version.Version.Protocol;

        var wrong = new StringBuilder();
        int suffixed = 0;
        foreach ((string member, int protocol) in bandStart.OrderBy(static e => e.Key, StringComparer.Ordinal))
        {
            Match era = EraSuffix.Match(member[(member.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            if (!era.Success)
                continue;

            suffixed++;
            string name = era.Groups["version"].Value.Replace('_', '.');
            if (JavaVersions.TryGetByName(name, out JavaVersion named) && named.Version.Protocol == protocol)
                continue;

            string says = JavaVersions.TryGetByName(name, out JavaVersion claimed)
                ? claimed.Version.Protocol.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "no such version";
            wrong.Append('\n').Append(member).Append(" says ").Append(says).Append(", bound from ").Append(protocol);
        }

        Assert.True(suffixed > 400, $"only {suffixed} codec members carry a version suffix; the walk found nothing to check.");
        Assert.True(wrong.Length == 0, $"{wrong.ToString().Split('\n').Length - 1} of {suffixed} era-suffixed codecs name a protocol they are not bound at:{wrong}");
    }

    private static IEnumerable<BoundPacketCodec> Implemented(ProtocolDescriptor descriptor)
    {
        foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
            foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
            {
                if (!descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                    continue;

                foreach ((int wireId, PacketType _) in registry.Packets)
                    if (registry.TryGetInbound(wireId, out BoundPacketCodec entry) && entry.IsImplemented)
                        yield return entry;

            }

    }
}
