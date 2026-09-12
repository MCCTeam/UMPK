using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The server's <c>minecraft:chat_type</c> registry, from the three wire shapes that carry it to the decorated chat line a consumer reads.</summary>
/// <remarks>
/// <para>From 1.19 the server sends a bare body plus a chat-type id and the client composes the line through a datapack registry lookup.</para>
/// <para>The assertions preserve observed registry shapes and ordering:</para>
/// <list type="bullet">
/// <item>1.19.2: the registry rides in the JoinGame blob with full elements, numbered in vanilla's
/// BOOTSTRAP order (chat, say_command, msg_command_incoming, msg_command_outgoing, team_msg_command_incoming, team_msg_command_outgoing, emote_command).</item>
/// <item>1.20.4: the configuration-phase blob, full elements, numbered ALPHABETICALLY.</item>
/// <item>1.21.8: configuration-phase packed entries, ALPHABETICAL, and all seven arrive with NO
/// element at all because the client echoes the server's known-pack list. The vanilla built-in table is the only source of the decoration there.</item>
/// </list>
/// <para>The two orderings are why the table has to come from the server: a client that assumed either one would decorate every line on the other era with the wrong entry.</para>
/// </remarks>
public sealed class ChatTypeRegistryTests
{
    private const string Body = "words on the wire";

    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    // 766+ : packed entries with the elements elided

    /// <summary>The 1.21.8 shape exactly as measured: seven identifiers, zero elements. The decoration can only come from the built-in table, and the whole point of keeping the entries is that the SERVER owns the id-to-identifier association.</summary>
    [Fact]
    public async Task ElidedPackedEntries_StillProduceTheVanillaDecorations()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        Registry<ChatTypeDefinition> types = harness.State.Registries!.ChatTypes!;
        Assert.Equal(7, types.Count);
        Assert.Equal("chat.type.text", types[0].Value.Chat!.TranslationKey);
        Assert.Equal("chat.type.emote", types[1].Value.Chat!.TranslationKey);
        Assert.Equal("commands.message.display.incoming", types[2].Value.Chat!.TranslationKey);
        Assert.Equal("commands.message.display.outgoing", types[3].Value.Chat!.TranslationKey);
        Assert.Equal("chat.type.announcement", types[4].Value.Chat!.TranslationKey);
        Assert.Equal("chat.type.team.text", types[5].Value.Chat!.TranslationKey);
        Assert.Equal("chat.type.team.sent", types[6].Value.Chat!.TranslationKey);

        // Direct messages use an empty style colored gray and italicized.
        Assert.Equal(TextColor.Gray, types[2].Value.Chat!.Style.Color);
        Assert.True(types[2].Value.Chat!.Style.Italic);
        Assert.True(types[0].Value.Chat!.Style.IsEmpty);

