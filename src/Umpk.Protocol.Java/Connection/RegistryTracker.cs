using System.Collections.Concurrent;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>The role-neutral owner of per-connection registry state. During the configuration phase both roles see <c>registry_data</c> packets carrying the server's (possibly datapack-customized) registries; the tracker collects them, keyed by registry id, so that at the config-to-play pause the session can install a <see cref="RegistryAccess"/> through <c>JavaConnection.SetCodecState</c> before the first play packet is decoded.</summary>
/// <remarks>Building a fully populated <see cref="RegistryAccess"/> from the packed NBT entries is the province of <c>Umpk.Client</c>/later phases; this stage collects the raw <see cref="PackedRegistryEntry"/> sets and hands back the well-formed empty registry access for <c>SetCodecState</c>, which is all the 47/770/776/777 reach-play flows require (the play codecs on 47/770/776/777 do not consult registry holders).</remarks>
public sealed class RegistryTracker
{
    private readonly ConcurrentDictionary<Identifier, IReadOnlyList<PackedRegistryEntry>> _registries = new();

    /// <summary>Records a <c>registry_data</c> packet's entries under its registry id.</summary>
    public void Accept(ClientboundConfigRegistryDataPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        _registries[packet.Registry] = packet.Entries;
    }

    /// <summary>The registry ids seen so far.</summary>
    public IReadOnlyCollection<Identifier> Registries => (IReadOnlyCollection<Identifier>)_registries.Keys;

    /// <summary>The number of registries collected.</summary>
    public int Count => _registries.Count;

    /// <summary>Returns the collected entries for a registry id, or <see langword="null"/> when unseen.</summary>
    public IReadOnlyList<PackedRegistryEntry>? EntriesFor(Identifier registry) =>
        _registries.TryGetValue(registry, out IReadOnlyList<PackedRegistryEntry>? entries) ? entries : null;

    /// <summary>Builds the <see cref="RegistryAccess"/> to install via <c>SetCodecState</c> at the phase pause. Currently the well-formed empty access (see the remarks on this type).</summary>
    public RegistryAccess BuildRegistryAccess() => EmptyRegistries.Access;
}
