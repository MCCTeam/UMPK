using System.Collections.Frozen;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Connection-owned tag facts. Configuration payloads remain pending until the existing before-play gate commits their native era policy.</summary>
internal sealed class ClientRegistryTags
{
    private enum UpdateProfile
    {
        LegacySnapshot,
        ResetStaticRegistries,
        ReportedRegistries,
    }

    private sealed record Snapshot(bool AllRegistriesKnown, FrozenSet<Identifier> KnownRegistries, FrozenDictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> Memberships);

    private static readonly Snapshot Unknown = new(false, FrozenSet<Identifier>.Empty, FrozenDictionary<(Identifier, Identifier), FrozenSet<int>>.Empty);
    private Snapshot _snapshot = Unknown;
    private Snapshot? _pendingConfiguration;
    private bool _configurationContentsReceived;

    private static UpdateProfile Profile(int protocol) => protocol < 766 ? UpdateProfile.LegacySnapshot : protocol < 768 ? UpdateProfile.ResetStaticRegistries : UpdateProfile.ReportedRegistries;
    private static bool IsRelevantStaticRegistry(Identifier registry) => registry == RegistryIds.Block || registry == RegistryIds.Item;

    public bool? Contains(Identifier registry, Identifier tag, int networkId)
    {
        Snapshot snapshot = _snapshot;
        return snapshot.AllRegistriesKnown || snapshot.KnownRegistries.Contains(registry)
            ? snapshot.Memberships.TryGetValue((registry, tag), out FrozenSet<int>? members) && members.Contains(networkId)
            : null;
    }

    // Retain the existing pre-766 full-snapshot contract. Modern packet callers select their source-backed phase policy below.
    public void Install(IReadOnlyList<TagRegistry> registries) => _snapshot = ReadUpdate(registries) with { AllRegistriesKnown = true };

    public void ApplyPlay(IReadOnlyList<TagRegistry> registries, int protocol)
    {
        UpdateProfile profile = Profile(protocol);
        if (profile == UpdateProfile.LegacySnapshot)
        {
            Install(registries);
            return;
        }
        Snapshot update = ReadUpdate(registries);
        Snapshot previous = _snapshot;
        if (profile == UpdateProfile.ResetStaticRegistries)
        {
            previous = ResetRelevantStaticRegistries();
            update = NonemptyRegistryPayloads(update, relevantStaticOnly: false);
        }
        _snapshot = Merge(previous, update);
    }

    public void BeginConfiguration()
    {
        _pendingConfiguration = null;
        _configurationContentsReceived = false;
    }

    public void StageConfiguration(IReadOnlyList<TagRegistry> registries, int protocol)
    {
        if (Profile(protocol) == UpdateProfile.LegacySnapshot)
        {
            Install(registries);
            return;
        }
        Snapshot update = ReadUpdate(registries);
        _pendingConfiguration = Merge(_pendingConfiguration ?? Unknown, update);
    }

    public void RecordConfigurationContents() => _configurationContentsReceived = true;

    public void CommitConfiguration(int protocol)
    {
        UpdateProfile profile = Profile(protocol);
        Snapshot previous = _configurationContentsReceived && profile != UpdateProfile.LegacySnapshot ? RelevantStaticFacts(_snapshot) : _snapshot;
        if (_pendingConfiguration is { } update)
        {
            if (profile == UpdateProfile.ResetStaticRegistries)
            {
                previous = ResetRelevantStaticRegistries();
                update = NonemptyRegistryPayloads(update, relevantStaticOnly: false);
            }
            else if (_configurationContentsReceived)
            {
                // The native contents-loading branch skips empty static-registry payloads. Unclassified rebuilt registry facts remain unknown; block and item registries retain their static identity.
                update = NonemptyRegistryPayloads(update, relevantStaticOnly: true);
            }
            previous = Merge(previous, update);
        }
        _snapshot = previous;
        BeginConfiguration();
    }

    public void Clear()
    {
        _snapshot = Unknown;
        BeginConfiguration();
    }

    private static Snapshot ReadUpdate(IReadOnlyList<TagRegistry> registries)
    {
        Dictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> memberships = [];
        HashSet<Identifier> seenRegistries = [];
        foreach (TagRegistry registry in registries)
        {
            if (!seenRegistries.Add(registry.Registry))
                throw new ProtocolViolationException("Duplicate registry in tag synchronization.");
            foreach (TagEntry tag in registry.Tags)
            {
                foreach (int member in tag.Ids)
                    if (member < 0)
                        throw new ProtocolViolationException("A synchronized tag contains a negative registry ID.");
                if (!memberships.TryAdd((registry.Registry, tag.TagId), tag.Ids.ToFrozenSet()))
                    throw new ProtocolViolationException("Duplicate tag in registry synchronization.");
            }
        }
        return new Snapshot(false, seenRegistries.ToFrozenSet(), memberships.ToFrozenDictionary());
    }

    private static Snapshot Merge(Snapshot previous, Snapshot update)
    {
        Dictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> memberships = [];
        foreach (var entry in previous.Memberships)
            if (!update.KnownRegistries.Contains(entry.Key.Registry))
                memberships.Add(entry.Key, entry.Value);
        foreach (var entry in update.Memberships)
            memberships.Add(entry.Key, entry.Value);
        HashSet<Identifier> known = new(previous.KnownRegistries);
        known.UnionWith(update.KnownRegistries);
        return new Snapshot(previous.AllRegistriesKnown, known.ToFrozenSet(), memberships.ToFrozenDictionary());
    }

    private static Snapshot RelevantStaticFacts(Snapshot previous)
    {
        HashSet<Identifier> known = [];
        Dictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> memberships = [];
        foreach (Identifier registry in previous.KnownRegistries)
            if (IsRelevantStaticRegistry(registry))
                known.Add(registry);
        if (previous.AllRegistriesKnown)
        {
            known.Add(RegistryIds.Block);
            known.Add(RegistryIds.Item);
        }
        foreach (var entry in previous.Memberships)
            if (IsRelevantStaticRegistry(entry.Key.Registry))
                memberships.Add(entry.Key, entry.Value);
        return new Snapshot(false, known.ToFrozenSet(), memberships.ToFrozenDictionary());
    }

    private static Snapshot ResetRelevantStaticRegistries()
    {
        // Original 766/767 TCP TagCollector resets built-ins before applying nonempty payloads. Only block/item reset facts have production consumers here; unclassified omitted registries become unknown.
        return new Snapshot(false, new[] { RegistryIds.Block, RegistryIds.Item }.ToFrozenSet(), FrozenDictionary<(Identifier, Identifier), FrozenSet<int>>.Empty);
    }

    private static Snapshot NonemptyRegistryPayloads(Snapshot update, bool relevantStaticOnly)
    {
        HashSet<Identifier> known = [];
        Dictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> memberships = [];
        foreach (var entry in update.Memberships)
            if (!relevantStaticOnly || IsRelevantStaticRegistry(entry.Key.Registry))
            {
                known.Add(entry.Key.Registry);
                memberships.Add(entry.Key, entry.Value);
            }
        return new Snapshot(false, known.ToFrozenSet(), memberships.ToFrozenDictionary());
    }
}
