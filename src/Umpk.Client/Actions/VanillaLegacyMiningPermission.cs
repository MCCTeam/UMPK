using System.Globalization;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;

namespace Umpk.Client.Actions;

/// <summary>The pre-flattening CanDestroy block-name/number list. State, tag and block-entity predicates belong to later formats.</summary>
internal static class VanillaLegacyMiningPermission
{
    private const int FlatteningProtocol = 393;
    private const int LowercasePathProtocol = 315;

    public static bool Supports(int protocol) => protocol < FlatteningProtocol;

    public static bool Allows(ItemStack item, BlockState target, int protocol)
    {
        if (item.IsEmpty || !target.IsValid || target.IsAir)
            return false;
        NbtList? list = item.Components.Get(DataComponents.LegacyNbt)?.Nbt.GetList("CanDestroy");
        if (list is null || list.ElementType != NbtTagType.String)
            return false;
        foreach (NbtTag entry in list)
        {
            string selector = ((NbtString)entry).Value;
            int colon = selector.IndexOf(':');
            ReadOnlySpan<char> nameSpace = colon > 1 ? selector.AsSpan(0, colon) : "minecraft";
            string path = colon >= 0 ? selector[(colon + 1)..] : selector;
            // ResourceLocation preserves the path through 1.10, then lowercases it from 1.11. A colon in position zero or one retains the default namespace in these original implementations.
            if (protocol >= LowercasePathProtocol)
                path = path.ToLowerInvariant();
            if (nameSpace.Equals(target.Block.Id.Namespace, StringComparison.OrdinalIgnoreCase) && path == target.Block.Id.Path)
                return true;
            if (TryReadJavaInteger(selector, out int id) && id == target.Block.NetworkId)
                return true;
        }
        return false;
    }

    internal static bool TryReadJavaInteger(ReadOnlySpan<char> value, out int id)
    {
        id = 0;
        if (value.IsEmpty)
            return false;
        bool negative = value[0] == '-';
        int offset = negative || value[0] == '+' ? 1 : 0;
        if (offset == value.Length)
            return false;
        long limit = negative ? 2147483648L : int.MaxValue;
        long number = 0;
        for (; offset < value.Length; offset++)
        {
            // Java Integer.parseInt accepts BMP decimal digits, including non-ASCII digits, but rejects whitespace and overflow.
            int digit = CharUnicodeInfo.GetDecimalDigitValue(value[offset]);
            if (digit < 0)
                return false;
            number = number * 10 + digit;
            if (number > limit)
                return false;
        }
        id = (int)(negative ? -number : number);
        return true;
    }
}
