using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Game.Dialogs;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies UI state: scoreboard objectives/scores/teams/display slots, player/tab list, boss bars, maps, advancements, sign editor, and resource-pack policy responses.</summary>
internal sealed class UiApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        switch (packet)
        {
            case ClientboundSetObjectivePacket objective:
                ApplyObjective(objective, context);
                await context.PublishAsync(new ScoreboardChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetScorePacket score:
                context.State.Scoreboard.SetScore(score.ObjectiveName, score.Owner, score.Value);
                await context.PublishAsync(new ScoreboardChanged()).ConfigureAwait(false);
                return true;

            // 47-404 carry the score under the pre-1.14 record: one packet for both set and remove, with an empty objective name meaning "every objective". Without this arm the packet decodes and is then dropped, which leaves the scoreboard empty on 24 protocols in exactly the way the marker did.
            case ClientboundLegacySetScorePacket legacyScore:
                ApplyLegacyScore(legacyScore, context);
                await context.PublishAsync(new ScoreboardChanged()).ConfigureAwait(false);
                return true;

            case ClientboundResetScorePacket reset:
                if (reset.ObjectiveName is { } obj)
                    context.State.Scoreboard.RemoveScore(obj, reset.Owner);

                else
                    context.State.Scoreboard.ResetScores(reset.Owner);

                await context.PublishAsync(new ScoreboardChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetDisplayObjectivePacket display:
                context.State.Scoreboard.SetDisplay((DisplaySlot)display.Slot, string.IsNullOrEmpty(display.ObjectiveName) ? null : display.ObjectiveName);
                await context.PublishAsync(new ScoreboardChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetPlayerTeamPacket team:
                ApplyTeam(team, context);
                await context.PublishAsync(new TeamChanged(team.Name)).ConfigureAwait(false);
                return true;

            case ClientboundPlayerInfoUpdatePacket info:
                ApplyPlayerInfo(info, context);
                return true;

            case ClientboundPlayerInfoRemovePacket remove:
                foreach (Guid id in remove.ProfileIds)
                    context.State.TabList.Remove(id);

                return true;

            case ClientboundLegacyPlayerListItemPacket legacyInfo:
                ApplyLegacyPlayerInfo(legacyInfo, context);
                return true;

            case ClientboundTabListPacket tab:
                context.State.TabList.Header = tab.Header;
                context.State.TabList.Footer = tab.Footer;
                await context.PublishAsync(new TabListHeaderFooterChanged()).ConfigureAwait(false);
                return true;

            case ClientboundBossEventPacket boss:
                await ApplyBossAsync(boss, context).ConfigureAwait(false);
                return true;

            case ClientboundOpenSignEditorPacket sign:
                await context.PublishAsync(new SignEditorOpened(sign.Pos, sign.IsFrontText)).ConfigureAwait(false);
                return true;

            case ClientboundLegacyResourcePackPacket legacyPack:
                await RespondLegacyResourcePackAsync(legacyPack, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundResourcePackPushPacket push:
                await RespondResourcePackAsync(push.Id, push.Url, push.Hash, push.Required, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundConfigResourcePackPushPacket configPush:
                await RespondConfigResourcePackAsync(
                    configPush.Id, configPush.Url, configPush.Hash, configPush.Required, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundMapItemDataPacket map:
                ApplyMapData(map, context);
                await context.PublishAsync(new MapDataReceived(map.MapId)).ConfigureAwait(false);
                return true;

            case ClientboundShowDialogPacket show:
                ApplyShowDialog(show.InlineDialog, show.RegistryId, context);
                await context.PublishAsync(new DialogShown(show.RegistryId)).ConfigureAwait(false);
                return true;

            // The configuration-phase dialog packets land in the same DialogState and publish the same events as the play-phase ones, so a consumer sees one dialog surface regardless of phase. The configuration wire is always an inline body (its stream codec is the context-free one, with no holder id), so there is never a registry id to carry.
            case ClientboundConfigShowDialogPacket configShow:
                ApplyShowDialog(configShow.Dialog, registryId: null, context);
                await context.PublishAsync(new DialogShown(null)).ConfigureAwait(false);
                return true;

            case ClientboundClearDialogPacket:
            case ClientboundConfigClearDialogPacket:
                context.State.Dialogs.Clear();
                await context.PublishAsync(new DialogCleared()).ConfigureAwait(false);
                return true;

            case ClientboundUpdateAdvancementsPacket advancements:
                ApplyAdvancements(advancements, context);
                await context.PublishAsync(new AdvancementsChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSelectAdvancementsTabPacket selectTab:
                context.State.Advancements.SelectedTab = selectTab.Tab;
                await context.PublishAsync(new AdvancementsChanged()).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Applies an update_advancements payload into <see cref="AdvancementState"/>: a reset clears the whole tree first (the server sends reset=true on the join batch), then removals, then the added definitions, then the progress map. Criterion names come from the wire list on the eras that carry it (1.12-1.20.1) and are recovered from the requirement groups on the eras that dropped it at 1.20.2, so consumers see the same criterion set on every version. The obtained instant is epoch millis; an unobtained criterion stays null.</summary>
    private static void ApplyAdvancements(ClientboundUpdateAdvancementsPacket packet, ApplierContext context)
    {
        AdvancementState state = context.State.Advancements;
        if (packet.Reset)
            state.Clear();

        state.ShowAdvancements = packet.ShowAdvancements;

        foreach (Identifier removed in packet.Removed)
            state.RemoveAdvancement(removed);

        foreach (AdvancementEntry entry in packet.Added)
        {
            AdvancementNode node = entry.Value;
            state.PutAdvancement(new Advancement(
                entry.Id,
                node.Parent,
                ToDisplay(node.Display),
                CriterionNames(node),
                node.Requirements));
        }

        foreach (AdvancementProgressEntry progress in packet.Progress)
            foreach (CriterionProgressEntry criterion in progress.Criteria)
            {
                DateTimeOffset? obtained = criterion.ObtainedEpochMillis is { } ms
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    : null;
                state.SetCriterionProgress(progress.Id, criterion.CriterionId, obtained);
            }

    }

    // 1.20.2 dropped the criterion-name list from the wire; every criterion still appears in the requirement groups (vanilla builds the requirements from the criteria key set), so the names are recovered from there in first-seen order rather than left empty on the modern band.
    private static IReadOnlyList<string> CriterionNames(AdvancementNode node)
    {
        if (node.Criteria.Count > 0)
            return node.Criteria;

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> group in node.Requirements)
            foreach (string name in group)
                if (seen.Add(name))
                    names.Add(name);

        return names;
    }

    // The game-side display record keeps the icon opaque (the item model is the item module's), so the protocol ItemStack rides through as the object payload.
    private static AdvancementDisplay? ToDisplay(AdvancementDisplayInfo? display) =>
        display is null
            ? null
            : new AdvancementDisplay(
                display.Title,
                display.Description,
                (int)display.Frame,
                display.ShowToast,
                display.Hidden,
                display.Icon);

    // An inline body is parsed; a registry reference only carries an index and the dialog registry is not modelled, so the id is recorded and the body left null rather than inventing one. Either way the state reports a dialog as showing, which is what a consumer gates its UI on.
    //
    // Shared by the play-phase and configuration-phase show_dialog packets: the two wire forms differ (the play one is a holder, the configuration one a bare body) but the state they produce must not.
    private static void ApplyShowDialog(Umpk.Nbt.NbtTag? inlineBody, int? registryId, ApplierContext context)
    {
        Dialog? dialog = inlineBody is { } body ? DialogNbt.Read(body) : null;
        if (dialog is null && registryId is null)
            context.Logger.LogWarning("A show_dialog packet carried neither a readable inline dialog nor a registry id.");

        context.State.Dialogs.Show(dialog, registryId);
    }

    /// <summary>Writes a decoded <c>map_item_data</c> packet into the stored <see cref="MapData"/>: scale, lock state, decorations, and the rectangular pixel patch. Before this the packet was decoded and dropped, so every consumer saw a map that existed but had no pixels.</summary>
    /// <remarks>Icons are replaced wholesale, and only when the packet carries a decoration list: from 1.9 the list is optional and its absence means "unchanged", whereas 1.8 always sends it. The pixel patch is applied only when it is non-degenerate AND lands inside the 128x128 grid; a patch whose declared rectangle runs off the map or disagrees with its own colour-array length is a malformed frame, and dropping it here keeps the applier from throwing on hostile input.</remarks>
    private static void ApplyMapData(ClientboundMapItemDataPacket packet, ApplierContext context)
    {
        MapData map = context.State.Maps.GetOrCreate(packet.MapId);
        map.Scale = packet.Scale;
        map.Locked = packet.Locked;

        if (packet.Icons is { } icons)
            map.SetIcons(icons);

        MapPatch patch = packet.Patch;
        if (patch.Columns == 0 || patch.Rows == 0 || patch.Colors is null)
            return;

        if (patch.Colors.Length != patch.Columns * patch.Rows
            || patch.StartX + patch.Columns > MapData.Size
            || patch.StartY + patch.Rows > MapData.Size)
        {
            context.Logger.LogWarning(
                "Dropping malformed map pixel patch for map {MapId}: {Columns}x{Rows} at ({StartX},{StartY}) with {ColorCount} colours.",
                packet.MapId,
                patch.Columns,
                patch.Rows,
                patch.StartX,
                patch.StartY,
                patch.Colors.Length);
            return;
        }

        map.UpdateRegion(patch.StartX, patch.StartY, patch.Columns, patch.Rows, patch.Colors);
    }

    private static void ApplyObjective(ClientboundSetObjectivePacket packet, ApplierContext context)
    {
        Scoreboard board = context.State.Scoreboard;
        switch (packet.Mode)
        {
            case ScoreboardObjectiveMode.Add:
            case ScoreboardObjectiveMode.Change:
                var objective = new Objective(
                    packet.ObjectiveName,
                    packet.DisplayName ?? Umpk.Text.Component.Text(packet.ObjectiveName),
                    packet.RenderType);
                board.PutObjective(objective);
                break;
            case ScoreboardObjectiveMode.Remove:
                board.RemoveObjective(packet.ObjectiveName);
                break;
        }
    }

    private static void ApplyTeam(ClientboundSetPlayerTeamPacket packet, ApplierContext context)
    {
        Scoreboard board = context.State.Scoreboard;
        switch (packet.Method)
        {
            case TeamMethod.Add:
            case TeamMethod.Change:
                Team team = board.TryGetTeam(packet.Name, out Team? existing) && existing is not null
                    ? existing
                    : new Team(packet.Name);
                if (packet.Parameters is { } p)
                {
                    team.DisplayName = p.DisplayName;
                    team.Prefix = p.Prefix;
                    team.Suffix = p.Suffix;
                    team.NameTagVisibility = p.NameTagVisibility;
                    team.CollisionRule = p.CollisionRule;
                    team.Color = p.Color ?? -1;
                }

                foreach (string member in packet.Players)
                    team.AddMember(member);

                board.PutTeam(team);
                break;
            case TeamMethod.Remove:
                board.RemoveTeam(packet.Name);
                break;
            case TeamMethod.AddPlayers:
                if (board.TryGetTeam(packet.Name, out Team? addTeam) && addTeam is not null)
                    foreach (string member in packet.Players)
                        addTeam.AddMember(member);

                break;
            case TeamMethod.RemovePlayers:
                if (board.TryGetTeam(packet.Name, out Team? removeTeam) && removeTeam is not null)
                    foreach (string member in packet.Players)
                        removeTeam.RemoveMember(member);

                break;
        }
    }

    /// <summary>Mirrors the local player's tab-list latency onto server state. This is the server's own measurement of the keep-alive round trip, which is the only real network latency figure a client gets, so it is worth surfacing somewhere a consumer can find without walking the tab list.</summary>
    private static void RecordSelfLatency(ApplierContext context, Guid profileId, int latency)
    {
        if (profileId == context.State.Self.Uuid)
            context.State.Server.ObservedLatency = latency;

    }

    private static void ApplyPlayerInfo(ClientboundPlayerInfoUpdatePacket packet, ApplierContext context)
    {
        TabList list = context.State.TabList;
        foreach (PlayerInfoEntry entry in packet.Entries)
        {
            TabListEntry tabEntry = GetOrCreateEntry(
                list, entry.ProfileId, entry.Name, entry.Properties,
                carriesProfile: (packet.Actions & PlayerInfoActions.AddPlayer) != 0);

            // INITIALIZE_CHAT carries the peer's chat session: session id, key expiry, DER profile public key and Mojang's signature over it. Without this the decoded payload was dropped on the floor and inbound signature verification had no key source at all. A null payload is meaningful (vanilla's Optional is empty when the player has no chat session), so it is assigned rather than merged.
            if ((packet.Actions & PlayerInfoActions.InitializeChat) != 0)
                tabEntry.ChatSession = entry.ChatSession;

            if ((packet.Actions & PlayerInfoActions.UpdateGameMode) != 0)
                tabEntry.GameMode = entry.GameMode;

            if ((packet.Actions & PlayerInfoActions.UpdateLatency) != 0)
            {
                tabEntry.Latency = entry.Latency;
                RecordSelfLatency(context, entry.ProfileId, entry.Latency);
            }

            if ((packet.Actions & PlayerInfoActions.UpdateListed) != 0)
                tabEntry.Listed = entry.Listed;

            if ((packet.Actions & PlayerInfoActions.UpdateDisplayName) != 0)
                tabEntry.DisplayName = entry.DisplayName;

            if ((packet.Actions & PlayerInfoActions.UpdateListOrder) != 0)
                tabEntry.ListOrder = entry.ListOrder;

            if ((packet.Actions & PlayerInfoActions.UpdateHat) != 0)
                tabEntry.ShowHat = entry.ShowHat;

            list.Upsert(tabEntry);
            BackfillPlayerProfile(context, tabEntry);
        }
    }

    /// <summary>Applies the pre-1.19.3 single <c>player_info</c> packet (protocols 47-760), which carries one action for a batch of entries instead of the modern bitset. It lands on exactly the same <see cref="TabList"/> and <see cref="TabListEntry"/> state as the modern split pair, through the same <see cref="GetOrCreateEntry"/> helper, so a consumer sees one tab-list model on every era. The legacy wire has no "listed", list-order or show-hat notion; those keep the entry defaults (listed and hat shown), which is what the vanilla pre-1.19.3 client renders.</summary>
    private static void ApplyLegacyPlayerInfo(ClientboundLegacyPlayerListItemPacket packet, ApplierContext context)
    {
        TabList list = context.State.TabList;
        foreach (LegacyPlayerListEntry entry in packet.Entries)
        {
            if (packet.Action == LegacyPlayerListAction.RemovePlayer)
            {
                list.Remove(entry.ProfileId);
                continue;
            }

            TabListEntry tabEntry = GetOrCreateEntry(
                list, entry.ProfileId, entry.Name, entry.Properties,
                carriesProfile: packet.Action == LegacyPlayerListAction.AddPlayer);
            switch (packet.Action)
            {
                case LegacyPlayerListAction.AddPlayer:
                    // Add is the only action carrying the whole profile, so game mode, latency and the display-name override all ride it. The modern equivalent is an update whose action set is AddPlayer|UpdateGameMode|UpdateLatency|UpdateDisplayName.
                    tabEntry.GameMode = (GameMode)entry.GameMode;
                    tabEntry.Latency = entry.Latency;
                    tabEntry.DisplayName = entry.DisplayName;
                    // On 759/760, add-player carries the peer's optional profile public key. It is the v1/v2 equivalent of INITIALIZE_CHAT and the only key source on those protocols.
                    tabEntry.ChatSession = entry.ProfileKey;
                    RecordSelfLatency(context, entry.ProfileId, entry.Latency);
                    break;
                case LegacyPlayerListAction.UpdateGameMode:
                    tabEntry.GameMode = (GameMode)entry.GameMode;
                    break;
                case LegacyPlayerListAction.UpdateLatency:
                    tabEntry.Latency = entry.Latency;
                    RecordSelfLatency(context, entry.ProfileId, entry.Latency);
                    break;
                case LegacyPlayerListAction.UpdateDisplayName:
                    // A null display name is meaningful: it clears the override back to the profile name.
                    tabEntry.DisplayName = entry.DisplayName;
                    break;
                default:
                    break;
            }

            list.Upsert(tabEntry);
            BackfillPlayerProfile(context, tabEntry);
        }
    }

    /// <summary>Returns the tracked tab-list entry for a uuid, or builds one from the profile fields the packet carries. Shared by both player-info eras so the legacy path cannot drift into a parallel model. The profile properties (the signed <c>textures</c> skin/cape blob) are carried onto the profile; both eras encode them identically on the add action.</summary>
    private static TabListEntry GetOrCreateEntry(
        TabList list, Guid profileId, string? name, IReadOnlyList<GameProfileProperty>? properties,
        bool carriesProfile)
    {
        var profile = new GameProfile(profileId, name ?? string.Empty)
        {
            Properties = MapProfileProperties(properties),
        };

        if (!list.TryGet(profileId, out TabListEntry? existing))
            return new TabListEntry(profile);

        // Only the add action carries a name and properties, and the server does not guarantee it arrives first: vanilla broadcasts the join-time game mode alongside the add, and either can land first. Whichever loses the race created a placeholder entry with an empty name, so the add has to install the real profile over it rather than keep the placeholder. Without this a player who lost the race stays nameless for the whole session, which also leaves every entity backfill keyed off that entry without a profile.
        if (!carriesProfile || existing.Profile.Name == profile.Name)
            return existing;

        // TabListEntry.Profile is immutable by design, so replacing it means a new entry; carry the mutable fields across so an update that arrived before the add is not lost.
        return new TabListEntry(profile)
        {
            GameMode = existing.GameMode,
            Latency = existing.Latency,
            DisplayName = existing.DisplayName,
            Listed = existing.Listed,
            ListOrder = existing.ListOrder,
            ShowHat = existing.ShowHat,
            ChatSession = existing.ChatSession,
        };
    }

    /// <summary>Copies a freshly learned tab-list profile onto the tracked entity with the same uuid, if one is already spawned. A player spawn carries the uuid but never the name, so which of the two packets arrives first would otherwise decide whether the entity ever resolves a name. Spawn-then-info is resolved here; info-then-spawn is resolved in <c>EntityApplier</c>. Runs only when the Entities feature is on, and matches by uuid, which is unique per player.</summary>
    private static void BackfillPlayerProfile(ApplierContext context, TabListEntry entry)
    {
        Game.Entities.EntityStore? entities = context.State.EntitiesOrNull;
        if (entities is not null
            && entities.TryGetByUuid(entry.Uuid, out Game.Entities.Entity? entity)
            && entity.PlayerProfile != entry.Profile)
            entity.PlayerProfile = entry.Profile;

    }

    private static IReadOnlyList<ProfileProperty> MapProfileProperties(IReadOnlyList<GameProfileProperty>? properties)
    {
        if (properties is null || properties.Count == 0)
            return [];

        var mapped = new ProfileProperty[properties.Count];
        for (int i = 0; i < properties.Count; i++)
        {
            GameProfileProperty property = properties[i];
            mapped[i] = new ProfileProperty(property.Name, property.Value, property.Signature);
        }

        return mapped;
    }

    private static async ValueTask ApplyBossAsync(ClientboundBossEventPacket packet, ApplierContext context)
    {
        BossBarState bars = context.State.BossBars;
        switch (packet.Operation)
        {
            case BossEventOperation.Remove:
                bars.Remove(packet.Id);
                break;
            case BossEventOperation.Add:
                bars.Add(new BossBar(packet.Id, packet.Title ?? Umpk.Text.Component.Empty, packet.Progress, packet.Color, packet.Overlay, packet.Flags));
                break;
            default:
                if (bars.TryGet(packet.Id, out BossBar? existing) && existing is not null)
                    bars.Add(new BossBar(
                        packet.Id,
                        packet.Title ?? existing.Title,
                        packet.Operation == BossEventOperation.UpdateProgress ? packet.Progress : existing.Progress,
                        packet.Color,
                        packet.Overlay,
                        packet.Flags));

                break;
        }

        await context.PublishAsync(new BossBarChanged(packet.Id)).ConfigureAwait(false);
    }

    private static void ApplyLegacyScore(ClientboundLegacySetScorePacket score, ApplierContext context)
    {
        Scoreboard scoreboard = context.State.Scoreboard;
        if (score.Action != LegacyScoreAction.Remove)
        {
            scoreboard.SetScore(score.ObjectiveName, score.Owner, score.Value);
            return;
        }

        if (string.IsNullOrEmpty(score.ObjectiveName))
            scoreboard.ResetScores(score.Owner);

        else
            scoreboard.RemoveScore(score.ObjectiveName, score.Owner);

    }

    private static async ValueTask ProcessResourcePackAsync(
        ResourcePackRequest request,
        Func<ResourcePackResponse, ValueTask> report,
        ApplierContext context,
        CancellationToken ct)
    {
        try
        {
            await context.Policies.ResourcePack.ProcessAsync(request, report, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Logger.LogError(ex, "Resource-pack policy threw; declining pack {Id}.", request.Id);
            await report(ResourcePackResponse.Declined).ConfigureAwait(false);
        }
    }

    /// <summary>Whether this protocol can actually SEND a packet record, i.e. its type is in the outbound table with a bound codec.</summary>
    /// <remarks>A wire-id lookup by identifier is NOT this question: the registry answers it for markers too, and for eras that register the same wire slot under a legacy identifier it answers -1 even though the packet is perfectly sendable. Both mistakes are silent, so the gate consults the outbound table.</remarks>
    private static bool CanSend(ApplierContext context, PacketType type) =>
        context.Version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry)
        && registry.TryGetOutbound(type, out _, out BoundPacketCodec bound)
        && bound.IsImplemented;

    private static async ValueTask RespondResourcePackAsync(
        Guid id, string url, string hash, bool required, ApplierContext context, CancellationToken ct)
    {
        // Modern (1.20.3+) sends the pack id; 1.14-1.20.2 send a bare action. The record for both is ServerboundResourcePackPacket(Guid, int); the codec handles the era difference.
        await ProcessResourcePackAsync(
            new ResourcePackRequest(id, url, hash, required),
            async decision =>
            {
                var response = new ServerboundResourcePackPacket(id, (ResourcePackAction)(int)decision);
                if (CanSend(context, response.Type))
                    await context.Sink.SendAsync(response, ct).ConfigureAwait(false);
            },
            context,
            ct).ConfigureAwait(false);
    }

    private static async ValueTask RespondConfigResourcePackAsync(
        Guid id, string url, string hash, bool required, ApplierContext context, CancellationToken ct)
    {
        await ProcessResourcePackAsync(
            new ResourcePackRequest(id, url, hash, required),
            decision => context.Sink.SendAsync(new ServerboundConfigResourcePackPacket(id, (int)decision), ct),
            context,
            ct).ConfigureAwait(false);
    }

    /// <summary>Answers the pre-1.20.3 resource-pack request, which carries a url and a hash and no pack uuid. A server running <c>require-resource-pack</c> KICKS a client that never answers, so on those protocols an unanswered request is not a cosmetic gap: it is a connection that cannot be held.</summary>
    /// <remarks>The response echoes the request hash. 47-110 put that hash on the wire ahead of the result and 210-404 send the result alone, but the record is the same either way and the era codec decides whether the hash is written, so this path does not branch on the version.</remarks>
    private static async ValueTask RespondLegacyResourcePackAsync(
        ClientboundLegacyResourcePackPacket pack, ApplierContext context, CancellationToken ct)
    {
        await ProcessResourcePackAsync(
            new ResourcePackRequest(Guid.Empty, pack.Url, pack.Hash, Required: false),
            async decision =>
            {
                var response = new ServerboundLegacyResourcePackPacket(pack.Hash, (ResourcePackAction)(int)decision);
                if (CanSend(context, response.Type))
                    await context.Sink.SendAsync(response, ct).ConfigureAwait(false);
            },
            context,
            ct).ConfigureAwait(false);
    }
}
