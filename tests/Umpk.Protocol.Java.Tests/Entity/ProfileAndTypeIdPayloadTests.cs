using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>The three item components 1.21.9 re-shaped: <c>minecraft:profile</c>, <c>minecraft:entity_data</c> and <c>minecraft:block_entity_data</c>. Protocols 773, 774 and 775 left them UNTYPED (an honest gap), but 776 bound the 1.21.5 codecs to them outright, so a player head or a spawn egg on 26.2 was read with the wrong payload and desynchronized the rest of the component list.</summary>
/// <remarks>
/// <para>The component definitions are stable across these eras:</para>
/// <list type="bullet">
/// <item><c>PROFILE</c> changed from optional name, optional UUID, and properties to a leading branch
/// discriminator, a uuid-then-name branch with both fields mandatory, and four optional patch fields.</item>
/// <item><c>ENTITY_DATA</c> and <c>BLOCK_ENTITY_DATA</c> gained a leading VarInt registry id before the
/// compound tag.</item>
/// <item><c>BUCKET_ENTITY_DATA</c> did NOT move and remains a plain compound tag through 26.2, which is
/// why it is pinned here as the control.</item>
/// </list>
/// <para>771 and 773 share one component id ORDERING (both use the 770 id table), and all three components sit at the same wire id on both, so every 771-versus-773 assertion below isolates the PAYLOAD axis with nothing else moving. The 776 assertions separately require its payload shapes to agree with 773.</para>
/// </remarks>
public class ProfileAndTypeIdPayloadTests
{
    private const string SetSlot = "minecraft:container_set_slot";

    private const int P771 = 771;   // 1.21.6-1.21.8: the last 1.21.5-payload era
    private const int P773 = 773;   // 1.21.9/1.21.10: the re-shaped payloads
    private const int P776 = 776;   // 26.2: the era that was still on the 1.21.5 codecs

    // container_set_slot from 1.21.2 on, with one-byte header values: the first component wire id is at offset 8 and its payload starts at 9. See ItemStackTemplateBindingTests for the field-by-field layout.
    private const int ComponentIdOffset = 8;
    private const int PayloadOffset = ComponentIdOffset + 1;

    private static readonly Guid ProfileId = new("11111111-2222-3333-4444-555555555555");

