namespace Umpk.Game.Registries;

// Definition types for registry-backed content. These are the immutable value objects a Registry<T> holds. Every registry entry pairs one of these with a numeric network id and an Identifier key (see RegistryEntry<T>). The types are records so that value equality and with-expressions are available to consumers that build synthetic snapshots.

/// <summary>A block registry entry: the facts about a block kind, plus the per-state facts a <see cref="Umpk.Game.Blocks.IBlockDataSource"/> answers through it. A composition root that has the version's generated data (see <c>Umpk.Data.Java.JavaGameData</c>) populates the attribute members; one that builds a synthetic registry leaves them at their defaults.</summary>
/// <param name="MinStateId">The lowest block-state id owned by this block (inclusive).</param>
/// <param name="MaxStateId">The highest block-state id owned by this block (inclusive).</param>
/// <param name="DefaultStateId">The block-state id vanilla treats as the default placement state.</param>
public sealed record BlockDefinition(int MinStateId, int MaxStateId, int DefaultStateId)
{
    /// <summary>The number of distinct block-states this block owns.</summary>
    public int StateCount => (MaxStateId - MinStateId) + 1;

    /// <summary>The flags shared by this block, and the flags of ALL its states when <see cref="StateFlags"/> is empty. Defaults to <see cref="Umpk.Game.Blocks.BlockFlags.None"/>, which is the honest answer for a registry built without block data: no claim either way.</summary>
    public Umpk.Game.Blocks.BlockFlags Flags { get; init; }

    /// <summary>Per-state flags, one entry per state from <see cref="MinStateId"/>, for blocks whose flags are NOT uniform across their states (an openable trapdoor blocks motion in one state and not in another; a waterloggable block is waterlogged in only half of them). Empty when <see cref="Flags"/> already describes every state, which is the common case.</summary>
    public IReadOnlyList<Umpk.Game.Blocks.BlockFlags> StateFlags { get; init; } = [];

    /// <summary>The slipperiness scalar (vanilla default 0.6; ice 0.98, blue ice 0.989, slime 0.8).</summary>
    public float Friction { get; init; } = 0.6f;

    /// <summary>The horizontal speed factor (vanilla default 1.0; soul sand and honey 0.4).</summary>
    public float SpeedFactor { get; init; } = 1.0f;

    /// <summary>The jump factor (vanilla default 1.0; honey 0.5).</summary>
    public float JumpFactor { get; init; } = 1.0f;

    /// <summary>The block's state properties in vanilla order (sorted by name), each with its ordered value domain. Empty when the version's data carries no property names for this block.</summary>
    public IReadOnlyList<BlockPropertyDefinition> Properties { get; init; } = [];

    /// <summary>True when <paramref name="stateId"/> falls inside this block's state range.</summary>
    public bool OwnsState(int stateId) => stateId >= MinStateId && stateId <= MaxStateId;

    /// <summary>The flags of one state id owned by this block.</summary>
    public Umpk.Game.Blocks.BlockFlags FlagsForState(int stateId)
    {
        int offset = stateId - MinStateId;
        return offset >= 0 && offset < StateFlags.Count ? StateFlags[offset] : Flags;
    }

    /// <summary>Resolves the value of a named property for one of this block's states, or false when the property is not on this block or its value domain is not known for this version.</summary>
    /// <remarks>Vanilla assigns state ids as a cartesian product over the block's properties with the LAST one varying fastest, so the stride of property <c>i</c> is the product of the domain sizes after it. That is the whole decode.</remarks>
    public bool TryGetPropertyValue(int stateId, string name, out string value)
    {
        value = string.Empty;
        int offset = stateId - MinStateId;
        if (offset < 0 || offset >= StateCount || Properties.Count == 0)
            return false;

        int index = -1;
        for (int i = 0; i < Properties.Count; i++)
            if (string.Equals(Properties[i].Name, name, StringComparison.Ordinal))
            {
                index = i;
                break;
            }

        if (index < 0 || Properties[index].Values.Count == 0)
            return false;

        int stride = 1;
        for (int i = Properties.Count - 1; i > index; i--)
        {
            int size = Properties[i].Values.Count;
            if (size == 0)
            {
                return false; // A gap after this property makes the stride unknowable.
            }

            stride *= size;
        }

        IReadOnlyList<string> values = Properties[index].Values;
        value = values[(offset / stride) % values.Count];
        return true;
    }
}

/// <summary>One block-state property: its name and the ordered list of values it can take.</summary>
/// <remarks>The order is load-bearing, not cosmetic: it is the order used by state-id arithmetic. An empty <see cref="Values"/> means the property NAME is known but its domain could not be established for this version, in which case a value lookup must fail rather than answer.</remarks>
/// <param name="Name">The vanilla property name, for example <c>age</c> or <c>facing</c>.</param>
/// <param name="Values">The ordered value domain, or empty when it is not known.</param>
public sealed record BlockPropertyDefinition(string Name, IReadOnlyList<string> Values);

