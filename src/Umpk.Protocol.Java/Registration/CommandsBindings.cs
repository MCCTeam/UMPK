using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Command-tree timeline (the declare-commands packet).</summary>
internal static class CommandsBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        CommandTreeCodecs.DeclareCommands(bindings);
    }
}
