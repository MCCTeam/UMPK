namespace Umpk.Protocol.Java.Codecs;

/// <summary>A decoded particle: its registry type id and the raw type-specific option bytes, captured verbatim so the payload re-encodes byte-for-byte. The option bytes are the wire content that follows the particle-type id (empty for the vast majority of particle types). Structured decoding of block / dust / item options is layered on top of this by callers; the wire codec keeps the raw form so that item-carrying particles round-trip without the inventory sibling's item codec (the brief's raw-bytes fallback), and so that unknown modded particles pass through frame-exact.</summary>
/// <param name="TypeId">The particle registry id on modern protocols or the legacy particle id on 1.8.</param>
/// <param name="Options">The raw type-specific option bytes that follow the id (never null; may be empty).</param>
public readonly record struct ParticleData(int TypeId, byte[] Options);

/// <summary>The layout family of a particle type's option payload. Used to compute how many bytes to consume after the type id so the raw-options capture stays frame-exact. Only the types that carry options need an entry; every other id resolves to <see cref="ParticleOptionShape.None"/>.</summary>
public enum ParticleOptionShape
{
    /// <summary>No option bytes follow the type id.</summary>
    None = 0,

    /// <summary>A single VarInt (block-state id, shriek delay, and similar).</summary>
    VarInt = 1,

    /// <summary>A single big-endian int (entity_effect / tinted_leaves color).</summary>
    Int = 2,

    /// <summary>A single float (sculk_charge roll).</summary>
    Float = 3,

    /// <summary>An int color plus a float scale (dust).</summary>
    DustColor = 4,

    /// <summary>Two int colors plus a float scale (dust_color_transition).</summary>
    DustTransition = 5,

    /// <summary>A trail: double target x/y/z, an int color, and a VarInt duration.</summary>
    Trail = 6,

    /// <summary>A vibration: a position source (block or entity) plus a VarInt arrival time.</summary>
    Vibration = 7,

    /// <summary>A count-first item stack on protocols 766-774.</summary>
    Item = 8,

    /// <summary>A big-endian integer followed by a float, used by spell effects and the 26.x geyser family.</summary>
    IntFloat = 9,

    /// <summary>A three-float color vector plus a float scale: the 16-byte <c>dust</c> payload on protocols 766-767. Protocol 768 replaced the vector with a four-byte packed integer, which is <see cref="DustColor"/> (8 bytes).</summary>
    Vec3fFloat = 10,

    /// <summary>Two three-float color vectors plus a float scale: the 1.20.5-1.21.1 <c>dust_color_transition</c> payload (28 bytes), replaced by the two-int <see cref="DustTransition"/> form (12 bytes) at protocol 768.</summary>
    Vec3fVec3fFloat = 11,

    /// <summary>A trail without the duration VarInt: the target vector and packed color used on protocol 768. Protocol 769 appended the duration VarInt represented by <see cref="Trail"/>.</summary>
    TrailNoDuration = 12,

    /// <summary>A 26.1+ nested item-stack TEMPLATE (item holder id first, VarInt count, no empty sentinel). Earlier eras use the count-first stack form.</summary>
    ItemTemplate = 13,
}

/// <summary>Reads and writes the particle option payload. The reader captures the raw option bytes for a given type id using an era-specific shape table; the writer emits the type id then the captured bytes. The reader never itself terminates the packet: it consumes exactly the option bytes for the type, which is what lets particles appear mid-payload (explosions) as well as last (level particles).</summary>
public static class ParticleCodec
{
    // Two things move independently and BOTH are era axes here:
    //
    //   1. the particle_type registry ORDERING, which is the generated ParticleIds table for the era.
    //   2. the option payload: dust and dust_color_transition swapped three-float colors for
    //      ints at 1.21.2; trail gained its duration VarInt at 1.21.4; effect, instant_effect,
    //      dragon_breath and flash gained payloads at 1.21.9; the geyser family arrived at 26.2; and the
    //      item particle changed to the template-stack form at 26.1.
    //
    // The payload tables below are keyed by registry NAME because the name is the stable half: ids shift whenever a type is inserted, the payload changes only when the option class does. Binding a neighbouring era's table therefore mis-reads the payload in one of two ways: it dispatches on the wrong particle (an ordering error) or it consumes the wrong number of bytes for the right one (a payload error). Both desynchronize the rest of the frame. Types absent from a table carry no options on that era.

