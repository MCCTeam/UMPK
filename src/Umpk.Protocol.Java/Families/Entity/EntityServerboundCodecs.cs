using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Era codec members for the SERVERBOUND player movement and interaction family. The 1.8 shapes use a bare on-ground byte and dedicated fields; the modern shapes pack on-ground and horizontal-collision into a single flags byte and add sequence/secondary-action fields. 26.1 splits attack out of interact and adds player-input flow.</summary>
/// <remarks>Modern move flags: bit 0 on-ground, bit 1 horizontal-collision. Player input: forward 0x01, back 0x02, left 0x04, right 0x08, jump 0x10, shift 0x20, sprint 0x40.</remarks>
internal static partial class EntityServerboundCodecs
{
    private static byte PackMoveFlags(bool onGround, bool horizontalCollision) =>
        (byte)((onGround ? 0x01 : 0) | (horizontalCollision ? 0x02 : 0));
}
