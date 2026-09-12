using Umpk.Data.Java;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>One declared-deliberate marker: a packet identity, the exact protocols it is a marker on, and why.</summary>
/// <param name="Phase">The protocol phase.</param>
/// <param name="Flow">The packet flow.</param>
/// <param name="Identifier">The packet identifier without its <c>minecraft:</c> namespace.</param>
/// <param name="Reason">Which class of deliberate this is.</param>
/// <param name="Protocols">The EXACT set of protocols on which this identity is a marker.</param>
/// <param name="Why">The prose justification. Reviewed as code; there is no generator for it.</param>
internal sealed record MarkerAllowance(
    ProtocolPhase Phase,
    PacketFlow Flow,
    string Identifier,
    MarkerReason Reason,
    IReadOnlyList<int> Protocols,
    string Why);

/// <summary>The intentional-marker allowlist: every registered-but-unimplemented packet in the shipped catalog, declared once with a reason, and checked exactly by <see cref="IntentionalMarkerAllowlistTests"/>.</summary>
/// <remarks>
/// <para>Three deliberate design decisions, because the obvious version of this file rots within a release.</para>
/// <list type="number">
/// <item><b>Hand-authored, with no regeneration path.</b> The registration and codec-identity pins have a
/// <c>UMPK_UPDATE_*</c> env var precisely because they are mechanical. This file has none, on purpose: an entry can only be added by writing one, which is a code review rather than a fixture refresh. A regenerable allowlist would be a rubber stamp by its second use.</item>
/// <item><b>Keyed by exact protocol set, not by range.</b> The range helpers below are sugar that expands
/// to a set of supported protocol numbers; the check compares SETS. So an allowlisted marker that later gains a codec fails rather than passing quietly, keeping every declaration current.</item>
/// <item><b>A reason string per entry, validated.</b> Bare identifiers would decay into a checklist.
/// The gate requires a reason of real length, ending in a full stop, free of placeholder words, so an entry cannot be added without saying something.</item>
/// </list>
/// <para>Every reason states the observable limitation and what it costs.</para>
/// <para>Every reason but <see cref="MarkerReason.KnownGap"/> records a decision, and a decision must also be declared where the packet is registered, on the same protocols and with the same reason. The two are checked against each other, so neither can drift; the gaps are the backlog and gain a declaration as they are worked on.</para>
/// </remarks>
internal static class IntentionalMarkers
{
    /// <summary>Every supported protocol number, ascending.</summary>
    private static readonly int[] Supported =
        [.. JavaVersions.All.Select(v => v.Version.Protocol).Distinct().Order()];

