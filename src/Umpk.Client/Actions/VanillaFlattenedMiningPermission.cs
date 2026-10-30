using System.Runtime.CompilerServices;
using System.Text;
using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Nbt;

namespace Umpk.Client.Actions;

/// <summary>Flattened CanDestroy selectors and the native per-stack, last-state permission cache. Null means the available session facts cannot establish the native result.</summary>
internal sealed class VanillaFlattenedMiningPermission
{
    private const int FlatteningProtocol = 393;
    private const int SingleQuotedPropertiesProtocol = 477;
    private const int ConditionalBlockEntityCacheProtocol = 757;
    private const int ItemComponentsProtocol = 766;
    private readonly ConditionalWeakTable<ItemStack, CachedPermission> _cache = new();
    private long? _interactionLifetime;
    private long _playerLifetime;
    private WeakReference<World>? _world;

    private sealed record CachedPermission(BlockState State, bool Allowed);
    private enum SelectorResult { Denied, Allowed, UnknownContext, UnsupportedNbt }

    public static bool Supports(int protocol) => protocol is >= FlatteningProtocol and < ItemComponentsProtocol;

    public void Synchronize(long interactionLifetime, long playerLifetime, World? world)
    {
        if (_interactionLifetime == interactionLifetime && _playerLifetime == playerLifetime
            && (_world is null ? world is null : _world.TryGetTarget(out World? previous) && ReferenceEquals(previous, world)))
            return;
        _cache.Clear();
        _interactionLifetime = interactionLifetime;
        _playerLifetime = playerLifetime;
        _world = world is null ? null : new WeakReference<World>(world);
    }

    public bool? Allows(ItemStack item, BlockState target, Registry<BlockDefinition> blocks, ClientRegistryTags tags, int protocol)
    {
        if (!Supports(protocol) || !target.IsValid)
            return null;
        if (item.IsEmpty)
            return false;
        if (_cache.TryGetValue(item, out CachedPermission? cached) && cached.State == target)
            return cached.Allowed;
        _cache.Remove(item);
        NbtList? list = item.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetList("CanDestroy");
        if (list is null || list.ElementType != NbtTagType.String || list.Count == 0)
            return false;
        // Before 1.18 native compares full block-entity saves even for selectors without NBT. Packet NBT cannot prove either entity absence or save equality. These ordinary blocks are the bounded source-verified absence set until that registry fact is available.
        if (protocol < ConditionalBlockEntityCacheProtocol && !(target.Block.Id.IsMinecraft && target.Block.Id.Path is "stone" or "dirt" or "oak_log"))
            return null;
        bool unknown = false;
        foreach (NbtTag entry in list)
        {
            SelectorResult result = Evaluate(((NbtString)entry).Value, target, blocks, tags, protocol);
            if (result == SelectorResult.UnsupportedNbt)
                return null;
            if (result == SelectorResult.Allowed)
            {
                _cache.Add(item, new CachedPermission(target, true));
                return true;
            }
            unknown |= result == SelectorResult.UnknownContext;
        }
        if (unknown)
            return null;
        _cache.Add(item, new CachedPermission(target, false));
        return false;
    }

