namespace Umpk.Commands;

/// <summary>A UMPK-owned command argument type. Instances are produced by <see cref="Arguments"/> (library types) or by consumer code; the Brigadier parser they wrap never appears on this surface.</summary>
/// <typeparam name="T">The parsed value type.</typeparam>
/// <remarks>The interface is intentionally opaque: it carries no public members, so no dispatcher type leaks through it. The dispatcher consumes registered argument types through internal seams only. Consumers obtain instances from <see cref="Arguments"/>; new library-neutral types are added there.</remarks>
public interface IArgumentType<out T>
{
}