    /// <summary>766/767 (1.20.5-1.21.1). Vector3f dust colours; no trail, block_crumble or tinted_leaves yet.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV1_20_5 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dust", ParticleOptionShape.Vec3fFloat),
        ("minecraft:dust_color_transition", ParticleOptionShape.Vec3fVec3fFloat),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:item", ParticleOptionShape.Item),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
    ];

    /// <summary>768 (1.21.2/1.21.3). Int dust colours, block_crumble arrives, trail has no duration.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV1_21_2 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:item", ParticleOptionShape.Item),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.TrailNoDuration),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    /// <summary>769 (1.21.4). Its own ids, and trail gains the duration VarInt.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV1_21_4 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:item", ParticleOptionShape.Item),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.Trail),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    /// <summary>770-772 (1.21.5-1.21.8). Its own ids, and tinted_leaves arrives.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV1_21_5 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:tinted_leaves", ParticleOptionShape.Int),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:item", ParticleOptionShape.Item),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.Trail),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    /// <summary>773/774 (1.21.9-1.21.11). effect, instant_effect, dragon_breath and flash gain payloads.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV1_21_9 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dragon_breath", ParticleOptionShape.Float),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:effect", ParticleOptionShape.IntFloat),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:tinted_leaves", ParticleOptionShape.Int),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:flash", ParticleOptionShape.Int),
        ("minecraft:instant_effect", ParticleOptionShape.IntFloat),
        ("minecraft:item", ParticleOptionShape.Item),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.Trail),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    /// <summary>775 (26.1). Its own ids, and the item particle uses the template-stack form.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV26_1 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:dragon_breath", ParticleOptionShape.Float),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:effect", ParticleOptionShape.IntFloat),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:tinted_leaves", ParticleOptionShape.Int),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:flash", ParticleOptionShape.Int),
        ("minecraft:instant_effect", ParticleOptionShape.IntFloat),
        ("minecraft:item", ParticleOptionShape.ItemTemplate),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.Trail),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    /// <summary>776 (26.2). Its own ids, plus the four geyser types.</summary>
    private static readonly (string Name, ParticleOptionShape Shape)[] PayloadsV26_2 =
    [
        ("minecraft:block", ParticleOptionShape.VarInt),
        ("minecraft:block_marker", ParticleOptionShape.VarInt),
        ("minecraft:geyser", ParticleOptionShape.Int),
        ("minecraft:geyser_base", ParticleOptionShape.IntFloat),
        ("minecraft:geyser_poof", ParticleOptionShape.IntFloat),
        ("minecraft:geyser_plume", ParticleOptionShape.Int),
        ("minecraft:dragon_breath", ParticleOptionShape.Float),
        ("minecraft:dust", ParticleOptionShape.DustColor),
        ("minecraft:dust_color_transition", ParticleOptionShape.DustTransition),
        ("minecraft:effect", ParticleOptionShape.IntFloat),
        ("minecraft:entity_effect", ParticleOptionShape.Int),
        ("minecraft:falling_dust", ParticleOptionShape.VarInt),
        ("minecraft:tinted_leaves", ParticleOptionShape.Int),
        ("minecraft:sculk_charge", ParticleOptionShape.Float),
        ("minecraft:flash", ParticleOptionShape.Int),
        ("minecraft:instant_effect", ParticleOptionShape.IntFloat),
        ("minecraft:item", ParticleOptionShape.ItemTemplate),
        ("minecraft:vibration", ParticleOptionShape.Vibration),
        ("minecraft:trail", ParticleOptionShape.Trail),
        ("minecraft:shriek", ParticleOptionShape.VarInt),
        ("minecraft:dust_pillar", ParticleOptionShape.VarInt),
        ("minecraft:block_crumble", ParticleOptionShape.VarInt),
    ];

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV1_20_5 =
        ResolveShapes(ParticleIds.V1_20_5, PayloadsV1_20_5);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV1_21_2 =
        ResolveShapes(ParticleIds.V1_21_2, PayloadsV1_21_2);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV1_21_4 =
        ResolveShapes(ParticleIds.V1_21_4, PayloadsV1_21_4);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV1_21_5 =
        ResolveShapes(ParticleIds.V1_21_5, PayloadsV1_21_5);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV1_21_9 =
        ResolveShapes(ParticleIds.V1_21_9, PayloadsV1_21_9);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV26_1 =
        ResolveShapes(ParticleIds.V26_1, PayloadsV26_1);

    private static readonly IReadOnlyDictionary<int, ParticleOptionShape> ShapesV26_2 =
        ResolveShapes(ParticleIds.V26_2, PayloadsV26_2);

    /// <summary>Joins an era's payload table onto its generated id ordering. A payload named for a type the era does not have is a hard failure rather than a dropped entry: a missing shape reads zero option bytes for a type that carries some, which desynchronizes every byte behind it.</summary>
    private static IReadOnlyDictionary<int, ParticleOptionShape> ResolveShapes(
        string[] ids, (string Name, ParticleOptionShape Shape)[] payloads)
    {
        Dictionary<int, ParticleOptionShape> shapes = new(payloads.Length);
        foreach ((string name, ParticleOptionShape shape) in payloads)
        {
            int id = Array.IndexOf(ids, name);
            if (id < 0)
                throw new InvalidOperationException(
                    $"Particle type {name} carries an option payload but is not in this era's particle_type registry.");

            shapes[id] = shape;
        }

        return shapes;
    }

    /// <summary>The 1.20.5/1.21 (protocols 766/767) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV1_20_5 => ShapesV1_20_5;

    /// <summary>The 1.21.2/1.21.3 (protocol 768) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV1_21_2 => ShapesV1_21_2;

    /// <summary>The 1.21.4 (protocol 769) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV1_21_4 => ShapesV1_21_4;

    /// <summary>The 1.21.5-1.21.8 (protocols 770-772) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV1_21_5 => ShapesV1_21_5;

    /// <summary>The 1.21.9-1.21.11 (protocols 773/774) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV1_21_9 => ShapesV1_21_9;

    /// <summary>The 26.1 (protocol 775) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV26_1 => ShapesV26_1;

    /// <summary>The 26.2 (protocol 776) particle option shape table.</summary>
    public static IReadOnlyDictionary<int, ParticleOptionShape> ModernV26_2 => ShapesV26_2;

    /// <summary>Reads a modern particle: a VarInt type id, then the type-specific options captured verbatim. The shape table tells the reader how many option bytes the type carries. Item particles read a full modern <c>ItemStack</c> (era-selected via <paramref name="icons"/>, including data components) and re-serialize it into the captured option bytes so the payload still round-trips byte-exactly.</summary>
    internal static ParticleData ReadModern(
        ref PacketReader r, IReadOnlyDictionary<int, ParticleOptionShape> shapes, ItemComponentTable icons, PacketCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(context);
        int typeId = r.ReadVarInt();
        ParticleOptionShape shape = shapes.TryGetValue(typeId, out ParticleOptionShape s) ? s : ParticleOptionShape.None;
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var echo = new PacketWriter(buffer);
        CopyModernOptions(ref r, ref echo, shape, icons, context);
        return new ParticleData(typeId, buffer.WrittenSpan.ToArray());
    }

    /// <summary>Writes a modern particle: the VarInt type id, then the raw option bytes verbatim.</summary>
    public static void WriteModern(ref PacketWriter w, ParticleData particle)
    {
        w.WriteVarInt(particle.TypeId);
        w.WriteBytes(particle.Options ?? []);
    }

    /// <summary>The number of trailing VarInt arguments a 1.8 particle id carries. The count is not on the wire iconcrack (36) carries two, blockcrack (37) carries one, blockdust (38) = 1, everything else 0.</summary>
    private static int LegacyArgumentCount(int typeId) => typeId switch
    {
        36 => 2,
        37 => 1,
        38 => 1,
        _ => 0,
    };

    /// <summary>Reads a 1.8 particle id (a signed int) plus its fixed count of trailing VarInt arguments, captured verbatim into <see cref="ParticleData.Options"/>. The int id is not part of the options.</summary>
    public static ParticleData ReadLegacy(ref PacketReader r)
    {
        int typeId = r.ReadInt();
        int args = LegacyArgumentCount(typeId);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var echo = new PacketWriter(buffer);
        for (int i = 0; i < args; i++)
            echo.WriteVarInt(r.ReadVarInt());

        return new ParticleData(typeId, buffer.WrittenSpan.ToArray());
    }

    /// <summary>Writes a 1.8 particle: the signed-int id then the captured trailing-argument bytes.</summary>
    public static void WriteLegacy(ref PacketWriter w, ParticleData particle)
    {
        w.WriteInt(particle.TypeId);
        w.WriteBytes(particle.Options ?? []);
    }

    // Reads each option primitive from the payload and echoes it into a side buffer, so the caller captures the exact option bytes without a back-slice on the reader.
    private static void CopyModernOptions(
        ref PacketReader r, ref PacketWriter echo, ParticleOptionShape shape, ItemComponentTable icons, PacketCodecContext context)
    {
        switch (shape)
        {
            case ParticleOptionShape.None:
                break;
            case ParticleOptionShape.VarInt:
                echo.WriteVarInt(r.ReadVarInt());
                break;
            case ParticleOptionShape.Int:
                echo.WriteInt(r.ReadInt());
                break;
            case ParticleOptionShape.Float:
                echo.WriteFloat(r.ReadFloat());
                break;
            case ParticleOptionShape.IntFloat:
                echo.WriteInt(r.ReadInt());     // color / water_blocks
                echo.WriteFloat(r.ReadFloat()); // power / burst_impulse_base
                break;
            case ParticleOptionShape.Vec3fFloat:
                echo.WriteFloat(r.ReadFloat()); // colour r
                echo.WriteFloat(r.ReadFloat()); // colour g
                echo.WriteFloat(r.ReadFloat()); // colour b
                echo.WriteFloat(r.ReadFloat()); // scale
                break;
            case ParticleOptionShape.Vec3fVec3fFloat:
                for (int i = 0; i < 6; i++)
                {
                    echo.WriteFloat(r.ReadFloat()); // from r/g/b then to r/g/b
                }

                echo.WriteFloat(r.ReadFloat()); // scale
                break;
            case ParticleOptionShape.TrailNoDuration:
                echo.WriteDouble(r.ReadDouble()); // target x
                echo.WriteDouble(r.ReadDouble()); // target y
                echo.WriteDouble(r.ReadDouble()); // target z
                echo.WriteInt(r.ReadInt());       // color
                break;
            case ParticleOptionShape.DustColor:
                echo.WriteInt(r.ReadInt());     // rgb
                echo.WriteFloat(r.ReadFloat()); // scale
                break;
            case ParticleOptionShape.DustTransition:
                echo.WriteInt(r.ReadInt());     // from
                echo.WriteInt(r.ReadInt());     // to
                echo.WriteFloat(r.ReadFloat()); // scale
                break;
            case ParticleOptionShape.Trail:
                echo.WriteDouble(r.ReadDouble()); // target x
                echo.WriteDouble(r.ReadDouble()); // target y
                echo.WriteDouble(r.ReadDouble()); // target z
                echo.WriteInt(r.ReadInt());       // color
                echo.WriteVarInt(r.ReadVarInt()); // duration
                break;
            case ParticleOptionShape.Vibration:
                CopyVibration(ref r, ref echo);
                break;
            case ParticleOptionShape.Item:
                CopyItem(ref r, ref echo, icons, context);
                break;
            case ParticleOptionShape.ItemTemplate:
                CopyItemTemplate(ref r, ref echo, icons, context);
                break;
            default:
                throw new ProtocolViolationException($"Unhandled particle option shape {shape}.");
        }
    }

    // Vibration: a position source dispatched by its own registry id (0 = packed block position;
    // 1 = entity source: entity-id VarInt + yOffset float), then the arrival-in-ticks VarInt.
    private static void CopyVibration(ref PacketReader r, ref PacketWriter echo)
    {
        int sourceType = r.ReadVarInt();
        echo.WriteVarInt(sourceType);
        switch (sourceType)
        {
            case 0:
                echo.WriteLong(r.ReadLong()); // block position (packed)
                break;
            case 1:
                echo.WriteVarInt(r.ReadVarInt()); // entity id
                echo.WriteFloat(r.ReadFloat());   // y offset
                break;
            default:
                throw new ProtocolViolationException($"Unknown vibration position-source type {sourceType}.");
        }

        echo.WriteVarInt(r.ReadVarInt()); // arrival in ticks
    }

    // An item particle carries a full modern item stack decoded through the era-selected component table. Re-serialize the decoded stack into the echo buffer so ParticleData.Options remains byte-exact.
    private static void CopyItem(ref PacketReader r, ref PacketWriter echo, ItemComponentTable icons, PacketCodecContext context)
    {
        Umpk.Game.Items.ItemStack stack = ItemStackCodecs.ReadModernStack(ref r, context, icons);
        ItemStackCodecs.WriteModernStack(ref echo, stack, context, icons);
    }

    // The 26.1+ item particle uses the template stack form rather than the earlier count-first form. The two swap their first two fields and neither announces itself, so reading one with the other's codec silently produces a different item and count and then walks the patch from the wrong offset.
    private static void CopyItemTemplate(ref PacketReader r, ref PacketWriter echo, ItemComponentTable icons, PacketCodecContext context)
    {
        Umpk.Game.Items.ItemStack stack = ItemStackCodecs.ReadTemplateStack(ref r, context, icons);
        ItemStackCodecs.WriteTemplateStack(ref echo, stack, context, icons);
    }
}