    /// <summary>The declared-deliberate marker set. Ordered phase, flow, identifier.</summary>
    internal static IReadOnlyList<MarkerAllowance> All { get; } =
    [
        // Configuration phase
        new(ProtocolPhase.Configuration, PacketFlow.Clientbound, "code_of_conduct", MarkerReason.KnownGap, From(773),
            "The 26.x code-of-conduct prompt. Unimplemented on both flows, so a server that requires a code of conduct cannot be joined at all. Not a misbinding: no protocol binds either half. Recorded as a residual by U16."),

        new(ProtocolPhase.Configuration, PacketFlow.Serverbound, "accept_code_of_conduct", MarkerReason.KnownGap, From(773),
            "The client half of the 26.x code-of-conduct handshake, unimplemented with its clientbound partner above. Until both land, joining a server that requires one is impossible rather than degraded."),

        new(ProtocolPhase.Configuration, PacketFlow.Serverbound, "bundle_delimiter", MarkerReason.DatasetArtifact, Only(765),
            "Vanilla has no serverbound configuration bundle delimiter; the bundler wraps clientbound play traffic only. The dataset row is a curation artifact, so a marker is the correct outcome and binding anything here would invent a packet. Flagged by U16 for deletion at source."),

        // Login phase
        new(ProtocolPhase.Login, PacketFlow.Serverbound, "bundle_delimiter", MarkerReason.DatasetArtifact, Between(762, 763),
            "Same curation artifact as the configuration row above: vanilla has no serverbound login bundle delimiter. A marker is correct; the dataset rows for 762 and 763 are what should go."),

        // Play, clientbound
        new(ProtocolPhase.Play, PacketFlow.Clientbound, "add_global_entity", MarkerReason.KnownGap, Between(107, 578),
            "The pre-1.16 lightning-bolt spawn packet, folded into add_entity at 1.16. Unbound on every protocol that carries it, so a lightning strike never becomes a tracked entity on 1.9 through 1.15.2."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "add_vibration_signal", MarkerReason.KnownGap, Between(755, 758),
            "The 1.17 sculk vibration signal, replaced at 1.19 by an ordinary particle. Unbound on all four protocols that carry it, so a consumer never sees vibration sources on that band."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "award_stats", MarkerReason.KnownGap, From(107),
            "The statistics payload a server sends in reply to a stats request. No consumer stores statistics on any protocol, so nothing decodes it; the 1.8 spelling minecraft:statistics is the same hole under an older name."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "block_break_ack", MarkerReason.KnownGap, Between(477, 758),
            "The 1.14 through 1.18.2 server acknowledgement of a dig action, superseded at 1.19 by the block-changed-ack sequence. Unbound across the band, so a rejected dig is not observed there; the modern sequence path is bound and unaffected."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "chat_preview", MarkerReason.KnownGap, Between(759, 760),
            "The server's answer to a preview request, removed again at 1.19.3. It arrives only in reply to a minecraft:chat_preview this client never sends, so it never arrives at all; binding it would decode a frame no session receives. What a consumer loses is the ability to sign over the server's DECORATION of its own message: without the round trip the serverbound signedPreview flag is false and the signature covers the plain text. That is not silent. minecraft:set_display_chat_preview IS bound, so a previewing server sets ClientState.Chat.ServerPreviewsChat, raises ChatPreviewAnnounced and logs a warning naming the protocol, so a consumer that cares can branch on that state rather than discovering it from a server-side drop."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "combat_event", MarkerReason.KnownGap, Only(47),
            "The 1.8 spelling of the combat event, which is also a marker as minecraft:player_combat on 107 through 754 and as the three 1.17 successors from 755. Nothing consumes combat events on any protocol, so a death reason never reaches a consumer anywhere."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "custom_payload", MarkerReason.HandledElsewhere, AllProtocols(),
            "Deliberate. The play plugin message is served by the raw-frame path rather than a codec: PluginChannelManager matches the observed wire id and reads channel plus body itself, and UmpkClient reads the pre-1.20.2 server brand straight off the observed payload through ServerBrandPayload. Both paths run on every session, so a codec here would duplicate working code."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "debug/block_value", MarkerReason.NoConsumerWorthWriting, From(773),
            "One of the 1.21.9 debug-subscription payloads. A vanilla server sends it only to a client that subscribed through minecraft:debug_subscription_request, and nothing in UMPK subscribes, so the frame never arrives."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "debug/chunk_value", MarkerReason.NoConsumerWorthWriting, From(773),
            "A 1.21.9 debug-subscription payload, delivered only to a subscriber. Nothing in UMPK subscribes, so there is no consumer worth writing for it."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "debug/entity_value", MarkerReason.NoConsumerWorthWriting, From(773),
            "A 1.21.9 debug-subscription payload, delivered only to a subscriber. Nothing in UMPK subscribes, so there is no consumer worth writing for it."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "debug/event", MarkerReason.NoConsumerWorthWriting, From(773),
            "A 1.21.9 debug-subscription payload, delivered only to a subscriber. Nothing in UMPK subscribes, so there is no consumer worth writing for it."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "debug_sample", MarkerReason.NoConsumerWorthWriting, From(766),
            "The 1.20.5 tick-timing debug sample, sent only in reply to minecraft:debug_sample_subscription. Nothing in UMPK subscribes, so it never arrives."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "game_rule_values", MarkerReason.KnownGap, From(775),
            "The 26.1 game-rule broadcast. Unbound on both protocols that carry it, together with its serverbound partner minecraft:set_game_rule, so a consumer never learns the server's rule values."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "game_test_highlight_pos", MarkerReason.NoConsumerWorthWriting, From(773),
            "A game-test harness packet a vanilla server emits only while running the developer test framework. It has no gameplay meaning, so nothing would read a decoded value."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "horse_screen_open", MarkerReason.KnownGap, Between(477, 773),
            "The mount inventory screen, renamed minecraft:mount_screen_open at 1.21.11. Unbound under both spellings, so opening a horse, donkey or llama inventory yields no container on any protocol."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "low_disk_space_warning", MarkerReason.KnownGap, From(775),
            "The 26.1 server low-disk-space notice. Unbound on both protocols that carry it, so a consumer never sees the warning a vanilla client would surface."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "mount_screen_open", MarkerReason.KnownGap, From(774),
            "The 1.21.11 rename of minecraft:horse_screen_open, unbound for the same reason: no mount-inventory container is modeled on any protocol."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "move_entity", MarkerReason.NoConsumerWorthWriting, Between(107, 754),
            "Vanilla's zero-delta base entity move (ClientboundMoveEntityPacket with neither a position nor a rotation subclass). It carries an entity id and nothing else, and vanilla's own handler applies no state from it, so discarding it costs nothing observable. The 1.8 spelling minecraft:entity is bound at 47."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "move_minecart_along_track", MarkerReason.KnownGap, From(768),
            "The 1.21.2 minecart-on-rail movement packet. Unbound on the whole band, so a consumer riding a minecart on a server with the new movement enabled sees no server-driven position."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "move_vehicle", MarkerReason.KnownGap, From(107),
            "The server-driven vehicle position correction. Its serverbound twin IS bound from 477, so UMPK sends vehicle movement and then ignores every correction the server sends back, and a boat or horse desyncs silently."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_chat_header", MarkerReason.KnownGap, Only(760),
            "The 1.19.1 detached chat header, used to relay a signed message's header when the body was filtered out. Unbound on the one protocol that carries it; the v2 signing chain is separately documented as partial."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_combat", MarkerReason.KnownGap, Between(107, 754),
            "The pre-1.17 combat event: enter combat, end combat, and death including the death message. Unbound here, and its three 1.17 successors are unbound too, so no protocol surfaces a death reason from this family."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_combat_end", MarkerReason.KnownGap, From(755),
            "The leave-combat half of the 1.17 combat-event split. Unbound on the whole band, as are the other two halves and the pre-1.17 minecraft:player_combat."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_combat_enter", MarkerReason.KnownGap, From(755),
            "The enter-combat half of the 1.17 combat-event split. Unbound on the whole band, alongside the other two halves."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_combat_kill", MarkerReason.KnownGap, From(755),
            "The death half of the 1.17 combat-event split, which carries the death message a consumer would show. Unbound on the whole band, so a client learns it died from its health and the respawn flow but never with a reason."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_look_at", MarkerReason.KnownGap, From(393),
            "The server's forced look direction, what vanilla's teleport-facing form sends. Unbound on every protocol that carries it, so the server's rotation is ignored and the client's view keeps diverging from the server's record of it."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "player_rotation", MarkerReason.KnownGap, From(768),
            "The 1.21.2 server-driven rotation update. Unbound on the whole band, with the same consequence as player_look_at above: a forced rotation is dropped."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "projectile_power", MarkerReason.KnownGap, From(766),
            "The 1.20.5 fireball acceleration update. Unbound on the whole band, so a tracked projectile's acceleration is never corrected from the server."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "set_border", MarkerReason.KnownGap, Between(107, 754),
            "The pre-1.17 combined world-border packet, split at 1.17 into the set_border family that IS bound. So the border is tracked from 755 up and invisible on 107 through 754."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "set_compression", MarkerReason.KnownGap, Only(47),
            "The 1.8-only play-phase set-compression frame; 1.9 moved compression negotiation entirely into login. Unbound, so a 1.8 server that changes the threshold mid-play desyncs the stream. Rare in practice because servers set it during login, where the packet IS bound."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "set_titles", MarkerReason.KnownGap, Between(107, 754),
            "The pre-1.17 combined title packet, split at 1.17 into set_title_text, set_subtitle_text and set_titles_animation, all of which are bound. So titles are observed from 755 up and dropped on 107 through 754."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "sound", MarkerReason.WrongCodecWouldBeWorse, Between(735, 758),
            "Declared deliberate in WorldBindings with MarkerFrom(V1_16). 1.16 reshaped the positional sound packet, which gained the random seed, and no codec models that form, so the band relays verbatim. Both neighbouring codecs mis-frame it: this is the only identifier in this list that is bound on BOTH sides of its marker band, which is exactly the shape a real misbinding would also have, so the reason matters more here than anywhere else."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "statistics", MarkerReason.KnownGap, Only(47),
            "The 1.8 spelling of minecraft:award_stats, which is a marker on all forty-eight later protocols too. Nothing consumes statistics on any version, so this is the oldest name on a hole that spans the catalog."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "tag_query", MarkerReason.KnownGap, From(393),
            "The operator-only NBT query response, the reply half of block_entity_tag_query and entity_tag_query. All three are unbound on every protocol that carries them, so the round trip does not exist rather than half existing."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "test_instance_block_status", MarkerReason.NoConsumerWorthWriting, From(770),
            "Part of the 1.21.5 game-test instance-block tooling, which only a developer running the test framework drives. No gameplay consumer would read it."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "ticking_state", MarkerReason.KnownGap, From(765),
            "The 1.20.3 server tick-rate and freeze state. Unbound with its ticking_step partner, so a consumer cannot tell a frozen or slowed server from a stalled connection."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "ticking_step", MarkerReason.KnownGap, From(765),
            "The 1.20.3 single-step advance for a frozen server. Unbound with ticking_state above, and with the same consequence."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "update_enabled_features", MarkerReason.KnownGap, Between(761, 763),
            "The 1.19.3 through 1.20.1 feature-flag announcement, moved into the configuration phase at 1.20.2. Unbound on the three protocols that carry it in play, so a consumer cannot tell which experimental packs are enabled there."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "update_entity_nbt", MarkerReason.KnownGap, Only(47),
            "A 1.8-only entity NBT update, removed at 1.9. Unbound, and no consumer stores entity NBT on any protocol, so there is nothing for a decoded value to reach."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "update_sign", MarkerReason.KnownGap, Between(47, 109),
            "The 1.8 through 1.9.2 clientbound sign contents packet, dropped by vanilla in favour of block entity data. Unbound on all four protocols that carry it, so sign text does not reach a consumer there. Distinct from the serverbound minecraft:sign_update entry below, which is a different argument."),

        new(ProtocolPhase.Play, PacketFlow.Clientbound, "update_tags", MarkerReason.KnownGap, From(393),
            "The registry tag sets for blocks, items, fluids and entities. Unbound on every protocol that carries it, and nothing in UMPK models tags, so a consumer cannot ask whether a block is in minecraft:logs."),

        // Play, serverbound
        new(ProtocolPhase.Play, PacketFlow.Serverbound, "block_entity_tag_query", MarkerReason.KnownGap, From(393),
            "The operator-only block NBT query request. Its reply minecraft:tag_query is unbound too, so the round trip is wholly unimplemented rather than half wired, which is the better of the two states to be in."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "bundle_item_selected", MarkerReason.KnownGap, From(768),
            "The 1.21.2 bundle-slot selection a client sends while scrolling a bundle's contents. Unbound on the whole band; UMPK models no bundle UI to drive it."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "change_difficulty", MarkerReason.KnownGap, From(477),
            "The client request to change server difficulty, which vanilla accepts from a permission-level-two player or a single-player host. Unbound on the whole band, so an operator client cannot change difficulty. Not a misbinding: the packet has no codec on any protocol."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "change_game_mode", MarkerReason.KnownGap, From(771),
            "The 1.21.6 client-side game-mode request. Unbound on the whole band, so an operator client cannot switch its own mode except through the chat command path."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "chat_ack", MarkerReason.WrongCodecWouldBeWorse, Only(760),
            "Deliberate on U13's evidence, and re-checked against the current tree: U14 did not close it. The 1.19.1 acknowledgement body is a whole LastSeenMessages.Update, a counted list of uuid plus signature entries with an optional trailing entry, while the 761-and-up codec that IS bound writes a bare VarInt offset. ChatCodecs.ReadLastSeenV2 discards the per-entry sender uuid the v2 body needs, so the model has to grow before this can bind at all."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "chat_preview", MarkerReason.KnownGap, Between(759, 760),
            "The client half of the 1.19 chat preview, removed at 1.19.3. Vanilla fires it from the chat SCREEN as the user types so the answer is cached by the time send is pressed, and a headless client has no typing surface to hang that round trip on; doing it inside the send would put a blocking server round trip inside the chat-signing ordering lock and stall every other send behind it. What a consumer loses is stated on the clientbound entry above, and it is declared rather than hidden: the serverbound chat codecs say in as many words that they write the preview flag false on purpose, and ClientState.Chat.ServerPreviewsChat reports when that costs anything."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "custom_payload", MarkerReason.HandledElsewhere, AllProtocols(),
            "Deliberate, the send half of the play plugin message. PluginChannelManager writes the channel and body into a raw frame and sends it by wire id, and it is what the REGISTER and UNREGISTER channel announcements go out on, so a codec here would duplicate a path that already runs on every session."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "debug_sample_subscription", MarkerReason.NoConsumerWorthWriting, Between(766, 772),
            "The subscribe request for the 1.20.5 debug samples. Nothing in UMPK wants the samples, so sending it would only create traffic nobody reads."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "debug_subscription_request", MarkerReason.NoConsumerWorthWriting, From(773),
            "The 1.21.9 replacement for debug_sample_subscription and the gate for the whole debug payload family above. Nothing subscribes, so none of those payloads ever arrive."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "entity_tag_query", MarkerReason.KnownGap, From(393),
            "The operator-only entity NBT query request, unbound with its block twin and with the minecraft:tag_query reply, so the whole query round trip is missing rather than partly present."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "jigsaw_generate", MarkerReason.KnownGap, From(735),
            "The creative-only jigsaw generate action. Unbound on the whole band together with minecraft:set_jigsaw_block, so structure editing is absent rather than half wired."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "lock_difficulty", MarkerReason.KnownGap, From(477),
            "The difficulty lock that accompanies the change_difficulty request above. Unbound on the same band and for the same reason: no operator surface sends either."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "pick_item", MarkerReason.KnownGap, Between(393, 768),
            "The creative middle-click pick. Unbound over the band, so a creative client cannot pull a block into its hotbar; the band ends at 768 because 1.21.4 replaces the packet with a different pair of identifiers."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "place_recipe", MarkerReason.WrongCodecWouldBeWorse, Between(338, 340),
            "Deliberate on U13's evidence. 1.12.1 and 1.12.2 send a NUMERIC crafting-manager recipe id where 1.13 and later send a resource location, and the bound codec is the by-name record. Binding it here would write a length-prefixed string into a field the server reads as an int."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "prepare_crafting_grid", MarkerReason.KnownGap, Only(335),
            "The 1.12-only crafting-grid prefill request, replaced at 1.12.1 by minecraft:place_recipe. Unbound, so the 1.12 recipe book cannot fill the crafting grid. Same family as the place_recipe entry above and equally narrow."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "recipe_book_update", MarkerReason.KnownGap, Between(335, 736),
            "The pre-1.16.2 combined recipe-book state update, the displayed and filtering toggles plus the seen set, split at 1.16.2 into recipe_book_change_settings and recipe_book_seen_recipe, both of which are bound. So recipe-book state is sent from 751 up and not on 335 through 736."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_beacon", MarkerReason.KnownGap, From(393),
            "The beacon effect selection. Unbound on every protocol that carries it, so a client cannot apply a beacon's primary or secondary power."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_command_minecart", MarkerReason.KnownGap, From(393),
            "The operator-only command-block minecart edit. Its stationary twin minecraft:set_command_block IS bound, so this is a remaining hole in a family that is otherwise wired rather than a misbinding."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_game_rule", MarkerReason.KnownGap, From(775),
            "The 26.1 client request to change a game rule, unbound with its clientbound partner minecraft:game_rule_values. Neither half exists, so the feature is absent rather than one-directional."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_jigsaw_block", MarkerReason.KnownGap, From(477),
            "The creative-only jigsaw block edit, unbound together with minecraft:jigsaw_generate. Structure editing is not a modeled surface on any protocol."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_structure_block", MarkerReason.KnownGap, From(393),
            "The operator-only structure block edit. Unbound on every protocol that carries it, in the same unmodeled family as the jigsaw pair above."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "set_test_block", MarkerReason.NoConsumerWorthWriting, From(770),
            "Part of the 1.21.5 game-test block tooling, driven only by a developer running the test framework. Nothing a client session would send."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "sign_update", MarkerReason.WrongCodecWouldBeWorse, Only(47),
            "Declared deliberate in UiBindings with MarkerFrom(V1_8) and a full evidence comment from U13. 1.8 writes each of the four sign lines as an IChatComponent JSON string, per 1.8.9 MCP C12PacketUpdateSign, where the 1.9 codec writes a plain line. The FRAMING is identical, which is exactly why it looks like an off-by-one, and binding the 1.9 form would encode plain text into a field the 1.8 server hands to the JSON deserializer."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "spectate_entity", MarkerReason.KnownGap, Only(775),
            "New at 26.1 and NOT the same packet as minecraft:teleport_to_entity, which stays registered and bound at a different wire id on the same protocol. The 26.1 decompiled ServerboundSpectateEntityPacket is a single VarInt entity id where teleport_to_entity carries a uuid. Unbound, so a spectator client on 26.1 cannot use the packet vanilla now uses."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "spectator_action", MarkerReason.KnownGap, From(776),
            "The 26.2 successor to spectate_entity: the decompiled ServerboundSpectatorActionPacket carries an OPTIONAL VarInt entity id, so it also expresses stopping spectating. Unbound, with the same consequence as the 26.1 entry above."),

        new(ProtocolPhase.Play, PacketFlow.Serverbound, "test_instance_block_action", MarkerReason.NoConsumerWorthWriting, From(770),
            "The client half of the 1.21.5 game-test instance-block tooling. Only a developer running the test framework drives it, so nothing a client session would send."),
    ];

    /// <summary>Every supported protocol. For an identity that is a marker on the whole catalog.</summary>
    private static int[] AllProtocols() => Supported;

    /// <summary>The supported protocols from <paramref name="first"/> to the newest, inclusive.</summary>
    private static int[] From(int first) => [.. Supported.Where(p => p >= first)];

    /// <summary>The supported protocols in <c>[first, last]</c>, inclusive.</summary>
    private static int[] Between(int first, int last) => [.. Supported.Where(p => p >= first && p <= last)];

    /// <summary>An explicit protocol set, for a marker whose band is not contiguous over supported versions.</summary>
    private static int[] Only(params int[] protocols) => protocols;
}
