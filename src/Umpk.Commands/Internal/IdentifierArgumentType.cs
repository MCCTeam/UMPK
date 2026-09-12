using Brigadier.NET;
using Brigadier.NET.Exceptions;
using Brig = Brigadier.NET.ArgumentTypes;

namespace Umpk.Commands.Internal;

/// <summary>Brigadier argument type parsing a namespaced <see cref="Identifier"/> (<c>namespace:path</c>). Dataset-free: it validates against <see cref="Identifier"/>'s own character rules, needing no per-version registry. Matches the game <c>ResourceLocationArgument</c> shape (identifier token only).</summary>
internal sealed class IdentifierArgumentType : Brig.IArgumentType<Identifier>
{
    private static readonly SimpleCommandExceptionType InvalidId =
        new(new Brigadier.NET.LiteralMessage("Invalid identifier"));

    internal static IdentifierArgumentType Instance { get; } = new();

    public Identifier Parse(IStringReader reader)
    {
        int start = reader.Cursor;
        while (reader.CanRead() && IsAllowed(reader.Peek()))
            reader.Skip();

        string token = reader.String.Substring(start, reader.Cursor - start);
        if (Identifier.TryParse(token, out var id))
            return id;

        reader.Cursor = start;
        throw InvalidId.CreateWithContext(reader);
    }

    public IEnumerable<string> Examples => ["minecraft:stone", "foo:bar", "stone"];

    private static bool IsAllowed(char c) =>
        c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-' or '/' or ':';
}
