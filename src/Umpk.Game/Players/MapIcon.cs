using Umpk.Text;

namespace Umpk.Game.Players;

/// <summary>A decoration icon on a filled map, matching the map-update packet fields: icon type, packed position and rotation, and an optional custom name.</summary>
/// <param name="Type">The decoration type id (player, frame, monument, banner colors, and so on).</param>
/// <param name="X">The packed X position on the map (-128..127).</param>
/// <param name="Z">The packed Z position on the map (-128..127).</param>
/// <param name="Rotation">The rotation (0..15, in 22.5-degree steps).</param>
/// <param name="DisplayName">An optional custom name shown on the decoration.</param>
public readonly record struct MapIcon(int Type, sbyte X, sbyte Z, byte Rotation, Component? DisplayName);