/// <summary>An item registry entry.</summary>
/// <param name="MaxStackSize">The maximum stack size vanilla assigns this item (1 when unknown).</param>
public sealed record ItemDefinition(int MaxStackSize = 64);

/// <summary>An entity-type registry entry, carrying the standing bounding-box size.</summary>
/// <param name="Width">Bounding-box width in blocks.</param>
/// <param name="Height">Bounding-box height in blocks.</param>
public sealed record EntityTypeDefinition(float Width, float Height);

/// <summary>A dimension-type registry entry: the vertical bounds a world in this dimension has.</summary>
/// <param name="MinY">The lowest block Y coordinate.</param>
/// <param name="Height">The total build height in blocks.</param>
/// <param name="HasSkylight">Whether the dimension receives skylight.</param>
public sealed record DimensionTypeDefinition(int MinY, int Height, bool HasSkylight)
{
    /// <summary>The exclusive upper Y bound (<see cref="MinY"/> + <see cref="Height"/>).</summary>
    public int MaxY => MinY + Height;
}

/// <summary>A biome registry entry.</summary>
public sealed record BiomeDefinition;

/// <summary>Which value a chat-type decoration substitutes into one of its translation arguments. Serialized names are <c>sender</c>, <c>target</c> and <c>content</c> from protocol 760 up. Protocol 759 named the middle one <c>team_name</c>; the wire position and the meaning (the OTHER party in the line, not the speaker) are the same, so both names land on <see cref="Target"/>.</summary>
public enum ChatDecorationParameter
{
    /// <summary>The sender's display name, id 0.</summary>
    Sender = 0,

    /// <summary>The target or team name, id 1; <c>team_name</c> on protocol 759.</summary>
    Target = 1,

    /// <summary>The message body itself, id 2.</summary>
    Content = 2,
}

/// <summary>One chat-type decoration: the translation key a line is composed with, which values fill its arguments, and the style applied to the composed line.</summary>
/// <param name="TranslationKey">The translation key, for example <c>chat.type.text</c>.</param>
/// <param name="Parameters">The arguments to substitute, in order.</param>
/// <param name="Style">The style applied to the composed line (vanilla's default is the empty style).</param>
public sealed record ChatDecorationDefinition(
    string TranslationKey,
    IReadOnlyList<ChatDecorationParameter> Parameters,
    Umpk.Text.Style Style);

/// <summary>A chat-type (<c>minecraft:chat_type</c>) registry entry: how a server-sent message body is composed into the line a client shows. From 1.19 the server stopped sending a composed chat line and started sending the bare body plus a chat-type id, so this table is what turns the one into the other.</summary>
/// <param name="Chat">The decoration for the chat window, or null when this type shows its content undecorated. Undecorated is a real value, not an absent one: protocol 759 models the chat display as an <c>Optional&lt;TextDisplay&gt;</c> holding an <c>Optional&lt;ChatDecoration&gt;</c>, so a type with no decoration shows the bare body. Distinguishing that from "this id is not in the registry at all" is why it is nullable.</param>
/// <remarks>The chat type also carries a <c>narration</c> decoration, read only by the client's accessibility narrator. UMPK has no narration surface, so it is deliberately not modelled: a decoded field with no consumer is indistinguishable from one that was never sent.</remarks>
public sealed record ChatTypeDefinition(ChatDecorationDefinition? Chat);

/// <summary>An enchantment registry entry.</summary>
/// <param name="MaxLevel">The maximum level the enchantment reaches.</param>
public sealed record EnchantmentDefinition(int MaxLevel = 1);

/// <summary>How a status effect is classified.</summary>
public enum MobEffectCategory
{
    Beneficial = 0,
    Harmful = 1,
    Neutral = 2,
}

/// <summary>A status-effect (<c>minecraft:mob_effect</c>) registry entry.</summary>
/// <param name="Category">The vanilla effect category.</param>
/// <param name="Color">The particle/HUD color as packed RGB.</param>
public sealed record MobEffectDefinition(MobEffectCategory Category = MobEffectCategory.Neutral, int Color = 0);

/// <summary>An attribute registry entry: the clamped default value of a game attribute.</summary>
/// <param name="DefaultValue">The base value before modifiers.</param>
/// <param name="MinValue">The clamp floor (meaningful only when <paramref name="IsRanged"/>).</param>
/// <param name="MaxValue">The clamp ceiling (meaningful only when <paramref name="IsRanged"/>).</param>
/// <param name="IsRanged">True when the attribute clamps; false when values pass through unchanged. Every 26.2 registry attribute is ranged; the flag retains the distinction for completeness.</param>
public sealed record AttributeDefinition(double DefaultValue, double MinValue, double MaxValue, bool IsRanged = true);
