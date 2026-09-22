using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// How one era puts a chat component on the wire, as the reader and writer that do it. There are three instances rather than two flags because two INDEPENDENT boundaries meet here and the combination they produce is what a codec closes over: the transport moves from a JSON string to network NBT at 1.20.3, and the interaction dialect moves from <c>clickEvent</c>/<c>hoverEvent</c> to <c>click_event</c>/<c>hover_event</c> two releases later at 1.21.5. Separate era values prevent the 765-769 range from being decoded with the 1.21.5 dialect and losing a hovered entity's uuid.
/// <para>The halves are the core element delegates rather than bespoke ones, so a pair can also be handed straight to <see cref="PacketReader.ReadOptional{T}"/> / <see cref="PacketWriter.WriteOptional{T}"/> for the packets whose component is optional (player-info display name, resource-pack prompt).</para>
/// </summary>
/// <param name="Read">The era's component reader.</param>
/// <param name="Write">The era's component writer, which must be the reader's inverse.</param>
/// <param name="Form">The transport and dialect this pair speaks, named once here. The halves are delegates and their method names are C# names, which is exactly what a shape token must not be derived from.</param>
internal readonly record struct ComponentWire(ReaderFunc<Component> Read, WriterAction<Component> Write, string Form)
{
    /// <summary>47-764: an object-form JSON string with legacy interactions.</summary>
    internal static ComponentWire V1_8 { get; } = new(UiCodecShared.ReadJson, UiCodecShared.WriteJson, "json");

    /// <summary>765-769: network NBT (root tag or string), legacy interactions.</summary>
    internal static ComponentWire V1_20_3 { get; } =
        new(UiCodecShared.ReadNbtText, UiCodecShared.WriteNbtText, "nbt/legacy");

    /// <summary>770+: network NBT, modern interactions.</summary>
    internal static ComponentWire V1_21_5 { get; } =
        new(UiCodecShared.ReadModernComponent, UiCodecShared.WriteModernComponent, "nbt/modern");

    /// <inheritdoc />
    public override string ToString() => Form;
}

/// <summary>Shared helpers for the UI-family codecs: component / identifier / number-format read-write, player-info and profile field helpers, advancement node/display serialization, chat builders, and the team/name-tag wire enums. Modern play components serialize as network NBT (modern era); 1.8 components are JSON strings (legacy era); those era values are baked into each codec at construction.</summary>
internal static class UiCodecShared
{
    internal const int MaxComponentBytes = 262144;

    internal const NbtWireFormat ModernNbt = NbtWireFormat.JavaRootTagOrString;

    // Modern component read/write helpers (network NBT, modern era).
    internal static Component ReadModernComponent(ref PacketReader r) => r.ReadComponent(ComponentWireEra.Modern, ModernNbt);

    internal static void WriteModernComponent(ref PacketWriter w, Component c) => w.WriteComponent(c, ComponentWireEra.Modern, ModernNbt);

    // Legacy JSON component read/write helpers (1.8 era).
    internal static Component ReadLegacyComponent(ref PacketReader r) => ComponentJson.Parse(r.ReadString(MaxComponentBytes), ComponentWireEra.Legacy);

    // Every caller is bound at protocol 764 or below, where components use the object-form JSON literal.
    internal static void WriteLegacyComponent(ref PacketWriter w, Component c) =>
        w.WriteString(ComponentJson.ToJsonString(c, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), MaxComponentBytes);

    // Namespaced identifiers use one UTF-8 string.
    internal static Identifier ReadIdentifier(ref PacketReader r) => Identifier.Parse(r.ReadString());

    internal static void WriteIdentifier(ref PacketWriter w, Identifier id) => w.WriteString(id.ToString());

    /// <summary>The custom-click payload has a 65536-byte limit in both directions.</summary>
    internal const int CustomClickPayloadLimit = 65536;

    // custom_click_action body, shared by the play-phase and configuration-phase codecs.
    //
    // The play and configuration phases use the same custom-click body and share its framing here.

    internal static void WriteCustomClickAction(ref PacketWriter w, Identifier id, NbtTag? payload)
    {
        WriteIdentifier(ref w, id);

        var scratch = new System.Buffers.ArrayBufferWriter<byte>();
        var body = new PacketWriter(scratch);
        body.WriteNbt(payload ?? NbtEnd.Instance, NbtWireFormat.JavaUnnamedRoot);
        if (scratch.WrittenCount > CustomClickPayloadLimit)
            throw new ProtocolViolationException(
                $"A custom-click payload of {scratch.WrittenCount} bytes exceeds the {CustomClickPayloadLimit}-byte limit.");

        w.WriteByteArray(scratch.WrittenSpan);
    }

