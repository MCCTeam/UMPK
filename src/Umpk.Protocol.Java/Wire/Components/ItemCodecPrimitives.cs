using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Shared low-level reader/writer helpers the item family codecs reuse: registry-id holders, modern network-NBT framing, network components, and structural NBT hashing for the hashed-slot model. Kept in one place so the item stack, component, and container codecs stay small.</summary>
internal static class ItemCodecPrimitives
{
    /// <summary>The modern network NBT root framing (1.20.2+): a type byte then the body, no root name.</summary>
    public const NbtWireFormat NetworkNbt = NbtWireFormat.JavaUnnamedRoot;

    /// <summary>Reads a registry holder id encoded as a plain VarInt.</summary>
    public static int ReadHolderId(ref PacketReader reader) => reader.ReadVarInt();

    /// <summary>Writes a registry holder id as a plain VarInt.</summary>
    public static void WriteHolderId(ref PacketWriter writer, int networkId) => writer.WriteVarInt(networkId);

    /// <summary>Reads a component-era network component. The NBT framing is the same on every component era (1.20.5+ unnamed root); what moves is the INTERACTION dialect, and it moves at 1.21.5: through protocol 769 the fields are <c>clickEvent</c>/<c>hoverEvent</c> with a flat click event; protocol 770 renames them <c>click_event</c>/<c>hover_event</c> and dispatches the click event on <c>action</c>. So 766-769 are <see cref="ComponentWireEra.Legacy"/> and 770+ are <see cref="ComponentWireEra.Modern"/>.</summary>
    /// <param name="reader">The packet reader.</param>
    /// <param name="era">The era's interaction dialect.</param>
    /// <returns>The component.</returns>
    public static Component ReadNetworkComponent(ref PacketReader reader, ComponentWireEra era) =>
        reader.ReadComponent(era, NetworkNbt);

    /// <summary>Writes a component-era network component (see <see cref="ReadNetworkComponent"/>).</summary>
    /// <param name="writer">The packet writer.</param>
    /// <param name="component">The component.</param>
    /// <param name="era">The era's interaction dialect.</param>
    public static void WriteNetworkComponent(ref PacketWriter writer, Component component, ComponentWireEra era) =>
        writer.WriteComponent(component, era, NetworkNbt);

    /// <summary>Resolves an enchantment holder id against the session registry, or a default handle.</summary>
    public static RegistryEntry<EnchantmentDefinition> ResolveEnchantment(PacketCodecContext context, int networkId) =>
        context.Registries.Enchantments.TryGet(networkId, out var entry) ? entry : default;

    /// <summary>Resolves an attribute holder id against the session registry, or a default handle.</summary>
    public static RegistryEntry<AttributeDefinition> ResolveAttribute(PacketCodecContext context, int networkId) =>
        context.Registries.Attributes.TryGet(networkId, out var entry) ? entry : default;

    /// <summary>Hashes an NBT tag through the protocol's structural data model, matching a component whose DATA codec produces NBT is hashed (the ops walks the same tree). Compounds hash as maps of (string-key, value); lists as lists; numeric/string/array leaves as their tagged forms.</summary>
    public static int HashNbt(in HashOps ops, NbtTag tag)
    {
        switch (tag)
        {
            case NbtEnd:
                return ops.Empty;
            case NbtByte b:
                return ops.Byte(b.Value);
            case NbtShort s:
                return ops.Short(s.Value);
            case NbtInt i:
                return ops.Int(i.Value);
            case NbtLong l:
                return ops.Long(l.Value);
            case NbtFloat f:
                return ops.Float(f.Value);
            case NbtDouble d:
                return ops.Double(d.Value);
            case NbtString str:
                return ops.String(str.Value);
            case NbtByteArray ba:
                {
                    var bytes = new byte[ba.Value.Length];
                    for (int k = 0; k < bytes.Length; k++)
                        bytes[k] = unchecked((byte)ba.Value[k]);

                    return ops.ByteList(bytes);
                }

            case NbtIntArray ia:
                return ops.IntList(ia.Value);
            case NbtLongArray la:
                return ops.LongList(la.Value);
            case NbtList list:
                {
                    var hashes = new int[list.Count];
                    for (int k = 0; k < list.Count; k++)
                        hashes[k] = HashNbt(ops, list[k]);

                    return ops.List(hashes);
                }

            case NbtCompound compound:
                {
                    var entries = new List<(int, int)>(compound.Count);
                    foreach (KeyValuePair<string, NbtTag> member in compound)
                        entries.Add((ops.String(member.Key), HashNbt(ops, member.Value)));

                    return ops.Map(entries);
                }

            default:
                return ops.Empty;
        }
    }
}