    private static SelectorResult Evaluate(string selector, BlockState target, Registry<BlockDefinition> blocks, ClientRegistryTags tags, int protocol)
    {
        var reader = new SelectorReader(selector, protocol >= SingleQuotedPropertiesProtocol);
        bool tag = reader.Take('#');
        if (!reader.ReadIdentifier(out Identifier id))
            return SelectorResult.Denied;
        BlockDefinition? definition = null;
        if (!tag && !blocks.TryGetValue(id, out definition))
            return SelectorResult.Denied;
        Dictionary<string, string> properties = [];
        if (reader.Take('['))
        {
            reader.SkipWhitespace();
            while (reader.CanRead && reader.Peek != ']')
            {
                reader.SkipWhitespace();
                if (!reader.ReadString(out string name) || properties.ContainsKey(name))
                    return SelectorResult.Denied;
                reader.SkipWhitespace();
                if (!reader.Take('='))
                    return SelectorResult.Denied;
                reader.SkipWhitespace();
                if (!reader.ReadString(out string value))
                    return SelectorResult.Denied;
                if (definition is not null)
                {
                    BlockPropertyDefinition? property = definition.Properties.FirstOrDefault(property => property.Name == name);
                    if (property is null || !property.Values.Contains(value) && !CouldNameAnIntegerValue(property, value))
                        return SelectorResult.Denied;
                }
                properties.Add(name, value);
                reader.SkipWhitespace();
                if (!reader.Take(','))
                    break;
            }
            if (!reader.Take(']'))
                return SelectorResult.Denied;
        }
        // Native parses only the consumed prefix. Whitespace before '[' or '{' ends that prefix; an immediate '{' needs the native block entity's full save and also changes cache invalidation.
        if (reader.CanRead && reader.Peek == '{')
            return SelectorResult.UnsupportedNbt;
        bool unknownPropertyKind = false;
        foreach ((string name, string value) in properties)
        {
            if (!target.TryGetProperty(name, out string actual))
                return SelectorResult.Denied;
            if (actual == value)
                continue;
            // Native IntegerProperty parses Java decimal spellings before comparing values. Canonical datasets do not expose the property's native class, so equal alternate numeric spellings cannot be called a proven denial.
            if (VanillaLegacyMiningPermission.TryReadJavaInteger(value, out int requested)
                && VanillaLegacyMiningPermission.TryReadJavaInteger(actual, out int present) && requested == present)
                unknownPropertyKind = true;
            else
                return SelectorResult.Denied;
        }
        bool? matches = tag ? tags.Contains(RegistryIds.Block, id, target.Block.NetworkId) : target.Block.Id == id;
        if (matches is false)
            return SelectorResult.Denied;
        return matches is null || unknownPropertyKind ? SelectorResult.UnknownContext : SelectorResult.Allowed;
    }

    private static bool CouldNameAnIntegerValue(BlockPropertyDefinition property, string value)
        => VanillaLegacyMiningPermission.TryReadJavaInteger(value, out int requested)
            && property.Values.Any(candidate => VanillaLegacyMiningPermission.TryReadJavaInteger(candidate, out int canonical) && canonical == requested);

    private ref struct SelectorReader(string text, bool singleQuotes)
    {
        private int _cursor;
        public readonly bool CanRead => _cursor < text.Length;
        public readonly char Peek => text[_cursor];

        public bool Take(char value)
        {
            if (!CanRead || Peek != value)
                return false;
            _cursor++;
            return true;
        }

        public bool ReadIdentifier(out Identifier id)
        {
            int start = _cursor;
            while (CanRead && (Peek is >= '0' and <= '9' or >= 'a' and <= 'z' or '_' or ':' or '/' or '.' or '-'))
                _cursor++;
            return Identifier.TryParse(text[start.._cursor], out id);
        }

        public void SkipWhitespace()
        {
            // Character.isWhitespace excludes the three nonbreaking spaces and NEXT LINE, and includes the four ASCII information separators.
            while (CanRead && (Peek is >= '\u001c' and <= '\u001f' || char.IsWhiteSpace(Peek) && Peek is not ('\u0085' or '\u00a0' or '\u2007' or '\u202f')))
                _cursor++;
        }

        public bool ReadString(out string value)
        {
            value = string.Empty;
            if (!CanRead)
                return true;
            char quote = Peek;
            if (quote != '"' && !(singleQuotes && quote == '\''))
            {
                int start = _cursor;
                while (CanRead && (Peek is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_' or '-' or '.' or '+'))
                    _cursor++;
                value = text[start.._cursor];
                return true;
            }
            _cursor++;
            var result = new StringBuilder();
            while (CanRead)
            {
                char character = text[_cursor++];
                if (character == quote)
                {
                    value = result.ToString();
                    return true;
                }
                if (character == '\\')
                {
                    if (!CanRead)
                        return false;
                    character = text[_cursor++];
                    if (character != quote && character != '\\')
                        return false;
                }
                result.Append(character);
            }
            return false;
        }
    }
}
