using Umpk.Client.Commands;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies the declare-commands (<c>minecraft:commands</c>) packet: reconstructs the server command tree from the wire node graph, stores it on <c>ClientState.ServerCommands</c>, and publishes <see cref="ServerCommandTreeUpdated"/>. The argument-type table is selected off the bound descriptor's <see cref="Umpk.Protocol.Java.ProtocolFeatures.ArgumentTypes"/> (a dataset-driven feature), mirroring the codec's era selection and keeping the client free of protocol-number branches.</summary>
internal sealed class CommandTreeApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        if (packet is ClientboundCommandSuggestionsPacket suggestions)
        {
            // Resolve the pending server tab-complete round trip correlated by transaction id.
            context.CommandCompletions.Complete(suggestions);
            return true;
        }

        if (packet is not ClientboundCommandsPacket commands)
            return false;

        ArgumentTypeRegistry registry = context.Version.Features.ArgumentTypes;

        ServerCommandTree tree = ServerCommandTree.Build(commands.Tree, registry);
        context.State.ServerCommands.Update(tree);
        await context.PublishAsync(new ServerCommandTreeUpdated(tree)).ConfigureAwait(false);
        return true;
    }
}
