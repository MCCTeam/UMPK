using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // 1.20.2-1.21.1 (protocols 764-767) configuration-family era codecs. 764/765 sync registries as ONE unnamed-root NBT blob (the whole registry access); 766+ moved to the per-registry packed-entry form ConfigurationCodecs.RegistryData covers. The start-configuration and configuration-acknowledged pair (both empty) is the Play-to-Configuration re-entry gate present on every configuration-phase version (1.20.2+). The registry payload is NBT, and configuration acknowledgement has no fields and terminates into configuration.

    /// <summary>764/765 registry-data: the whole registry access as one unnamed-root NBT compound.</summary>
    public static PacketCodec<ClientboundConfigRegistryBlobPacket> RegistryBlob { get; } =
        PacketCodec<ClientboundConfigRegistryBlobPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigRegistryBlobPacket p, PacketCodecContext _) =>
                w.WriteNbt(p.Registries, NbtWireFormat.JavaUnnamedRoot),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigRegistryBlobPacket(r.ReadNbt(NbtWireFormat.JavaUnnamedRoot)));

    // Registry data

    private static readonly WriterAction<PackedRegistryEntry> WriteRegistryEntry =
        static (ref PacketWriter w, PackedRegistryEntry e) =>
        {
            w.WriteString(e.Id.ToString());
            if (e.Data is null)
                w.WriteBool(false);

            else
            {
                w.WriteBool(true);
                w.WriteNbt(e.Data, NbtWireFormat.JavaRootTagOrString);
            }
        };

    private static readonly ReaderFunc<PackedRegistryEntry> ReadRegistryEntry =
        static (ref PacketReader r) =>
        {
            Identifier id = Identifier.Parse(r.ReadString());
            NbtTag? data = r.ReadBool() ? r.ReadNbt(NbtWireFormat.JavaRootTagOrString) : null;
            return new PackedRegistryEntry(id, data);
        };

    /// <summary>Configuration registry-data (registry key + packed entries).</summary>
    public static readonly PacketCodec<ClientboundConfigRegistryDataPacket> RegistryData =
        PacketCodec<ClientboundConfigRegistryDataPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigRegistryDataPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Registry.ToString());
                w.WriteList(p.Entries, WriteRegistryEntry);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Identifier registry = Identifier.Parse(r.ReadString());
                return new ClientboundConfigRegistryDataPacket(registry, r.ReadList(ReadRegistryEntry));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRegistryData(PacketBindings bindings)
    {
        // 1.20.2/1.20.4 sync registries as one NBT blob under minecraft:registry_data; 1.20.5 switches the same identifier to per-registry packed entries (a distinct record).
        bindings.Packet(LoginFamilyPackets.Config.RegistryBlob)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.RegistryBlob)
            .FromAs(JavaEras.ItemComponents, LoginFamilyPackets.Config.RegistryData, ConfigurationCodecs.RegistryData);
    }
}