    internal static (Identifier Id, NbtTag? Payload) ReadCustomClickAction(ref PacketReader r)
    {
        Identifier id = ReadIdentifier(ref r);
        int length = r.ReadVarInt();
        if (length < 0 || length > CustomClickPayloadLimit)
            throw new ProtocolViolationException(
                $"A custom-click payload length of {length} is outside the 0..{CustomClickPayloadLimit} range.");

        var body = new PacketReader(r.ReadBytes(length));
        NbtTag tag = body.ReadNbt(NbtWireFormat.JavaUnnamedRoot);
        if (body.Remaining != 0)
            throw new ProtocolViolationException(
                $"A custom-click payload left {body.Remaining} unread byte(s) inside its length prefix.");

        return (id, tag is NbtEnd ? null : tag);
    }

    // Number format (1.20.3+): optional registry-dispatched score number format.

    // The FIXED arm carries a component, so a number format inherits the interaction dialect of the protocol carrying it. The number format arrived at 1.20.3 (765); the dialect only modernizes at 1.21.5 (770). So 765-769 write clickEvent/hoverEvent here and 770+ write click_event/hover_event. BLANK and STYLED formats, and FIXED formats containing plain text, are identical under both dialects. The era is still required because FIXED components may carry interactions.
    internal static ScoreNumberFormat? ReadOptionalNumberFormat(ref PacketReader r, ComponentWireEra era)
    {
        if (!r.ReadBool())
            return null;

        int typeId = r.ReadVarInt();
        return typeId switch
        {
            0 => new ScoreNumberFormat(ScoreNumberFormatKind.Blank, null, null),
            1 => new ScoreNumberFormat(ScoreNumberFormatKind.Styled, r.ReadNbt(NbtWireFormat.JavaUnnamedRoot), null),
            2 => new ScoreNumberFormat(ScoreNumberFormatKind.Fixed, null, r.ReadComponent(era, ModernNbt)),
            _ => throw new ProtocolViolationException($"Unknown score number-format type id {typeId}."),
        };
    }

    internal static void WriteOptionalNumberFormat(ref PacketWriter w, ScoreNumberFormat? format, ComponentWireEra era)
    {
        if (format is null)
        {
            w.WriteBool(false);
            return;
        }

        w.WriteBool(true);
        w.WriteVarInt((int)format.Kind);
        switch (format.Kind)
        {
            case ScoreNumberFormatKind.Blank:
                break;
            case ScoreNumberFormatKind.Styled:
                w.WriteNbt(format.StyledStyle ?? throw new ProtocolViolationException("A styled number format must carry a style tag."), NbtWireFormat.JavaUnnamedRoot);
                break;
            case ScoreNumberFormatKind.Fixed:
                w.WriteComponent(
                    format.FixedContent ?? throw new ProtocolViolationException("A fixed number format must carry a component."),
                    era,
                    ModernNbt);
                break;
            default:
                throw new ProtocolViolationException($"Unknown score number-format kind {format.Kind}.");
        }
    }

    /// <summary>The set-player-team parameter-block wire eras. The FIELD ORDER is the same from 1.14 to 1.21.5 (display name, options, name-tag visibility, collision rule, color, prefix, suffix); what moves is how the three components are encoded and how the two rule enums are encoded.</summary>
    internal enum TeamWire
    {
        /// <summary>477-764 (1.14-1.20.2): JSON-string components, name-tag visibility and collision rule as STRINGS.</summary>
        V1_14,

        /// <summary>765-769 (1.20.3-1.21.4): network-NBT components with legacy interactions, rules still STRINGS.</summary>
        V1_20_3,

        /// <summary>770-775 (1.21.5-26.1): modern-interaction components, rules as VarInt enum ids.</summary>
        V1_21_5,

        /// <summary>776 (26.2): reordered parameters and an optional color id.</summary>
        V26_2,
    }

