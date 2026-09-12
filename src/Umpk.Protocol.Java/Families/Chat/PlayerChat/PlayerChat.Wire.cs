using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCodecs
{
    private const int SignatureBytes = 256;

    private const int MaxComponentBytes = 262144;

    /// <summary>The v3 packed last-seen list is capped at 20 entries. Reading an uncapped VarInt count here would let a hostile or broken server make this side allocate an arbitrarily large array before the entries are even parsed; a client-observed field, not a signature-material one, so mirroring the cap costs nothing.</summary>
    private const int LastSeenMaxLength = 20;

    /// <summary>The v2 (1.19.1/1.19.2) window cap, matching <c>Umpk.Protocol.Java.Signing.LastSeenMessagesCollector.Window1_19</c> rather than <see cref="LastSeenMaxLength"/>: v2's own window size is 5 entries, not v3's later 20-entry cache window. Kept as a local constant (not a reference to that type) so this wire-decode layer does not take a dependency on <c>Signing</c>, which the rest of <c>Codecs/</c> deliberately does not either.</summary>
    private const int V2LastSeenMaxLength = 5;

    /// <summary>The partially filtered wire value, the only mask type carrying a trailing bitset.</summary>
    private const int PartiallyFilteredMask = 2;

    // shared helpers

    private static void WriteJsonComponent(ref PacketWriter w, Component c) =>
        w.WriteString(ComponentJson.ToJsonString(c, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), MaxComponentBytes);

    private static Component ReadJsonComponent(ref PacketReader r) =>
        ComponentJson.Parse(r.ReadString(MaxComponentBytes), ComponentWireEra.Legacy);

    private static void WriteChatType(ref PacketWriter w, ClientboundPlayerChatPacket p, WriterAction<Component> component)
    {
        // Protocols 761-765 write a plain registry index. From 766 onward the registry-reference form is still one VarInt (id + 1), so carrying the value verbatim preserves both wire forms. The inline form (id 0) is not modeled because this field uses registry references.
        w.WriteVarInt(p.ChatTypeId);
        component(ref w, p.SenderName);
        w.WriteBool(p.TargetName is not null);
        if (p.TargetName is not null)
            component(ref w, p.TargetName);

    }

    private static (int chatTypeId, Component name, Component? target) ReadChatType(ref PacketReader r, ReaderFunc<Component> component)
    {
        int id = r.ReadVarInt();
        Component name = component(ref r);
        Component? target = r.ReadBool() ? component(ref r) : null;
        return (id, name, target);
    }

    // The v3 last-seen collection writes each entry as a VarInt id plus one. Zero means a full 256-byte signature follows; other values reference the connection signature cache.
    private static void WriteLastSeen(ref PacketWriter w, IReadOnlyList<PackedMessageSignature> lastSeen)
    {
        w.WriteVarInt(lastSeen.Count);
        foreach (PackedMessageSignature entry in lastSeen)
        {
            w.WriteVarInt(entry.Id + 1);
            if (entry.Id != PackedMessageSignature.FullSignatureId)
                continue;

            byte[] sig = entry.FullSignature
                ?? throw new ProtocolViolationException("A full-signature last-seen entry requires signature bytes.");
            if (sig.Length != SignatureBytes)
                throw new ProtocolViolationException("A chat signature must be exactly 256 bytes.");

            w.WriteBytes(sig);
        }
    }

    private static PackedMessageSignature[] ReadLastSeen(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        if (count < 0 || count > LastSeenMaxLength)
            throw new ProtocolViolationException(
                $"A v3 last-seen list carried {count} entries, exceeding vanilla's {LastSeenMaxLength}-entry cap.");

        var list = new PackedMessageSignature[count];
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadVarInt() - 1;
            list[i] = id == PackedMessageSignature.FullSignatureId
                ? PackedMessageSignature.Full(r.ReadBytes(SignatureBytes).ToArray())
                : PackedMessageSignature.Cached(id);
        }
        return list;
    }

    // A filter mask is a VarInt type followed, for partially filtered (2), by a VarInt-length array of longs. Reading only the type would leave the bitset in the buffer and desync later fields.
    private static void WriteFilter(ref PacketWriter w, int type, IReadOnlyList<long> bits)
    {
        w.WriteVarInt(type);
        if (type != PartiallyFilteredMask)
            return;

        w.WriteVarInt(bits.Count);
        foreach (long word in bits)
            w.WriteLong(word);

    }

    private static long[] ReadFilter(ref PacketReader r, int type)
    {
        if (type != PartiallyFilteredMask)
            return [];

        int count = r.ReadVarInt();
        var words = new long[count];
        for (int i = 0; i < count; i++)
            words[i] = r.ReadLong();

        return words;
    }

    /// <summary>Builds a v3 inbound player-chat codec. The v3 body is frozen from 761 to 776: <c>[globalIndex]</c> + uuid sender + varint index + nullable 256-byte signature + signed body (utf content + long instant + long salt + packed last-seen) + nullable unsigned content + filter mask + bound chat type. Only two things vary: the component encoding and whether the leading globalIndex is present.</summary>
    private static PacketCodec<ClientboundPlayerChatPacket> V3(
        ComponentWire text, bool hasGlobalIndex) =>
        PacketCodec<ClientboundPlayerChatPacket>.Of(
            (ref PacketWriter w, ClientboundPlayerChatPacket p, PacketCodecContext _) =>
            {
                if (hasGlobalIndex)
                    w.WriteVarInt(p.GlobalIndex);

                w.WriteUuid(p.Sender);
                w.WriteVarInt(p.Index);
                w.WriteBool(p.Signature is not null);
                if (p.Signature is not null)
                    w.WriteBytes(p.Signature);

                w.WriteString(p.SignedContent ?? string.Empty, MaxContentChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteLastSeen(ref w, p.LastSeen);
                w.WriteBool(p.UnsignedContent is not null);
                if (p.UnsignedContent is not null)
                    text.Write(ref w, p.UnsignedContent);

                WriteFilter(ref w, p.FilterType, p.FilterBits);
                WriteChatType(ref w, p, text.Write);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int globalIndex = hasGlobalIndex ? r.ReadVarInt() : 0;
                Guid sender = r.ReadUuid();
                int index = r.ReadVarInt();
                byte[]? sig = r.ReadBool() ? r.ReadBytes(SignatureBytes).ToArray() : null;
                string content = r.ReadString(MaxContentChars);
                long ts = r.ReadLong();
                long salt = r.ReadLong();
                PackedMessageSignature[] lastSeen = ReadLastSeen(ref r);
                Component? unsigned = r.ReadBool() ? text.Read(ref r) : null;
                int filterType = r.ReadVarInt();
                long[] filterBits = ReadFilter(ref r, filterType);
                (int chatTypeId, Component name, Component? target) = ReadChatType(ref r, text.Read);
                return new ClientboundPlayerChatPacket(sender, index, sig, content, ts, salt, unsigned, chatTypeId, name, target)
                {
                    GlobalIndex = globalIndex,
                    LastSeen = lastSeen,
                    FilterType = filterType,
                    FilterBits = filterBits,
                };
            },
            WireShape.Of(
                hasGlobalIndex
                    ? "varint,uuid,varint,opt_signature,string,long,long,last_seen,opt_component,filter,chat_type"
                    : "uuid,varint,opt_signature,string,long,long,last_seen,opt_component,filter,chat_type",
                text.Form));

    /// <summary>v3, 761-764 (1.19.3 - 1.20.2): JSON string components, no globalIndex. 1.20.2 kept the 1.19.3 body verbatim; components move to network NBT only at 765, exactly as system_chat and disguised_chat model the same boundary.</summary>
    public static readonly PacketCodec<ClientboundPlayerChatPacket> V1_19_3 =
        V3(ComponentWire.V1_8, hasGlobalIndex: false);

    /// <summary>v3, 765-769 (1.20.3 - 1.21.4): network NBT components with legacy interactions, no globalIndex. At 766 the chat-type field becomes a registry reference, whose wire form is still the same single VarInt, so the byte layout is unchanged across the span.</summary>
    public static readonly PacketCodec<ClientboundPlayerChatPacket> V1_20_3 =
        V3(ComponentWire.V1_20_3, hasGlobalIndex: false);

    /// <summary>v3, 770-776 (1.21.5 - 26.2): a leading <c>globalIndex</c> VarInt plus modern-interaction NBT components. The layout is unchanged through 26.2, so one codec covers the whole band.</summary>
    public static readonly PacketCodec<ClientboundPlayerChatPacket> V1_21_5 =
        V3(ComponentWire.V1_21_5, hasGlobalIndex: true);

    // v1: 1.19.0 (759) signed content component + optional unsigned content + varint typeId + sender(uuid+name+optional target) + long timestamp + salt-signature pair (long salt + byte-array signature).
    public static readonly PacketCodec<ClientboundPlayerChatPacket> V1_19 =
        PacketCodec<ClientboundPlayerChatPacket>.Of(
            (ref PacketWriter w, ClientboundPlayerChatPacket p, PacketCodecContext _) =>
            {
                WriteJsonComponent(ref w, p.SignedComponent ?? Component.Text(p.SignedContent ?? string.Empty));
                w.WriteBool(p.UnsignedContent is not null);
                if (p.UnsignedContent is not null)
                    WriteJsonComponent(ref w, p.UnsignedContent);

                w.WriteVarInt(p.ChatTypeId);
                w.WriteUuid(p.Sender);
                WriteJsonComponent(ref w, p.SenderName);
                w.WriteBool(p.TargetName is not null);
                if (p.TargetName is not null)
                    WriteJsonComponent(ref w, p.TargetName);

                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                w.WriteByteArray(p.Signature ?? []);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                Component signed = ReadJsonComponent(ref r);
                Component? unsigned = r.ReadBool() ? ReadJsonComponent(ref r) : null;
                int chatTypeId = r.ReadVarInt();
                Guid sender = r.ReadUuid();
                Component name = ReadJsonComponent(ref r);
                Component? target = r.ReadBool() ? ReadJsonComponent(ref r) : null;
                long ts = r.ReadLong();
                long salt = r.ReadLong();
                byte[] sig = r.ReadByteArray().ToArray();
                return new ClientboundPlayerChatPacket(sender, 0, sig, null, ts, salt, unsigned, chatTypeId, name, target)
                {
                    SignedComponent = signed,
                };
            });

    // The v2 body has a signed header (nullable previous-signature + uuid sender) + header signature (byte-array) + signed body + long timestamp + long salt + last-seen collection + optional unsigned content + filter mask, then chat-type BoundNetwork. The signed body on 1.19.1/1.19.2 is a PLAIN string (the raw message text) followed by an optional formatted JSON component, not the single JSON component used by 1.19.0.
    public static readonly PacketCodec<ClientboundPlayerChatPacket> V1_19_1 =
        PacketCodec<ClientboundPlayerChatPacket>.Of(
            (ref PacketWriter w, ClientboundPlayerChatPacket p, PacketCodecContext _) =>
            {
                // signed header
                w.WriteBool(p.PreviousSignature is not null);
                if (p.PreviousSignature is not null)
                    w.WriteByteArray(p.PreviousSignature);

                w.WriteUuid(p.Sender);
                // header signature
                w.WriteByteArray(p.Signature ?? []);
                // signed body: plain message + optional formatted component
                w.WriteString(p.SignedContent ?? string.Empty, MaxContentChars);
                w.WriteBool(p.SignedComponent is not null || p.SignedComponentJson is not null);
                if (p.SignedComponentJson is { } rawDecorated)
                {
                    // Round trip the bytes the decoder kept rather than re-serialising the parsed model: this field is signature material and the frame has to survive a decode/encode pair unchanged.
                    w.WriteString(rawDecorated, MaxComponentBytes);
                }
                else if (p.SignedComponent is not null)
                    WriteJsonComponent(ref w, p.SignedComponent);

                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                WriteLastSeenV2(ref w, p.LastSeen);
                // unsigned + filter
                w.WriteBool(p.UnsignedContent is not null);
                if (p.UnsignedContent is not null)
                    WriteJsonComponent(ref w, p.UnsignedContent);

                WriteFilter(ref w, p.FilterType, p.FilterBits);
                WriteChatType(ref w, p, WriteJsonComponent);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                byte[]? prev = r.ReadBool() ? r.ReadByteArray().ToArray() : null;
                Guid sender = r.ReadUuid();
                byte[] headerSig = r.ReadByteArray().ToArray();
                string plain = r.ReadString(MaxContentChars);
                // The decorated component is kept as its RAW JSON as well as parsed. It is folded into the signed body digest (vanilla SignedMessageBody.hash writes Component.Serializer.toStableJson of it between the 0x46 separator and the last-seen entries), and that digest is over the JsonElement tree this text encodes, so re-serialising the parsed model would be a different tree and a failed signature.
                string? formattedJson = null;
                Component? formatted = null;
                if (r.ReadBool())
                {
                    formattedJson = r.ReadString(MaxComponentBytes);
                    formatted = ComponentJson.Parse(formattedJson, ComponentWireEra.Legacy);
                }

                long ts = r.ReadLong();
                long salt = r.ReadLong();
                PackedMessageSignature[] lastSeen = ReadLastSeenV2(ref r);
                Component? unsigned = r.ReadBool() ? ReadJsonComponent(ref r) : null;
                int filterType = r.ReadVarInt();
                long[] filterBits = ReadFilter(ref r, filterType);
                (int chatTypeId, Component name, Component? target) = ReadChatType(ref r, ReadJsonComponent);
                return new ClientboundPlayerChatPacket(sender, 0, headerSig, plain, ts, salt, unsigned, chatTypeId, name, target)
                {
                    SignedComponent = formatted,
                    SignedComponentJson = formattedJson,
                    PreviousSignature = prev,
                    LastSeen = lastSeen,
                    FilterType = filterType,
                    FilterBits = filterBits,
                };
            });

    // v2 last-seen (LastSeenMessages, capped at 5 entries): a VarInt count, then per entry the acknowledged message's SENDER profile id and its full signature. There is no packing on this generation, so every entry is a full signature.
    //
    // The per-entry uuid is carried, not synthesized. Each entry's own uuid is part of the signed body before its signature bytes, so substituting another uuid changes the digest and breaks verification.
    private static void WriteLastSeenV2(ref PacketWriter w, IReadOnlyList<PackedMessageSignature> lastSeen)
    {
        w.WriteVarInt(lastSeen.Count);
        foreach (PackedMessageSignature entry in lastSeen)
        {
            w.WriteUuid(entry.ProfileId);
            w.WriteByteArray(entry.FullSignature
                ?? throw new ProtocolViolationException("A 1.19.1 last-seen entry requires signature bytes."));
        }
    }

    private static PackedMessageSignature[] ReadLastSeenV2(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        if (count < 0 || count > V2LastSeenMaxLength)
            throw new ProtocolViolationException(
                $"A v2 last-seen window carried {count} entries, exceeding vanilla's {V2LastSeenMaxLength}-entry cap.");

        var list = new PackedMessageSignature[count];
        for (int i = 0; i < count; i++)
        {
            Guid profileId = r.ReadUuid();
            list[i] = PackedMessageSignature.Full(profileId, r.ReadByteArray().ToArray());
        }
        return list;
    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerChat(PacketBindings bindings)
    {
        // Inbound signed player_chat has v1, v2, and v3 signing generations. The v3 body is stable from 761 to 776; only the component encoding (JSON to network NBT at 765, legacy to modern interactions at 770) and the leading globalIndex VarInt (added at 770) vary.
        bindings.Packet(UiPackets.Clientbound.PlayerChat)
            .From(JavaProtocols.V1_19, ChatCodecs.V1_19)
            .From(JavaProtocols.V1_19_1, ChatCodecs.V1_19_1)
            .From(JavaProtocols.V1_19_3, ChatCodecs.V1_19_3)
            .From(JavaProtocols.V1_20_3, ChatCodecs.V1_20_3)
            .From(JavaProtocols.V1_21_5, ChatCodecs.V1_21_5);
    }
}
