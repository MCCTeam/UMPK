using System.Diagnostics.CodeAnalysis;

namespace Umpk.Game.Players;

/// <summary>The stored pixel and decoration state of one filled map. Maps are always 128x128; the color grid is one byte per pixel (the vanilla map-color index). Icons are replaced wholesale on each update, while pixel updates arrive as a rectangular sub-region.</summary>
public sealed class MapData
{
    /// <summary>The fixed edge length of a Minecraft map in pixels.</summary>
    public const int Size = 128;

    private readonly byte[] _colors = new byte[Size * Size];
    private MapIcon[] _icons = [];

    /// <summary>Creates map state for the given map id.</summary>
    public MapData(int mapId) => MapId = mapId;

    /// <summary>The map item id (the state key).</summary>
    public int MapId { get; }

    /// <summary>The map scale (0-4; each step doubles the blocks-per-pixel).</summary>
    public byte Scale { get; set; }

    /// <summary>Whether the map is locked (cannot be updated by exploration).</summary>
    public bool Locked { get; set; }

    /// <summary>The current decoration icons.</summary>
    public IReadOnlyList<MapIcon> Icons => _icons;

    /// <summary>The full 128x128 color grid, row-major (index = z * 128 + x).</summary>
    public ReadOnlySpan<byte> Colors => _colors;

    /// <summary>Gets the color index at a pixel.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is outside [0, 128).</exception>
    public byte GetColor(int x, int z)
    {
        ThrowIfOutOfRange(x, z);
        return _colors[(z * Size) + x];
    }

    /// <summary>Replaces the decoration icons wholesale.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="icons"/> is null.</exception>
    public void SetIcons(IReadOnlyList<MapIcon> icons)
    {
        ArgumentNullException.ThrowIfNull(icons);
        _icons = [.. icons];
    }

    /// <summary>Applies a rectangular pixel update at (<paramref name="startX"/>, <paramref name="startZ"/>) of size <paramref name="width"/> x <paramref name="height"/>. <paramref name="colors"/> is row-major over the sub-region (length must be width * height). Matches the packet's column update layout.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="colors"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The region falls outside the map or the array length is wrong.</exception>
    public void UpdateRegion(int startX, int startZ, int width, int height, ReadOnlySpan<byte> colors)
    {
        if (width < 0 || height < 0 || startX < 0 || startZ < 0 || startX + width > Size || startZ + height > Size)
            throw new ArgumentOutOfRangeException(nameof(width), "Map pixel region falls outside the 128x128 map.");

        if (colors.Length != width * height)
            throw new ArgumentOutOfRangeException(nameof(colors), "Color span length must equal width * height.");

        for (int row = 0; row < height; row++)
        {
            int destStart = ((startZ + row) * Size) + startX;
            colors.Slice(row * width, width).CopyTo(_colors.AsSpan(destStart, width));
        }
    }

    private static void ThrowIfOutOfRange(int x, int z)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Size);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(z, Size);
    }
}
