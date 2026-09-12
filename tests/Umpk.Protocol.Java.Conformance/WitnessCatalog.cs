namespace Umpk.Protocol.Java.Conformance;

/// <summary>The hand-authored witness declarations: tranche one, the multi-era packets ordered by fork count.</summary>
/// <remarks>
/// <para>Fork count is the ordering because it is where the wrong-era binding has actually recurred: a packet whose timeline forks twenty times has twenty chances to be bound one band off, and the all-zero probe in the codec-identity pin cannot see any of them. The twenty packets here carry 173 of the tree's era steps between them; the ten the tranche reached for and could not finish are named in <see cref="Deferred"/> with the pairs that defeated them. Everything outside the tranche stays exactly as protected as it was, which is to say by a <c>read:N</c> string diff, and this file says so rather than implying a gate it does not build.</para>
/// <para>One declaration per packet, not per band: the work is choosing a payload the neighbouring layouts disagree about, and that choice is the same for every band of one packet. The harness mints one witness per band from it, so a band added tomorrow arrives with a witness already attached and its rejection clause already required.</para>
/// </remarks>
internal static partial class WitnessCatalog
{
    /// <summary>The size of the tranche, so a packet cannot leave it quietly.</summary>
    internal const int TrancheOne = 20;

    /// <summary>Every declared packet, ordered by fork count.</summary>
    internal static IReadOnlyList<WitnessedPacket> All { get; } =
    [
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_entity_data",
            "Four serializer kinds in one list. The 1.8 packed format writes type<<5|index and ends at " +
            "0x7F while every modern era writes index, VarInt serializer id, value and ends at 0xFF, and " +
            "the modern serializer table shifts whenever a serializer is inserted, so a list carrying " +
            "more than one kind moves with the table. A single byte field would not.",
            [SetEntityData, SetEntityDataWithNbt, SetEntityDataLegacy], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:container_set_content",
            "Three slots, two of them non-empty and one of them empty, plus a carried stack. The count " +
            "prefix width, the state id, the carried-stack field and the item stack's own era all move " +
            "across this timeline, and an empty inventory shows none of them.",
            [ContainerSetContent], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:container_set_slot",
            "A non-empty stack in a numbered slot. The container id widens from a signed byte to a " +
            "VarInt, the state id arrives at 1.17.1, and the stack encoding changes under it repeatedly; " +
            "an empty stack collapses all of that to a single present flag.",
            [ContainerSetSlot], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:update_advancements",
            "One advancement with display info, a background, a criterion list, a requirement group, the " +
            "telemetry flag and a progress entry. The criterion list leaves the node at 1.20.2, the " +
            "telemetry bool arrives at 1.20, the show-advancements flag arrives at 1.21.5 and the icon " +
            "is an item stack, so the node exercises four independent boundaries at once.",
            [UpdateAdvancements], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:merchant_offers",
            "A trade with a second cost, a price multiplier, a demand and a non-zero special price. The " +
            "second cost is optional on the wire, demand and special price arrive later than the rest, " +
            "and the three stacks carry the item era with them.",
            [MerchantOffers], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:set_creative_mode_slot",
            "A non-empty stack in a signed slot. The slot widens from a short to a VarInt at 1.21.5 and " +
            "the stack encoding is the item era; an empty stack would only prove the present flag.",
            [SetCreativeModeSlot]),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:level_chunk_with_light",
            "A real recorded column wherever a capture reaches the band, because a chunk is the one " +
            "payload in the tree that cannot be authored more faithfully than it can be recorded: the " +
            "section framing, the palette widths, the heightmap shape and the light and block-entity " +
            "tails are all era properties and all present in the real bytes. The 1.8 band is the " +
            "exception, since its capture carries only bulk frames, so it is built by hand.",
            [LegacyChunk], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:respawn",
            "A spawn-info block with a portal cooldown and a sea level, plus the legacy dimension and " +
            "level-type fields. 1.20.2 folded the inline fields into the common block, 1.21.2 added the " +
            "sea level, and the data-to-keep mask replaced the copy-metadata bool.",
            [Respawn], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:commands",
            "A four-node tree with a literal, an integer argument carrying properties, and a " +
            "minecraft:function argument whose wire id moved when 1.19.3 reshuffled the parser table. " +
            "The parser identifier became a numeric id at 1.19, the properties are read by the era's own " +
            "table, and the id shift is the only thing separating two of these bands.",
            [Commands, CommandsTeamColor], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_cursor_item",
            "A non-empty stack, so the packet is the item era and nothing else. Every band of it is an " +
            "item-component table.",
            [SetCursorItem], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_player_inventory",
            "A non-empty stack in a numbered slot, for the same reason as the cursor item: the packet is " +
            "a slot index and an item era.",
            [SetPlayerInventory], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_player_team",
            "A team creation carrying every parameter: display name, prefix, suffix, both enum " +
            "parameters, a colour and the option flags, plus two members. 47 spells the visibility and " +
            "collision rules as strings and the colour as a byte, the modern eras use VarInt enums, and " +
            "26.2 reorders the block; a team with no parameters would not reach any of that.",
            [SetPlayerTeam], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Login, PacketFlow.Clientbound, "minecraft:login_finished",
            "A profile with one signed property. The uuid moved from a string to 128 bits, the property " +
            "list arrived at 1.19, and the property's signature is optional; a bare name and uuid " +
            "reaches none of those.",
            [LoginFinished], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:add_mob",
            "A spawn with a uuid, a velocity triple and a metadata list. The uuid arrives at 1.9, the " +
            "trailing metadata list leaves the packet at 1.15, and the type id widens; the metadata " +
            "carries its own era on top.",
            [AddMob, AddMobWithNbt, AddMobLegacy], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:add_player",
            "A player spawn with a held-item id and a metadata list, both of which the packet lost on " +
            "later eras. A spawn with an empty metadata list would decode the same way under two of " +
            "these bands.",
            [AddPlayer, AddPlayerWithNbt, AddPlayerLegacy], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_default_spawn_position",
            "A packed position with a spawn angle, a dimension key and a pitch. The angle arrives at " +
            "1.17, the dimension and the split pitch at 26.2, and the position packing itself changed " +
            "at 1.14; a bare position is identical under several of these.",
            [SetDefaultSpawnPosition], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Login, PacketFlow.Serverbound, "minecraft:hello",
            "A username with a profile id. The id is absent before 1.19.1, optional on 1.19.1 and " +
            "1.19.2, and mandatory from 1.20.2, so carrying one is what separates the three shapes.",
            [Hello]),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:player_chat",
            "A message with an index, a salt, unsigned content carrying click and hover events, and a " +
            "chat type. The last-seen window, the message index and the signature width all changed " +
            "across the signing eras, and the unsigned content carries the text transport era.",
            [PlayerChat], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:player_position",
            "A teleport with a relative-flag mask, a teleport id and the 1.21.2 delta-movement block. " +
            "The teleport id arrives at 1.9, the dismount bool comes and goes, and 1.21.2 replaced the " +
            "flat position with the position-and-delta record.",
            [PlayerPosition], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:set_equipment",
            "A single equipment slot carrying a non-empty legacy item. 1.16 turned the packet into a " +
            "multi-slot list with a continuation bit on the slot byte, so one slot with real contents " +
            "is read differently on either side of that boundary.",
            [SetEquipment], PreferCorpus: true),
    ];

    /// <summary>The packets tranche one reached for and could not finish. Each is bound, each has an authored payload, and each has a handful of adjacent bands that payload cannot separate, so they are named here rather than shipped with a witness that proves less than it looks like it proves.</summary>
    /// <remarks>
    /// <para>The unproven pairs, by first protocol: container_click 770/771, 771/773, 773/774, 774/775 and 775/776; level_particles 766/767, 770/771, 773/774 and 774/775; explode 770/771, 773/774 and 774/775; login 768/775; map_item_data 765/770; open_screen 765/768 and 768/770; player_info_update 765/768 and 769/770; recipe_book_add 770/771; server_data 766/770; and serverbound chat 770/775. Twenty-one pairs in all.</para>
    /// <para>They share one shape: the two codecs are the same body handed a different table, and the table is one the packet's payload does not reach. A component id that rides the wire as a hash, a particle whose option bytes are captured verbatim, a menu type stored as a raw number. That is not proof they are twins, which is why none of them is declared one: it is proof that no payload THIS author found separates them, and the honest place for that is a list of what is still owed rather than an allowlist that reads as an answer.</para>
    /// </remarks>
    internal static IReadOnlyList<WitnessedPacket> Deferred { get; } =
    [
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:container_click",
            "A click with a state id, a clicked stack, a changed-slot list and a carried stack. The " +
            "action-number-versus-state-id swap at 1.17.1, the changed-slot list itself, and the item " +
            "stack's own era all land in these bytes; a click on an empty slot exercises none of them.",
            [ContainerClick]),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:level_particles",
            "A long-form position and a real count, and a second payload carrying particle options. The " +
            "position widens from float to double, the always-show flag arrives separately from the " +
            "limiter, and the particle id moves from an int to a VarInt registry holder. The second " +
            "value exists because 1.8 reads a fixed run of VarInt arguments where 1.13 reads the frame " +
            "remainder: the two can only disagree about a payload the older reader stops short of.",
            [LevelParticlesTrailDuration, LevelParticlesTrail, LevelParticlesTransitionWide, LevelParticlesTransitionNarrow,
                LevelParticlesDustWide, LevelParticlesDustNarrow, LevelParticlesWithOptions, LevelParticles],
            PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:login",
            "A full spawn-info block plus the legacy flat fields. The dimension list, the view and " +
            "simulation distances, the limited-crafting flag, the sea level and the enforces-secure-chat " +
            "flag each arrive on a different release, so this packet forks fourteen times and every fork " +
            "is a field this value carries.",
            [JoinGame], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:explode",
            "A blast with a legacy affected-block list, a knockback vector, a sound holder carrying an " +
            "inline name and a fixed range, and a block-interaction id. The 1.20.5 rewrite replaced the " +
            "list-and-motion form with the particle-and-sound form, and the inline sound name is the " +
            "field that separates the holder eras.",
            [ExplodeTrailDuration, ExplodeTrail, ExplodeTransitionWide, ExplodeTransitionNarrow,
                ExplodeDustWide, ExplodeDustNarrow, Explode],
            PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:recipe_book_add",
            "One shapeless entry whose result is an item-stack slot display rather than a bare item id. " +
            "The stack display carries the item era into the recipe tree, which is the only thing that " +
            "moves across several of these bands.",
            [RecipeBookAdd], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:map_item_data",
            "A locked map with a scale, an icon carrying a display name, and a real colour patch. The " +
            "icon list became optional, the icon type became a VarInt and gained a display name, and the " +
            "tracking-position bool comes and goes; a map with no icons and no patch shows none of it.",
            [MapItemData], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:server_data",
            "A motd carrying click and hover events, plus icon bytes. The icon moved from a base64 " +
            "string to a byte array and then to an optional, and the motd carries the text transport and " +
            "interaction eras with it.",
            [ServerData], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:chat",
            "Plain chat text, which is the whole payload on the unsigned bands and the signed prefix on " +
            "the rest. The signing block that follows it is what the eras disagree about, and a message " +
            "of real length makes the length prefix readable in the byte count too.",
            [Chat]),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:open_screen",
            "A window with a title carrying click and hover events, a menu type and the legacy slot " +
            "count and entity id. The menu type moved from a string to a VarInt registry id at 1.14 and " +
            "the title carries the text transport era.",
            [OpenScreen], PreferCorpus: true),
        new WitnessedPacket(
            ProtocolPhase.Play, PacketFlow.Clientbound, "minecraft:player_info_update",
            "Five actions at once on one entry: a profile with a property, a game mode, a listed flag, a " +
            "latency and a display name. The action set is a fixed bit set whose width grows with the " +
            "actions, and the list-order and show-hat actions arrive later than the rest.",
            [PlayerInfoUpdate], PreferCorpus: true),
    ];
}
