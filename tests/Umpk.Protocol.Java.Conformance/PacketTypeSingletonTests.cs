using Umpk.Data.Java;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>A packet identity is one object for the life of the process. The outbound table is a reference-identity <see cref="Dictionary{TKey, TValue}"/> keyed on <see cref="PacketType"/> (<c>PhaseRegistry</c>), so a second instance carrying the same identity would be a key that never matches: the packet would encode fine in a test holding the first instance and then fail to resolve a wire id on a live send.</summary>
/// <remarks>
/// <para>The identity is <c>(phase, flow, identifier, payload type)</c>, not <c>(phase, flow, identifier)</c>. Seven identifiers are deliberately split across eras into two records apiece, so the identifier alone is not unique across the 49 descriptors; within any one descriptor only one of the pair is ever registered, and <c>PhaseRegistry</c> refuses a second entry at the same identity anyway.</para>
/// <para>Markers are excluded because they are per-registration by construction: <c>PacketRegistrar</c> builds a fresh <see cref="MarkerPacketType"/> for each unbound packet in each descriptor, and no caller can hold one to look up with.</para>
/// </remarks>
public sealed class PacketTypeSingletonTests
{
    /// <summary>The identifiers carried by two records apiece, spelled out so an eighth is a deliberate edit here rather than a silent widening of what "one identity" means.</summary>
    private static readonly string[] EraSplitIdentities =
    [
        "Configuration/Clientbound minecraft:registry_data => ClientboundConfigRegistryBlobPacket | ClientboundConfigRegistryDataPacket",
        "Play/Clientbound minecraft:set_score => ClientboundLegacySetScorePacket | ClientboundSetScorePacket",
        "Play/Serverbound minecraft:chat => ServerboundLegacyChatPacket | ServerboundSignedChatPacket",
        "Play/Serverbound minecraft:chat_command => ServerboundChatCommandPacket | ServerboundSignedChatCommandPacket",
        "Play/Serverbound minecraft:edit_book => ServerboundEditBookPacket | ServerboundLegacyEditBookPacket",
        "Play/Serverbound minecraft:place_recipe => ServerboundPlaceRecipeByNamePacket | ServerboundPlaceRecipePacket",
        "Play/Serverbound minecraft:recipe_book_seen_recipe => ServerboundRecipeBookSeenRecipeByNamePacket | ServerboundRecipeBookSeenRecipePacket",
    ];

    [Fact]
    public void NoTwoInstances_ShareAPacketIdentity()
    {
        Dictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id, Type Payload), PacketType> first = [];
        List<string> offenders = [];
        int reused = 0;

        foreach (PacketType type in ImplementedTypes())
        {
            (ProtocolPhase, PacketFlow, Identifier, Type) key = (type.Phase, type.Flow, type.Id, type.PayloadType);
            if (first.TryAdd(key, type))
                continue;

            if (ReferenceEquals(first[key], type))
                reused++;

            else
                offenders.Add($"{type.Phase}/{type.Flow} {type.Id} ({type.PayloadType.Name})");

        }

        Assert.True(offenders.Count == 0, $"A packet identity has more than one instance: {string.Join(", ", offenders.Take(20))}.");

        // Without a reuse count this passes on a walk that visited nothing. A floor rather than an exact number, because every binding added raises it.
        Assert.True(reused > 1000, $"Only {reused} identities were reused across descriptors; the walk found {first.Count} distinct.");
    }

    [Fact]
    public void OnlyTheDeclaredIdentifiers_CarryTwoRecords()
    {
        Dictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), SortedSet<string>> records = [];
        foreach (PacketType type in ImplementedTypes())
        {
            (ProtocolPhase, PacketFlow, Identifier) key = (type.Phase, type.Flow, type.Id);
            if (!records.TryGetValue(key, out SortedSet<string>? names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                records[key] = names;
            }

            names.Add(type.PayloadType.Name);
        }

        string[] split =
        [
            .. records.Where(static entry => entry.Value.Count > 1)
                .Select(static entry => $"{entry.Key.Phase}/{entry.Key.Flow} {entry.Key.Id} => {string.Join(" | ", entry.Value)}")
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(EraSplitIdentities, split);
    }

    private static IEnumerable<PacketType> ImplementedTypes()
    {
        foreach (JavaVersion version in JavaVersions.All)
            foreach (ProtocolPhase phase in Enum.GetValues<ProtocolPhase>())
                foreach (PacketFlow flow in Enum.GetValues<PacketFlow>())
                {
                    if (!version.Protocol.TryGetRegistry(phase, flow, out PhaseRegistry registry))
                        continue;

                    foreach ((int _, PacketType type) in registry.Packets)
                        if (type is not MarkerPacketType)
                            yield return type;

                }

    }
}