        // outgoing takes TARGET, not SENDER: the outgoing direct-message decoration.
        Assert.Equal(
            [ChatDecorationParameter.Target, ChatDecorationParameter.Content],
            types[3].Value.Chat!.Parameters);
    }

    /// <summary>An entry with no element AND no vanilla built-in is dropped rather than invented, which keeps a datapack chat type from silently rendering as some other type's decoration.</summary>
    [Fact]
    public async Task AnElidedDatapackEntry_IsDroppedRatherThanInvented()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(
            RegistryIds.ChatType,
            [
                new PackedRegistryEntry(Identifier.Minecraft("chat"), null),
                new PackedRegistryEntry(new Identifier("mypack", "shout"), null),
            ]));

        Registry<ChatTypeDefinition> types = harness.State.Registries!.ChatTypes!;
        Assert.Single(types);
        Assert.False(types.ContainsNetworkId(1));
    }

    // The applied result: a decorated chat line

    /// <summary>The line a consumer reads for <c>/me</c>. Live on 1.21.8 the server sent wire value 2 for this and vanilla rendered <c>* a2probe waves</c>; before the registry was kept it read <c>&lt;a2probe&gt; waves</c>.</summary>
    [Theory]
    [InlineData(772)]
    [InlineData(776)]
    public async Task EmoteChatType_RendersTheEmoteDecoration(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        // Wire value 2 on a holder-encoded era is registry id 1, minecraft:emote_command.
        ChatMessageReceived seen = await ChatAsync(harness, protocol, wireChatType: 2, target: null);
        Assert.Equal("* Notch waves", seen.Message.ToPlainText());
        Assert.Equal(1, seen.ChatTypeId);
        Assert.Equal("waves", seen.Body.ToPlainText());
    }

    /// <summary>The outgoing whisper: the TARGET parameter, not the sender, plus the gray-italic style. This is the cell that proves the parameter list is read from the registry rather than assumed, because every other built-in decoration starts with SENDER.</summary>
    [Fact]
    public async Task OutgoingWhisper_UsesTheTargetParameterAndTheGrayItalicStyle()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        // Wire value 4 -> registry id 3, minecraft:msg_command_outgoing.
        ChatMessageReceived seen = await ChatAsync(harness, 772, wireChatType: 4, target: Component.Text("Steve"));

        Assert.Equal("You whisper to Steve: waves", seen.Message.ToPlainText());
        Assert.Equal(TextColor.Gray, seen.Message.Style.Color);
        Assert.True(seen.Message.Style.Italic);

        var translatable = Assert.IsType<TranslatableContent>(seen.Message.Content);
        Assert.Equal("commands.message.display.outgoing", translatable.Key);
        Assert.Equal(2, translatable.Args.Count);
        Assert.Equal("Steve", translatable.Args[0].ToPlainText());
    }

    /// <summary>Disguised chat uses its bound decoration type, which turns a <c>/say</c> into <c>[name] body</c>. Publishing it undecorated would lose the command-issued chat type.</summary>
    [Fact]
    public async Task DisguisedChat_IsDecoratedThroughTheResolvedChatType()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        // Wire value 5 -> registry id 4, minecraft:say_command.
        var packet = new ClientboundDisguisedChatPacket(
            Component.Text("announcement"), ChatTypeId: 5, Component.Text("Server"), TargetName: null);
        ChatMessageReceived seen = await DeliverAsync(harness, 772, "disguised_chat", packet);

        Assert.Equal(ChatCategory.Disguised, seen.Category);
        Assert.Equal("[Server] announcement", seen.Message.ToPlainText());
        Assert.Equal("announcement", seen.Body.ToPlainText());
        Assert.Equal(4, seen.ChatTypeId);
    }

    /// <summary>A chat-type id the server never declared falls back to the default player-chat decoration, so a consumer never loses the sender name to a registry gap.</summary>
    [Fact]
    public async Task AnUnresolvedChatTypeId_FallsBackToTheDefaultPlayerDecoration()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        ChatMessageReceived seen = await ChatAsync(harness, 772, wireChatType: 99, target: null);
        Assert.Equal("<Notch> waves", seen.Message.ToPlainText());
    }

    // The holder offset

    /// <summary>The wire value is the registry id through 1.20.6 and <c>id + 1</c> from 1.21.</summary>
    /// <remarks>Protocols through 766 write the registry id with no offset. Protocol 767 and later write <c>id + 1</c> and reserves 0 for an inline chat type. Live on 1.21.8 a plain chat arrived as wire value 1 and vanilla rendered <c>&lt;name&gt; body</c>, i.e. registry id 0.</remarks>
    [Theory]
    [InlineData(760, 2, 2)]
    [InlineData(765, 2, 2)]
    [InlineData(766, 2, 2)]
    [InlineData(767, 2, 1)]
    [InlineData(772, 2, 1)]
    [InlineData(776, 2, 1)]
    public async Task TheWireValue_IsNormalizedToARegistryId(int protocol, int wire, int expected)
    {
        ApplierHarness harness = Harness(protocol);
        ChatMessageReceived seen = await ChatAsync(harness, protocol, wireChatType: wire, target: null);
        Assert.Equal(expected, seen.ChatTypeId);
    }

    /// <summary>Wire value 0 on a holder era is vanilla's <c>DIRECT_HOLDER_ID</c>, an inline chat type UMPK does not model, so it is reported as unresolvable rather than as registry id 0.</summary>
    [Fact]
    public async Task TheDirectHolderSentinel_IsReportedAsUnresolvable()
    {
        ApplierHarness harness = Harness(772);
        await harness.ApplyAsync(new ClientboundConfigRegistryDataPacket(RegistryIds.ChatType, AlphabeticalElided()));

        ChatMessageReceived seen = await ChatAsync(harness, 772, wireChatType: 0, target: null);
        Assert.Equal(-1, seen.ChatTypeId);
        Assert.Equal("<Notch> waves", seen.Message.ToPlainText());
    }

    // 759-765 : the NBT blob shapes, with real elements

    /// <summary>The 1.20.2/1.20.4 configuration blob, which carries the elements in full because the known-packs handshake does not exist before 1.20.5. The decorations here come from the server's own NBT, not from the built-in table: the datapack entry proves it.</summary>
    [Fact]
    public async Task TheConfigurationBlob_DecodesTheServersOwnElements()
    {
        ApplierHarness harness = Harness(765);
        await harness.ApplyAsync(new ClientboundConfigRegistryBlobPacket(Blob(
            ("minecraft:chat", 0, Modern("chat.type.text", ["sender", "content"], gray: false)),
            ("mypack:shout", 1, Modern("mypack.shout", ["sender", "target", "content"], gray: true)))));

        Registry<ChatTypeDefinition> types = harness.State.Registries!.ChatTypes!;
        Assert.Equal(2, types.Count);
        ChatDecorationDefinition shout = types[1].Value.Chat!;
        Assert.Equal("mypack.shout", shout.TranslationKey);
        Assert.Equal(
            [ChatDecorationParameter.Sender, ChatDecorationParameter.Target, ChatDecorationParameter.Content],
            shout.Parameters);
        Assert.Equal(TextColor.Gray, shout.Style.Color);

        // A datapack key has no en_us fallback here, so it renders as the key with the arguments applied, which is exactly what vanilla's own client shows for a key its language file lacks.
        ChatMessageReceived seen = await ChatAsync(harness, 765, wireChatType: 1, target: Component.Text("Steve"));
        var translatable = Assert.IsType<TranslatableContent>(seen.Message.Content);
        Assert.Equal("mypack.shout", translatable.Key);
        Assert.Null(translatable.Fallback);
        Assert.Equal(3, translatable.Args.Count);
        Assert.Equal("Notch", translatable.Args[0].ToPlainText());
        Assert.Equal("Steve", translatable.Args[1].ToPlainText());
        Assert.Equal("waves", translatable.Args[2].ToPlainText());
    }

    /// <summary>The 1.19-1.20.1 JoinGame blob, in vanilla's bootstrap order as measured live on 1.19.2. The join packet is the ONLY carrier on that band, so taking it there is what makes 759-763 work at all.</summary>
    [Fact]
    public async Task TheJoinGameBlob_InstallsTheChatTypes()
    {
        ApplierHarness harness = Harness(760);
        NbtCompound blob = Blob(
            ("minecraft:chat", 0, Modern("chat.type.text", ["sender", "content"], gray: false)),
            ("minecraft:emote_command", 6, Modern("chat.type.emote", ["sender", "content"], gray: false)));

        await JoinAsync(harness, blob);

        Registry<ChatTypeDefinition> types = harness.State.Registries!.ChatTypes!;
        Assert.Equal(2, types.Count);
        Assert.Equal("chat.type.emote", types[6].Value.Chat!.TranslationKey);

        ChatMessageReceived seen = await ChatAsync(harness, 760, wireChatType: 6, target: null);
        Assert.Equal("* Notch waves", seen.Message.ToPlainText());
    }

    /// <summary>Protocol 759's own element shape: the chat display is a <c>TextDisplay</c> wrapping an OPTIONAL decoration, and its middle parameter is named <c>team_name</c> rather than <c>target</c> (the 1.19 wire layout carries the string <c>team_name</c> and no <c>msg_command_incoming</c>, and every jar from 1.19.1 up is the reverse). A TextDisplay with no decoration means "show the body bare", which is a value, not an absence.</summary>
    [Fact]
    public async Task Protocol759_ReadsTheTextDisplayShapeAndTheTeamNameParameter()
    {
        ApplierHarness harness = Harness(759);
        var teamDisplay = new NbtCompound();
        teamDisplay.Put("decoration", Decoration("chat.type.team.text", ["team_name", "sender", "content"], gray: false));

        NbtCompound blob = Blob(
            ("minecraft:team_msg_command", 0, Legacy(teamDisplay)),
            ("minecraft:tellraw_command", 1, Legacy(new NbtCompound())));

        await JoinAsync(harness, blob);

        Registry<ChatTypeDefinition> types = harness.State.Registries!.ChatTypes!;
        Assert.Equal(
            [ChatDecorationParameter.Target, ChatDecorationParameter.Sender, ChatDecorationParameter.Content],
            types[0].Value.Chat!.Parameters);
        Assert.Null(types[1].Value.Chat);

        Assert.Equal(
            "Reds <Notch> waves",
            (await ChatAsync(harness, 759, wireChatType: 0, target: Component.Text("Reds"))).Message.ToPlainText());

        // The undecorated type shows the body alone, and does NOT fall through to the default decoration.
        Assert.Equal(
            "waves",
            (await ChatAsync(harness, 759, wireChatType: 1, target: null)).Message.ToPlainText());
    }

    // helpers

    private static ApplierHarness Harness(int protocol) =>
        new(Version(protocol)) { State = { Registries = JavaGameData.Registries(protocol) } };

    /// <summary>The seven vanilla identifiers in the alphabetical order both 1.20.4 and 1.21.8 send.</summary>
    private static PackedRegistryEntry[] AlphabeticalElided() =>
    [
        new(Identifier.Minecraft("chat"), null),
        new(Identifier.Minecraft("emote_command"), null),
        new(Identifier.Minecraft("msg_command_incoming"), null),
        new(Identifier.Minecraft("msg_command_outgoing"), null),
        new(Identifier.Minecraft("say_command"), null),
        new(Identifier.Minecraft("team_msg_command_incoming"), null),
        new(Identifier.Minecraft("team_msg_command_outgoing"), null),
    ];

    private static async Task<ChatMessageReceived> ChatAsync(
        ApplierHarness harness, int protocol, int wireChatType, Component? target)
    {
        var packet = new ClientboundPlayerChatPacket(
            Sender: Sender,
            Index: 0,
            Signature: null,
            SignedContent: "waves",
            TimestampMillis: 1_700_000_000_000L,
            Salt: 0,
            UnsignedContent: null,
            ChatTypeId: wireChatType,
            SenderName: Component.Text("Notch"),
            TargetName: target);
        return await DeliverAsync(harness, protocol, "player_chat", packet);
    }

    private static async Task<ChatMessageReceived> DeliverAsync(
        ApplierHarness harness, int protocol, string identifier, IPacket packet)
    {
        ChatMessageReceived? seen = null;
        using IDisposable subscription = harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        // Through the bound descriptor codec, so the era's wire form reaches the applier.
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, identifier, packet));
        Assert.NotNull(seen);
        return seen!;
    }

    private static async Task JoinAsync(ApplierHarness harness, NbtTag registries)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null)
        {
            JoinGameRegistry = registries,
        });
    }

    private static NbtCompound Blob(params (string Name, int Id, NbtCompound Element)[] entries)
    {
        var values = new NbtList(NbtTagType.Compound);
        foreach ((string name, int id, NbtCompound element) in entries)
        {
            var record = new NbtCompound();
            record.PutString("name", name);
            record.PutInt("id", id);
            record.Put("element", element);
            values.Add(record);
        }

        var section = new NbtCompound();
        section.PutString("type", "minecraft:chat_type");
        section.Put("value", values);

        var root = new NbtCompound();
        root.Put("minecraft:chat_type", section);
        return root;
    }

    /// <summary>The 760+ element: <c>{chat:{translation_key,parameters,style?},narration:{...}}</c>.</summary>
    private static NbtCompound Modern(string key, string[] parameters, bool gray)
    {
        var element = new NbtCompound();
        element.Put("chat", Decoration(key, parameters, gray));
        element.Put("narration", Decoration("chat.type.text.narrate", ["sender", "content"], gray: false));
        return element;
    }

    /// <summary>The 759 element: the chat display wraps an optional decoration.</summary>
    private static NbtCompound Legacy(NbtCompound chatDisplay)
    {
        var element = new NbtCompound();
        element.Put("chat", chatDisplay);
        return element;
    }

    private static NbtCompound Decoration(string key, string[] parameters, bool gray)
    {
        var list = new NbtList(NbtTagType.String);
        foreach (string parameter in parameters)
            list.Add(new NbtString(parameter));

        var decoration = new NbtCompound();
        decoration.PutString("translation_key", key);
        decoration.Put("parameters", list);
        if (gray)
        {
            var style = new NbtCompound();
            style.PutString("color", "gray");
            decoration.Put("style", style);
        }

        return decoration;
    }
}