    /// <summary>Builds the set-player-team codec for one wire era.</summary>
    /// <remarks>
    /// <para>Through protocol 769, the two rules are UTF-8 strings limited to 40 bytes (<c>always</c> / <c>never</c> / <c>hideForOtherTeams</c> / <c>hideForOwnTeam</c> and <c>always</c> / <c>never</c> / <c>pushOtherTeams</c> / <c>pushOwnTeam</c>); protocol 770 replaced them with VarInt ids in the same 0-3 ordering. Components move separately: JSON strings through protocol 764, network NBT from 765, and the click/hover interaction shapes modernize at 1.21.5.</para>
    /// <para>These boundaries are independent. Reading the protocol-770 VarInt rule before that boundary consumes the legacy string length as an enum value and misaligns the remaining team fields.</para>
    /// </remarks>
    internal static PacketCodec<ClientboundSetPlayerTeamPacket> Team(TeamWire wire) =>
        PacketCodec<ClientboundSetPlayerTeamPacket>.Of(
            (ref PacketWriter w, ClientboundSetPlayerTeamPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Name);
                w.WriteByte((byte)p.Method);
                if (p.Method is TeamMethod.Add or TeamMethod.Change)
                {
                    TeamParameters prm = p.Parameters ?? throw new ProtocolViolationException("Add/change team requires parameters.");
                    if (wire == TeamWire.V26_2)
                    {
                        WriteModernComponent(ref w, prm.DisplayName);
                        WriteModernComponent(ref w, prm.Prefix);
                        WriteModernComponent(ref w, prm.Suffix);
                        w.WriteVarInt((int)prm.NameTagVisibility);
                        w.WriteVarInt((int)prm.CollisionRule);
                        w.WriteOptionalStruct(prm.Color, static (ref PacketWriter sw, int c) => sw.WriteVarInt(c));
                        w.WriteByte(prm.Options);
                    }
                    else
                    {
                        WriteTeamComponent(ref w, wire, prm.DisplayName);
                        w.WriteByte(prm.Options);
                        if (wire == TeamWire.V1_21_5)
                        {
                            w.WriteVarInt((int)prm.NameTagVisibility);
                            w.WriteVarInt((int)prm.CollisionRule);
                        }
                        else
                        {
                            w.WriteString(NameTagVisibilityWire(prm.NameTagVisibility), TeamRuleMaxChars);
                            w.WriteString(CollisionRuleWire(prm.CollisionRule), TeamRuleMaxChars);
                        }

                        w.WriteVarInt(prm.Color ?? throw new ProtocolViolationException("A pre-26.2 team color is a mandatory ChatFormatting id."));
                        WriteTeamComponent(ref w, wire, prm.Prefix);
                        WriteTeamComponent(ref w, wire, prm.Suffix);
                    }
                }

                if (p.Method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers)
                    w.WriteList(p.Players, static (ref PacketWriter sw, string s) => sw.WriteString(s));

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString();
                var method = (TeamMethod)r.ReadByte();
                TeamParameters? prm = null;
                if (method is TeamMethod.Add or TeamMethod.Change)
                    if (wire == TeamWire.V26_2)
                    {
                        Component display = ReadModernComponent(ref r);
                        Component prefix = ReadModernComponent(ref r);
                        Component suffix = ReadModernComponent(ref r);
                        var vis = (NameTagVisibility)r.ReadVarInt();
                        var col = (CollisionRule)r.ReadVarInt();
                        int? color = r.ReadBool() ? r.ReadVarInt() : null;
                        byte options = r.ReadByte();
                        prm = new TeamParameters(display, prefix, suffix, vis, col, color, options);
                    }
                    else
                    {
                        Component display = ReadTeamComponent(ref r, wire);
                        byte options = r.ReadByte();
                        NameTagVisibility vis;
                        CollisionRule col;
                        if (wire == TeamWire.V1_21_5)
                        {
                            vis = (NameTagVisibility)r.ReadVarInt();
                            col = (CollisionRule)r.ReadVarInt();
                        }
                        else
                        {
                            vis = NameTagVisibilityFromWire(r.ReadString(TeamRuleMaxChars));
                            col = CollisionRuleFromWire(r.ReadString(TeamRuleMaxChars));
                        }

                        int color = r.ReadVarInt();
                        Component prefix = ReadTeamComponent(ref r, wire);
                        Component suffix = ReadTeamComponent(ref r, wire);
                        prm = new TeamParameters(display, prefix, suffix, vis, col, color, options);
                    }

                string[] players = method is TeamMethod.Add or TeamMethod.AddPlayers or TeamMethod.RemovePlayers
                    ? r.ReadList(static (ref PacketReader sr) => sr.ReadString())
                    : [];
                return new ClientboundSetPlayerTeamPacket(name, method, prm, players);
            },
            WireShape.OfEra("set_player_team", wire));

    /// <summary>Both team-rule strings are length-prefixed UTF-8 with a 40-character limit.</summary>
    private const int TeamRuleMaxChars = 40;

    private static Component ReadTeamComponent(ref PacketReader r, TeamWire wire) => wire switch
    {
        TeamWire.V1_14 => ReadJson(ref r),
        TeamWire.V1_20_3 => ReadNbtText(ref r),
        _ => ReadModernComponent(ref r),
    };

    private static void WriteTeamComponent(ref PacketWriter w, TeamWire wire, Component c)
    {
        switch (wire)
        {
            case TeamWire.V1_14:
                WriteJson(ref w, c);
                break;
            case TeamWire.V1_20_3:
                WriteNbtText(ref w, c);
                break;
            default:
                WriteModernComponent(ref w, c);
                break;
        }
    }

    internal static string NameTagVisibilityWire(NameTagVisibility v) => v switch
    {
        NameTagVisibility.Always => "always",
        NameTagVisibility.Never => "never",
        NameTagVisibility.HideForOtherTeams => "hideForOtherTeams",
        NameTagVisibility.HideForOwnTeam => "hideForOwnTeam",
        _ => "always",
    };

    internal static NameTagVisibility NameTagVisibilityFromWire(string s) => s switch
    {
        "never" => NameTagVisibility.Never,
        "hideForOtherTeams" => NameTagVisibility.HideForOtherTeams,
        "hideForOwnTeam" => NameTagVisibility.HideForOwnTeam,
        _ => NameTagVisibility.Always,
    };

    // The collision-rule wire strings vanilla used from 1.9 (when the field arrived) to 1.21.4, before 1.21.5 replaced them with a VarInt ordinal. 1.9 spells them always / never / pushOtherTeams / pushOwnTeam with ordinals 0-3.
    internal static string CollisionRuleWire(CollisionRule rule) => rule switch
    {
        CollisionRule.Always => "always",
        CollisionRule.Never => "never",
        CollisionRule.PushOtherTeams => "pushOtherTeams",
        CollisionRule.PushOwnTeam => "pushOwnTeam",
        _ => "always",
    };

    internal static CollisionRule CollisionRuleFromWire(string s) => s switch
    {
        "never" => CollisionRule.Never,
        "pushOtherTeams" => CollisionRule.PushOtherTeams,
        "pushOwnTeam" => CollisionRule.PushOwnTeam,
        _ => CollisionRule.Always,
    };

    // Player list

    internal static readonly PlayerInfoActions[] ActionOrder =
    [
        PlayerInfoActions.AddPlayer,
        PlayerInfoActions.InitializeChat,
        PlayerInfoActions.UpdateGameMode,
        PlayerInfoActions.UpdateListed,
        PlayerInfoActions.UpdateLatency,
        PlayerInfoActions.UpdateDisplayName,
        PlayerInfoActions.UpdateListOrder,
        PlayerInfoActions.UpdateHat,
    ];

    internal static void WritePlayerInfoField(ref PacketWriter ew, PlayerInfoActions action, PlayerInfoEntry entry, WriterAction<Component> writeText)
    {
        switch (action)
        {
            case PlayerInfoActions.AddPlayer:
                ew.WriteString(entry.Name ?? throw new ProtocolViolationException("Add-player requires a name."), 16);
                ew.WriteList(entry.Properties ?? [], WriteProfileProperty);
                break;
            case PlayerInfoActions.InitializeChat:
                ew.WriteBool(entry.HasChatSession);
                if (entry.HasChatSession)
                    WriteRemoteChatSession(ref ew, entry.ChatSession ?? throw new ProtocolViolationException("Initialize-chat with session flag set requires session data."));

                break;
            case PlayerInfoActions.UpdateGameMode:
                ew.WriteVarInt((int)entry.GameMode);
                break;
            case PlayerInfoActions.UpdateListed:
                ew.WriteBool(entry.Listed);
                break;
            case PlayerInfoActions.UpdateLatency:
                ew.WriteVarInt(entry.Latency);
                break;
            case PlayerInfoActions.UpdateDisplayName:
                ew.WriteOptional(entry.DisplayName, writeText);
                break;
            case PlayerInfoActions.UpdateListOrder:
                ew.WriteVarInt(entry.ListOrder);
                break;
            case PlayerInfoActions.UpdateHat:
                ew.WriteBool(entry.ShowHat);
                break;
            default:
                break;
        }
    }

    internal static GameProfileProperty ReadProfileProperty(ref PacketReader r)
    {
        string name = r.ReadString();
        string value = r.ReadString();
        string? sig = r.ReadBool() ? r.ReadString() : null;
        return new GameProfileProperty(name, value, sig);
    }

    internal static void WriteProfileProperty(ref PacketWriter w, GameProfileProperty prop)
    {
        w.WriteString(prop.Name);
        w.WriteString(prop.Value);
        w.WriteOptional(prop.Signature, static (ref PacketWriter sw, string s) => sw.WriteString(s));
    }

    internal static RemoteChatSession ReadRemoteChatSession(ref PacketReader r)
    {
        Guid sessionId = r.ReadUuid();
        long expires = r.ReadLong();
        byte[] key = r.ReadByteArray().ToArray();
        byte[] sig = r.ReadByteArray().ToArray();
        return new RemoteChatSession(sessionId, expires, key, sig);
    }

    internal static void WriteRemoteChatSession(ref PacketWriter w, RemoteChatSession s)
    {
        w.WriteUuid(s.SessionId);
        w.WriteLong(s.ExpiresAtMillis);
        w.WriteByteArray(s.PublicKey);
        w.WriteByteArray(s.KeySignature);
    }

    /// <summary>The per-era shape knobs of update_advancements. The outer packet body (reset, added, removed, progress) and the advancement-display field order have been constant since 1.12; everything that varies is captured here. <see cref="AdvancementCodecs"/> owns the era timeline.</summary>
    /// <param name="Icon">The advancement icon's era item-stack strategy.</param>
    /// <param name="Text">The era encoding of the title/description components.</param>
    /// <param name="HasCriteria">True on 1.12-1.20.1 (335-763), where the node carries the criterion-name list.</param>
    /// <param name="HasTelemetry">True from 1.20 (763), where the node gained the telemetry bool.</param>
    /// <param name="HasShowAdvancements">True from 1.21.5 (770), where the packet gained the trailing show-advancements bool. When false the bool is neither written nor read and decode surfaces ShowAdvancements as true (those versions always show).</param>
    /// <param name="HasPositions">True from 26.3 (777), where each added element carries trailing Float x and Float y after the node. When false the floats are neither written nor read.</param>
    /// <param name="HasDisplayCoordinates">True through 26.2 (776), where DisplayInfo carries its own trailing Float x and Float y. From 26.3 the tab position rides only on the outer added element and the inner floats are neither written nor read.</param>
    internal readonly record struct AdvancementWireShape(
        AdvancementIconStrategy Icon,
        ComponentWire Text,
        bool HasCriteria,
        bool HasTelemetry,
        bool HasShowAdvancements,
        bool HasPositions = false,
        bool HasDisplayCoordinates = true)
    {
        /// <inheritdoc />
        public override string ToString()
        {
            string form =
                $"icon={Icon},text={Text},criteria={(HasCriteria ? 1 : 0)}," +
                $"telemetry={(HasTelemetry ? 1 : 0)},showadv={(HasShowAdvancements ? 1 : 0)}";
            return HasPositions || !HasDisplayCoordinates
                ? $"{form},positions={(HasPositions ? 1 : 0)},displaycoords={(HasDisplayCoordinates ? 1 : 0)}"
                : form;
        }
    }

    /// <summary>The advancement icon's era item-stack read/write pair.</summary>
    /// <param name="Read">The era's icon reader.</param>
    /// <param name="Write">The era's icon writer.</param>
    /// <param name="Form">What the pair reads, named here rather than derived from two delegate names.</param>
    internal readonly record struct AdvancementIconStrategy(
        ItemPacketCodecShared.StackReader Read,
        ItemPacketCodecShared.StackWriter Write,
        string Form)
    {
        /// <inheritdoc />
        public override string ToString() => Form;
    }

    /// <summary>The pre-1.20.5 icon strategies, by the era of the item Slot wire form.</summary>
    internal static AdvancementIconStrategy IconLegacySlot { get; } =
        new(ItemStackCodecs.ReadLegacyStack, ItemStackCodecs.WriteLegacyStack, "legacy");

    /// <summary>The 1.13/1.13.1 short-id icon slot.</summary>
    internal static AdvancementIconStrategy IconShortIdSlot { get; } =
        new(ItemStackCodecs.ReadShortIdStack, ItemStackCodecs.WriteShortIdStack, "shortid");

    /// <summary>The 1.13.2-1.20.1 present-flag icon slot (named NBT root).</summary>
    internal static AdvancementIconStrategy IconPresentIdSlot { get; } =
        new(ItemStackCodecs.ReadPresentIdStack, ItemStackCodecs.WritePresentIdStack, "presentid");

    /// <summary>The 1.20.2-1.20.4 present-flag icon slot (unnamed NBT root).</summary>
    internal static AdvancementIconStrategy IconVarIntIdSlot { get; } =
        new(ItemStackCodecs.ReadVarIntIdStack, ItemStackCodecs.WriteVarIntIdStack, "varintid");

    /// <summary>The 1.20.5-1.21.11 component icon stack for a given component-id era table.</summary>
    internal static AdvancementIconStrategy IconComponents(ItemComponentTable table) =>
        new(
            (ref PacketReader r, PacketCodecContext c) => ItemStackCodecs.ReadModernStack(ref r, c, table),
            (ref PacketWriter w, ItemStack s, PacketCodecContext c) => ItemStackCodecs.WriteModernStack(ref w, s, c, table),
            "countfirst/" + table.ShapeToken);

    /// <summary>The 26.1+ icon uses the template form: the item holder id first, then a VarInt count, and no empty sentinel. Earlier eras use the count-first stack form. Nothing else in the display-info payload moved, so this is the ONLY reason 775 and 776 need their own icon strategy.</summary>
    /// <param name="table">The protocol's component era table.</param>
    /// <returns>The strategy.</returns>
    internal static AdvancementIconStrategy IconTemplateComponents(ItemComponentTable table) =>
        new(
            (ref PacketReader r, PacketCodecContext c) => ItemStackCodecs.ReadTemplateStack(ref r, c, table),
            (ref PacketWriter w, ItemStack s, PacketCodecContext c) => ItemStackCodecs.WriteTemplateStack(ref w, s, c, table),
            "template/" + table.ShapeToken);

    // Update-advancements body shared by every era; the shape knobs live in AdvancementWireShape.
    internal static PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancements(AdvancementWireShape shape) =>
        PacketCodec<ClientboundUpdateAdvancementsPacket>.Of(
            (ref PacketWriter w, ClientboundUpdateAdvancementsPacket p, PacketCodecContext ctx) =>
            {
                w.WriteBool(p.Reset);
                w.WriteList(p.Added, (ref PacketWriter aw, AdvancementEntry e) =>
                {
                    WriteIdentifier(ref aw, e.Id);
                    WriteAdvancementNode(ref aw, e.Value, shape, ctx);
                    if (shape.HasPositions)
                    {
                        if (e.PositionX is not float x || e.PositionY is not float y)
                            throw new ProtocolViolationException(
                                "26.3 update_advancements requires each added element's position; a positionless entry has no wire form on this era.");

                        aw.WriteFloat(x);
                        aw.WriteFloat(y);
                    }
                });
                w.WriteList(p.Removed, WriteIdentifier);
                w.WriteList(p.Progress, static (ref PacketWriter pw, AdvancementProgressEntry e) =>
                {
                    WriteIdentifier(ref pw, e.Id);
                    pw.WriteList(e.Criteria, static (ref PacketWriter cw, CriterionProgressEntry c) =>
                    {
                        cw.WriteString(c.CriterionId);
                        cw.WriteOptionalStruct(c.ObtainedEpochMillis, static (ref PacketWriter ow, long ms) => ow.WriteLong(ms));
                    });
                });
                if (shape.HasShowAdvancements)
                    w.WriteBool(p.ShowAdvancements);

            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                bool reset = r.ReadBool();
                AdvancementEntry[] added = r.ReadList((ref PacketReader ar) =>
                {
                    Identifier id = ReadIdentifier(ref ar);
                    AdvancementNode node = ReadAdvancementNode(ref ar, shape, ctx);
                    float? x = shape.HasPositions ? ar.ReadFloat() : null;
                    float? y = shape.HasPositions ? ar.ReadFloat() : null;
                    return new AdvancementEntry(id, node) { PositionX = x, PositionY = y };
                });
                Identifier[] removed = r.ReadList(static (ref PacketReader rr) => ReadIdentifier(ref rr));
                AdvancementProgressEntry[] progress = r.ReadList(static (ref PacketReader pr) =>
                {
                    Identifier id = ReadIdentifier(ref pr);
                    CriterionProgressEntry[] criteria = pr.ReadList(static (ref PacketReader cr) =>
                    {
                        string criterionId = cr.ReadString();
                        long? obtained = cr.ReadOptionalStruct(static (ref PacketReader or) => or.ReadLong());
                        return new CriterionProgressEntry(criterionId, obtained);
                    });
                    return new AdvancementProgressEntry(id, criteria);
                });
                bool show = !shape.HasShowAdvancements || r.ReadBool();
                return new ClientboundUpdateAdvancementsPacket(reset, added, removed, progress, show);
            },
            WireShape.OfEra("update_advancements", shape));

    internal static void WriteAdvancementNode(ref PacketWriter w, AdvancementNode node, AdvancementWireShape shape, PacketCodecContext ctx)
    {
        ArgumentNullException.ThrowIfNull(node);
        w.WriteOptionalStruct(node.Parent, WriteIdentifier);
        w.WriteOptional(node.Display, (ref PacketWriter dw, AdvancementDisplayInfo d) => WriteAdvancementDisplayInfo(ref dw, d, shape, ctx));

        // Criterion map (1.12-1.20.1): a VarInt-counted list of names, each with an empty body.
        if (shape.HasCriteria)
            w.WriteList(node.Criteria, static (ref PacketWriter cw, string s) => cw.WriteString(s));

        // AdvancementRequirements: list of list of criterion-name strings.
        w.WriteList(node.Requirements, static (ref PacketWriter rw, IReadOnlyList<string> group) =>
            rw.WriteList(group, static (ref PacketWriter gw, string s) => gw.WriteString(s)));
        if (shape.HasTelemetry)
            w.WriteBool(node.SendsTelemetryEvent);

    }

    internal static AdvancementNode ReadAdvancementNode(ref PacketReader r, AdvancementWireShape shape, PacketCodecContext ctx)
    {
        Identifier? parent = r.ReadBool() ? ReadIdentifier(ref r) : null;
        AdvancementDisplayInfo? display = r.ReadBool() ? ReadAdvancementDisplayInfo(ref r, shape, ctx) : null;
        IReadOnlyList<string> criteria = shape.HasCriteria
            ? r.ReadList(static (ref PacketReader cr) => cr.ReadString())
            : [];
        IReadOnlyList<string>[] requirements = r.ReadList(static (ref PacketReader rr) =>
            (IReadOnlyList<string>)rr.ReadList(static (ref PacketReader gr) => gr.ReadString()));
        bool telemetry = shape.HasTelemetry && r.ReadBool();
        return new AdvancementNode(parent, display, criteria, requirements, telemetry);
    }

    internal static void WriteAdvancementDisplayInfo(ref PacketWriter w, AdvancementDisplayInfo d, AdvancementWireShape shape, PacketCodecContext ctx)
    {
        ArgumentNullException.ThrowIfNull(d);
        shape.Text.Write(ref w, d.Title);
        shape.Text.Write(ref w, d.Description);
        shape.Icon.Write(ref w, d.Icon, ctx);
        w.WriteVarInt((int)d.Frame);

        int flags = 0;
        if (d.Background is not null) flags |= 1;
        if (d.ShowToast) flags |= 2;
        if (d.Hidden) flags |= 4;
        w.WriteInt(flags);

        if (d.Background is { } background)
            WriteIdentifier(ref w, background);

        if (shape.HasDisplayCoordinates)
        {
            w.WriteFloat(d.X);
            w.WriteFloat(d.Y);
        }
    }

    internal static AdvancementDisplayInfo ReadAdvancementDisplayInfo(ref PacketReader r, AdvancementWireShape shape, PacketCodecContext ctx)
    {
        Component title = shape.Text.Read(ref r);
        Component description = shape.Text.Read(ref r);
        ItemStack icon = shape.Icon.Read(ref r, ctx);
        var frame = (AdvancementFrameType)r.ReadVarInt();
        int flags = r.ReadInt();
        Identifier? background = (flags & 1) != 0 ? ReadIdentifier(ref r) : null;
        bool showToast = (flags & 2) != 0;
        bool hidden = (flags & 4) != 0;
        float x = shape.HasDisplayCoordinates ? r.ReadFloat() : 0f;
        float y = shape.HasDisplayCoordinates ? r.ReadFloat() : 0f;
        return new AdvancementDisplayInfo(title, description, icon, frame, background, showToast, hidden, x, y);
    }

    // The 1.21.2 seven-action ordinal order (no UpdateHat).
    internal static readonly PlayerInfoActions[] ActionOrderV1_21_2 =
    [
        PlayerInfoActions.AddPlayer,
        PlayerInfoActions.InitializeChat,
        PlayerInfoActions.UpdateGameMode,
        PlayerInfoActions.UpdateListed,
        PlayerInfoActions.UpdateLatency,
        PlayerInfoActions.UpdateDisplayName,
        PlayerInfoActions.UpdateListOrder,
    ];

    /// <summary>The per-entry action set one era's player-info-update carries. The order array and the hat flag are one fact, not two: 1.21.2 has seven actions and no <c>UPDATE_HAT</c>, and 1.21.4 has eight with it, so an order array from one era beside the flag from the other writes bit 7 into a frame that has no field for it.</summary>
    /// <param name="Order">The actions in wire order.</param>
    /// <param name="HasHat">Whether <c>UPDATE_HAT</c> (bit 7) exists on this era.</param>
    internal readonly record struct PlayerInfoWire(PlayerInfoActions[] Order, bool HasHat)
    {
        /// <summary>768 (1.21.2/1.21.3): seven actions, no hat.</summary>
        internal static PlayerInfoWire V1_21_2 { get; } = new(ActionOrderV1_21_2, HasHat: false);

        /// <summary>761-767 and 769+: the eight-action order.</summary>
        internal static PlayerInfoWire V1_19_3 { get; } = new(ActionOrder, HasHat: true);

        /// <inheritdoc />
        public override string ToString() => $"{string.Join('+', Order)},hat={(HasHat ? 1 : 0)}";
    }

    // The three component wire forms, in protocol order. A text-carrying packet picks one of these at construction; which one is a function of the protocol alone, never of the packet:
    //
    //   47-764   JSON string
    //   765-769  network NBT
    //   770+     network NBT        same transport, MODERN click/hover interaction shapes
    //
    // The transport boundary (JSON -> NBT) is 1.20.3/765. The INTERACTION boundary is 1.21.5/770 and is independent of it: through 769 the fields are "clickEvent" and "hoverEvent", with a flat "value" field; from 770 they are "click_event" and "hover_event", dispatched on "action". Plain text is identical under both, which is why a codec bound one era out only misbehaves once a component carries an interaction.

    // 47-764: JSON string components, legacy interactions.
    internal static Component ReadJson(ref PacketReader r) =>
        ComponentJson.Parse(r.ReadString(MaxComponentBytes), ComponentWireEra.Legacy);

    internal static void WriteJson(ref PacketWriter w, Component c) =>
        w.WriteString(ComponentJson.ToJsonString(c, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), MaxComponentBytes);

    // 765-769: network NBT (root tag or string), legacy interactions.
    internal static Component ReadNbtText(ref PacketReader r) =>
        r.ReadComponent(ComponentWireEra.Legacy, NbtWireFormat.JavaRootTagOrString);

    internal static void WriteNbtText(ref PacketWriter w, Component c) =>
        w.WriteComponent(c, ComponentWireEra.Legacy, NbtWireFormat.JavaRootTagOrString);

    /// <summary>An era's component read/write pair. These are the core element delegates rather than bespoke ones so the same pair can also be handed straight to <see cref="PacketReader.ReadOptional{T}"/> / <see cref="PacketWriter.WriteOptional{T}"/> for the packets whose component is optional (player-info display name, resource-pack prompt).</summary>
    internal static PacketCodec<ClientboundSystemChatPacket> MakeSystemChat(ComponentWire text) =>
        PacketCodec<ClientboundSystemChatPacket>.Of(
            (ref PacketWriter w, ClientboundSystemChatPacket p, PacketCodecContext _) =>
            {
                text.Write(ref w, p.Content);
                w.WriteBool(p.Overlay);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Component content = text.Read(ref r);
                return new ClientboundSystemChatPacket(content, r.ReadBool());
            },
            WireShape.Of("component,bool", text.Form));

    internal static PacketCodec<ClientboundDisguisedChatPacket> MakeDisguisedChat(ComponentWire text) =>
        PacketCodec<ClientboundDisguisedChatPacket>.Of(
            (ref PacketWriter w, ClientboundDisguisedChatPacket p, PacketCodecContext _) =>
            {
                text.Write(ref w, p.Message);
                w.WriteVarInt(p.ChatTypeId);
                text.Write(ref w, p.SenderName);
                if (p.TargetName is { } target)
                {
                    w.WriteBool(true);
                    text.Write(ref w, target);
                }
                else
                    w.WriteBool(false);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Component message = text.Read(ref r);
                int chatTypeId = r.ReadVarInt();
                Component sender = text.Read(ref r);
                Component? target = r.ReadBool() ? text.Read(ref r) : null;
                return new ClientboundDisguisedChatPacket(message, chatTypeId, sender, target);
            },
            WireShape.Of("component,varint,component,opt_component", text.Form));

    internal static PacketCodec<ClientboundTabListPacket> MakeTabList(ComponentWire text) =>
        PacketCodec<ClientboundTabListPacket>.Of(
            (ref PacketWriter w, ClientboundTabListPacket p, PacketCodecContext _) =>
            {
                text.Write(ref w, p.Header);
                text.Write(ref w, p.Footer);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Component header = text.Read(ref r);
                Component footer = text.Read(ref r);
                return new ClientboundTabListPacket(header, footer);
            },
            WireShape.Of("component,component", text.Form));

    internal static PacketCodec<ClientboundServerDataPacket> MakeServerData(ComponentWire text, bool hasSecureChatBool) =>
        PacketCodec<ClientboundServerDataPacket>.Of(
            (ref PacketWriter w, ClientboundServerDataPacket p, PacketCodecContext _) =>
            {
                text.Write(ref w, p.Motd);
                w.WriteOptional(p.IconBytes, static (ref PacketWriter sw, byte[] b) => sw.WriteByteArray(b));
                if (hasSecureChatBool)
                    w.WriteBool(p.EnforcesSecureChat);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Component motd = text.Read(ref r);
                byte[]? icon = r.ReadOptional(static (ref PacketReader sr) => sr.ReadByteArray().ToArray());
                bool secure = hasSecureChatBool && r.ReadBool();
                return new ClientboundServerDataPacket(motd, icon) { EnforcesSecureChat = secure };
            },
            WireShape.Of(
                hasSecureChatBool ? "component,opt_bytes,bool" : "component,opt_bytes",
                text.Form));
}