    private static byte[] EncodeSlot(int protocol, DataComponentMap components) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot).Encode(
            new ClientboundContainerSetSlotPacket(
                ContainerId: 1,
                StateId: 0,
                Slot: 0,
                Item: new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.FilledMap), 1, components)));

    private static byte[] EncodeSlot<T>(int protocol, DataComponentType<T> key, T value)
        where T : class =>
        EncodeSlot(protocol, DataComponentMap.Empty.With(key, value));

    // 1. minecraft:profile

    private static ProfileComponent PartialProfile { get; } =
        new(new ResolvableProfile("p", ProfileId, []));

    /// <summary>The 1.21.9 form is exactly FIVE bytes wider than the 1.21.5 one for the same partial profile: one leading branch discriminator bool plus the four optional-absent bools of the trailing skin patch. The component's wire id is 61 on BOTH 771 and 773, so the whole difference is payload.</summary>
    [Fact]
    public void Profile_1_21_9FormIsFiveBytesWiderThanThe1_21_5Form()
    {
        byte[] legacy = EncodeSlot(P771, DataComponents.Profile, PartialProfile);
        byte[] modern = EncodeSlot(P773, DataComponents.Profile, PartialProfile);

        Assert.Equal(61, legacy[ComponentIdOffset]);
        Assert.Equal(61, modern[ComponentIdOffset]);
        Assert.Equal(legacy.Length + 5, modern.Length);

        // The discriminator is false for the partial branch and true for the resolved profile branch.
        Assert.Equal(0, modern[PayloadOffset]);

        // The 1.21.5 payload starts straight at the optional-name bool.
        Assert.Equal(1, legacy[PayloadOffset]);
    }

    /// <summary>Protocol 776 must carry the 1.21.9 profile payload rather than the 1.21.5 form. The profile wire id is a single byte on both eras (61 on 773, 70 on 776), so the frame widths are directly comparable.</summary>
    [Fact]
    public void Profile_776UsesThe1_21_9Form()
    {
        int legacy = EncodeSlot(P771, DataComponents.Profile, PartialProfile).Length;
        int modern = EncodeSlot(P773, DataComponents.Profile, PartialProfile).Length;
        byte[] frame776 = EncodeSlot(P776, DataComponents.Profile, PartialProfile);

        Assert.Equal(70, frame776[ComponentIdOffset]);
        Assert.Equal(modern, frame776.Length);
        Assert.NotEqual(legacy, frame776.Length);
        Assert.Equal(0, frame776[PayloadOffset]);
    }

    /// <summary>The LEFT branch is a <c>GameProfile</c>: uuid FIRST, then a mandatory name, with no optional bools at all. That is the reverse field order of the partial branch, so a resolved head read through the 1.21.5 codec takes the uuid's first byte as the name-present flag. Pinned by width (16 + 2 for the name, against the partial branch's 1 + 3 + 1 + 16) and by the discriminator byte.</summary>
    [Fact]
    public void Profile_ResolvedBranchIsUuidFirstAndMandatory()
    {
        var resolved = new ProfileComponent(new ResolvableProfile("p", ProfileId, [], Resolved: true));

        byte[] frame = EncodeSlot(P776, DataComponents.Profile, resolved);
        Assert.Equal(1, frame[PayloadOffset]);

        // The uuid is written immediately after the discriminator, so its first byte is 0x11.
        Assert.Equal(0x11, frame[PayloadOffset + 1]);

        // The resolved branch is exactly the two optional-present bools narrower: the partial branch is [nameBool][name][idBool][uuid][properties] and the resolved one is [uuid][name][properties].
        byte[] partial = EncodeSlot(P776, DataComponents.Profile, PartialProfile);
        Assert.Equal(partial.Length - 2, frame.Length);

        // The branch survives the decode, which is what keeps a re-encode byte-exact.
        var decoded = (ClientboundContainerSetSlotPacket)BoundCodec
            .At(P776, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.Profile, out ProfileComponent? back));
        Assert.True(back!.Profile.Resolved);
        Assert.Equal(ProfileId, back.Profile.Id);
        Assert.Equal("p", back.Profile.Name);
    }

    /// <summary>The resolved branch has no optionality on the wire, so a profile that claims to be resolved without both fields must FAULT rather than quietly fall back to the partial branch, which the server would read as a different value.</summary>
    [Fact]
    public void Profile_ResolvedWithoutBothFieldsFaults()
    {
        var broken = new ProfileComponent(new ResolvableProfile("p", null, [], Resolved: true));

        Assert.Throws<ProtocolViolationException>(() => EncodeSlot(P776, DataComponents.Profile, broken));
    }

    /// <summary>The skin patch is a real field, not padding: a patch that overrides the body texture makes the frame wider by the identifier it carries, and the value survives the decode.</summary>
    [Fact]
    public void Profile_SkinPatchIsCarried()
    {
        var withSkin = new ProfileComponent(new ResolvableProfile(
            "p", ProfileId, [], Resolved: false, new PlayerSkinPatch(Body: Identifier.Minecraft("s"), SlimModel: true)));

        byte[] plain = EncodeSlot(P776, DataComponents.Profile, PartialProfile);
        byte[] patched = EncodeSlot(P776, DataComponents.Profile, withSkin);

        // "minecraft:s" is 11 bytes plus its length prefix, and the model optional gains its value byte.
        Assert.Equal(plain.Length + 12 + 1, patched.Length);

        var decoded = (ClientboundContainerSetSlotPacket)BoundCodec
            .At(P776, PacketFlow.Clientbound, SetSlot).DecodeFrame(patched);
        Assert.True(decoded.Item.Components.TryGet(DataComponents.Profile, out ProfileComponent? back));
        Assert.Equal(Identifier.Minecraft("s"), back!.Profile.SkinPatch!.Body);
        Assert.True(back.Profile.SkinPatch.SlimModel);
    }

    /// <summary>Cross-era rejection in BOTH directions on the payload axis alone: the profile wire id is 61 on both 771 and 773, so neither decode can be excused by the id ordering.</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that must refuse it.</param>
    [Theory]
    [InlineData(P773, P771)]
    [InlineData(P771, P773)]
    public void ProfileFrame_DoesNotCrossThePayloadBoundary(int from, int to)
    {
        byte[] frame = EncodeSlot(from, DataComponents.Profile, PartialProfile);

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(to, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    // 2. minecraft:entity_data and minecraft:block_entity_data

    private static NbtCompound Tag()
    {
        var tag = new NbtCompound();
        tag.Put("a", new NbtByte(1));
        return tag;
    }

    /// <summary>1.21.9 put a VarInt registry type id AHEAD of the tag, so the payload is exactly one byte wider for a single-byte type id and its first byte IS the type. Both components sit at the same wire id on 771 and 773 (49 and 51), so this is a pure payload assertion.</summary>
    [Fact]
    public void EntityData_GainsALeadingTypeIdAt773() => AssertTypeIdArrivesAt773(
        49,
        DataComponentMap.Empty.With(DataComponents.EntityData, new EntityDataComponent(Tag())),
        DataComponentMap.Empty.With(DataComponents.EntityData, new EntityDataComponent(Tag(), 7)));

    /// <summary><c>minecraft:block_entity_data</c> moved with its entity twin, at wire id 51 on 771/773.</summary>
    [Fact]
    public void BlockEntityData_GainsALeadingTypeIdAt773() => AssertTypeIdArrivesAt773(
        51,
        DataComponentMap.Empty.With(DataComponents.BlockEntityData, new BlockEntityDataComponent(Tag())),
        DataComponentMap.Empty.With(DataComponents.BlockEntityData, new BlockEntityDataComponent(Tag(), 7)));

    private static void AssertTypeIdArrivesAt773(int wireId, DataComponentMap legacyValue, DataComponentMap modernValue)
    {
        byte[] legacy = EncodeSlot(P771, legacyValue);
        byte[] modern = EncodeSlot(P773, modernValue);

        Assert.Equal(wireId, legacy[ComponentIdOffset]);
        Assert.Equal(wireId, modern[ComponentIdOffset]);
        Assert.Equal(legacy.Length + 1, modern.Length);
        Assert.Equal(7, modern[PayloadOffset]);

        // The 1.21.5 payload starts straight at the compound's root tag type byte (10).
        Assert.Equal(10, legacy[PayloadOffset]);
    }

    /// <summary>Protocol 776 must carry the type id for both typed-data components.</summary>
    [Fact]
    public void EntityData_776CarriesTheTypeId() => AssertTypeIdOn776(
        58, DataComponentMap.Empty.With(DataComponents.EntityData, new EntityDataComponent(Tag(), 7)));

    /// <summary>Same for <c>minecraft:block_entity_data</c>, at wire id 60 on 776.</summary>
    [Fact]
    public void BlockEntityData_776CarriesTheTypeId() => AssertTypeIdOn776(
        60, DataComponentMap.Empty.With(DataComponents.BlockEntityData, new BlockEntityDataComponent(Tag(), 7)));

    private static void AssertTypeIdOn776(int wireId776, DataComponentMap components)
    {
        byte[] frame = EncodeSlot(P776, components);

        Assert.Equal(wireId776, frame[ComponentIdOffset]);
        Assert.Equal(7, frame[PayloadOffset]);
        Assert.Equal(10, frame[PayloadOffset + 1]);
    }

    /// <summary>The type id has no default from 773 on, so a component built without one must fault rather than guess a value and desynchronize everything after it in the component list.</summary>
    [Fact]
    public void TypedEntityData_MissingTypeIdFaults() =>
        Assert.Throws<ProtocolViolationException>(
            () => EncodeSlot(P776, DataComponents.EntityData, new EntityDataComponent(Tag())));

    /// <summary>Cross-era rejection in both directions, again on the payload axis alone (same wire id).</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that must refuse it.</param>
    [Theory]
    [InlineData(P773, P771)]
    [InlineData(P771, P773)]
    public void EntityDataFrame_DoesNotCrossThePayloadBoundary(int from, int to)
    {
        var value = from == P773 ? new EntityDataComponent(Tag(), 7) : new EntityDataComponent(Tag());
        byte[] frame = EncodeSlot(from, DataComponentMap.Empty.With(DataComponents.EntityData, value));

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(to, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    /// <summary>The control: <c>bucket_entity_data</c> is still a plain compound tag on every era through 26.2, so it must NOT have gained a type id. Without this, "add a type id to the entity data family" would have been an easy over-correction.</summary>
    [Fact]
    public void BucketEntityData_DidNotMove()
    {
        byte[] legacy = EncodeSlot(P771, DataComponents.BucketEntityData, new BucketEntityDataComponent(Tag()));
        byte[] modern = EncodeSlot(P776, DataComponents.BucketEntityData, new BucketEntityDataComponent(Tag()));

        Assert.Equal(legacy.Length, modern.Length);
        Assert.Equal(10, legacy[PayloadOffset]);
        Assert.Equal(10, modern[PayloadOffset]);
    }
}
