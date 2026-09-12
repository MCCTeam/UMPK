using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Commands;

namespace Umpk.Client.Tests.Support;

/// <summary>Builds a minimal, unsigned <see cref="ChatActions"/> for tests that need to wire one into <see cref="DialogActions"/> (a run_command dialog button routes through it) but are not themselves exercising chat or command sending.</summary>
internal static class TestChatActions
{
    public static ChatActions Build(IPacketSink sink, ClientSessionServices services) =>
        new(
            sink,
            services,
            new CommandCompletionService(sink),
            new CommandService<ClientCommandSource>(),
            () => null!,
            static (send, ct) => send(null, ct));
}
