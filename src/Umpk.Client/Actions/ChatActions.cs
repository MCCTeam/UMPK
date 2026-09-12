using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Commands;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Client.Actions;

/// <summary>Runs one chat or command send with the session's chat-signing state resolved and held stable for the whole of resolve, sign and send.</summary>
/// <remarks>The scope, not merely the resolved value, is the contract. A profile-key rotation swaps the chat session id, the key and the message index together and announces the swap on the wire; a sender that resolved before the swap and sent after the announcement puts a message the server cannot verify behind it, and the 1.19.2 server answers that by disconnecting rather than dropping. Passing a resolved <see cref="ChatSigningState"/> out of a scope and signing with it later reintroduces exactly that window. <paramref name="send"/> receives null when the session has no certificates, which is the unsigned path.</remarks>
/// <param name="send">The resolve-sign-send body, run while the signing state is pinned.</param>
/// <param name="ct">The send's cancellation token.</param>
internal delegate ValueTask ChatSigningScope(
    Func<ChatSigningState?, CancellationToken, ValueTask> send, CancellationToken ct);

/// <summary>Chat and command sending plus command completion. Applies the instance chat cooldown, splits long messages, selects the legacy or signed send path by version, and offers a merged completion entry point (<see cref="CompleteAsync"/>) that unions host-command suggestions, the local server-command-tree walk, and the server tab-complete round trip. Signed chat uses the empty-signature/last-seen shape the offline path supports; full signing lives in the signing session. Commands are a separate wire from chat on 1.19+ and take the <c>chat_command</c> family (see <see cref="SendCommandAsync"/>); only below 1.19 is a slashed chat frame a command.</summary>
public sealed class ChatActions
{
    private const int MaxChatLength = 256;

    /// <summary>1.19 (protocol 759): the first version with a serverbound <c>minecraft:chat_command</c>.</summary>
    private const int FirstChatCommandProtocol = 759;

    /// <summary>1.20.5 (protocol 766): where vanilla split <c>chat_command</c> into the bare unsigned packet plus the separate <c>chat_command_signed</c>.</summary>
    private const int FirstSplitChatCommandProtocol = 766;

    /// <summary>The 20-bit last-seen acknowledgement bitset, ceil(20/8) = 3 bytes on the wire.</summary>
    private const int AckBitsetBytes = 3;

    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;
    private readonly CommandCompletionService _completion;
    private readonly CommandService<ClientCommandSource> _commands;
    private readonly Func<ClientCommandSource> _sourceFactory;
    private readonly ChatSigningScope _signing;
    private DateTimeOffset _nextSendAt = DateTimeOffset.MinValue;

    internal ChatActions(
        IPacketSink sink,
        ClientSessionServices services,
        CommandCompletionService completion,
        CommandService<ClientCommandSource> commands,
        Func<ClientCommandSource> sourceFactory,
        ChatSigningScope signing)
    {
        _sink = sink;
        _services = services;
        _completion = completion;
        _commands = commands;
        _sourceFactory = sourceFactory;
        _signing = signing;
    }

    /// <summary>Sends a chat line the way typing it does: a leading <c>/</c> routes to the command path (the slash is preserved by <see cref="SendCommandAsync"/>), anything else is sent as chat.</summary>
    /// <remarks>Vanilla input semantics, not product policy: pre-1.19 the server routes a leading slash out of <c>handleChat</c> into the dispatcher, and from 1.19 the client itself picks the packet, which is exactly this branch. No trim and no <c>//</c> stripping happen here: a doubled leading slash is not collapsed, so <c>//foo</c> is sent as the command <c>/foo</c> rather than as chat text with an escaped slash. That escape, if a host wants one, is product UX layered on top of this method, not part of it.</remarks>
    /// <param name="message">The line as typed, including any leading slash.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public Task SendAsync(string message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.StartsWith('/') ? SendCommandAsync(message, ct) : SendChatAsync(message, ct);
    }

    /// <summary>Sends a chat message, honoring cooldown and splitting past the length limit.</summary>
    public async Task SendChatAsync(string message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (string chunk in Split(message))
        {
            await ThrottleAsync(ct).ConfigureAwait(false);
            await SendMessageAsync(chunk, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Sends one chat message chunk, signed on every signing era when certificates are available.</summary>
    /// <remarks>
    /// <para>The three eras differ in what the signature covers and what rides beside it:</para>
    /// <list type="bullet">
    /// <item><description>
    /// v1 (protocol 759): salt, timestamp, and the message, with no acknowledgement.
    /// </description></item>
    /// <item><description>
    /// v2 (protocol 760): a two-stage signature over a body that FOLDS IN the 5-entry last-seen window, chained onto the previous message's signature, with the same window repeated on the packet.
    /// </description></item>
    /// <item><description>
    /// v3 (761+): the linked message body plus the offset/bitset acknowledgement window.
    /// </description></item>
    /// </list>
    /// <para>In every era the window used for the SIGNATURE and the window written on the PACKET come from one snapshot: a server that re-derives the body from the packet's window has to arrive at the bytes that were signed. Offline, or with no certificates, the send falls back to the unsigned path, byte-identical to what an offline session emits.</para>
    /// </remarks>
    private async Task SendMessageAsync(string text, CancellationToken ct)
    {
        if (_services.Version.Features.ChatSigning == "none")
        {
            await SendOneAsync(text, ct).ConfigureAwait(false);
            return;
        }

        // The whole of resolve, sign and send runs inside the scope. Resolving here and signing after the scope closed would let a concurrent send's profile-key rotation land its chat_session_update between this message's signature and its frame, which the server answers with a disconnect.
        await _signing(
            async (signing, token) => await SendMessageInScopeAsync(text, signing, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    /// <summary>The signed-chat body, run with <paramref name="signing"/> pinned by the caller's scope. Falls through to the unsigned send when there are no certificates or the era has no signed shape.</summary>
    private async ValueTask SendMessageInScopeAsync(string text, ChatSigningState? signing, CancellationToken ct)
    {
        if (signing is not null)
        {
            long timestampMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long salt = Random.Shared.NextInt64();
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampMillis);

            switch (signing.Era)
            {
                case ChatSignatureEra.V1_19_3 when signing.Tracker is { } tracker:
                    {
                        // The signed body must acknowledge exactly the signatures the packet's bitset marks, so the ack window and the last-seen list for the signature come from the same tracker snapshot.
                        LastSeenMessagesUpdate ack = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);
                        var lastSeen = new AcknowledgedMessage[acknowledged.Count];
                        for (int i = 0; i < acknowledged.Count; i++)
                            lastSeen[i] = new AcknowledgedMessage(Guid.Empty, acknowledged[i]);

                        byte[] signature = signing.Session.Sign(
                            signing.Certificates, signing.Era, text, timestamp, salt, lastSeen);

                        await _sink.SendAsync(
                            new ServerboundSignedChatPacket(text, timestampMillis, salt, signature, ack), ct)
                            .ConfigureAwait(false);
                        return;
                    }

                case ChatSignatureEra.V1_19:
                    {
                        byte[] signature = signing.Session.Sign(
                            signing.Certificates, signing.Era, text, timestamp, salt, []);

                        await _sink.SendAsync(
                            new ServerboundSignedChatPacket(text, timestampMillis, salt, signature, EmptyAck()), ct)
                            .ConfigureAwait(false);
                        return;
                    }

                case ChatSignatureEra.V1_19_1:
                    {
                        IReadOnlyList<AcknowledgedMessage> window = LegacyWindow(signing);
                        byte[] signature = signing.Session.Sign(
                            signing.Certificates, signing.Era, text, timestamp, salt, window);

                        await _sink.SendAsync(
                            new ServerboundSignedChatPacket(text, timestampMillis, salt, signature, EmptyAck())
                            {
                                LegacyLastSeen = ToWireEntries(window),
                            },
                            ct).ConfigureAwait(false);
                        return;
                    }

                default:
                    break;
            }
        }

        await SendOneAsync(text, ct).ConfigureAwait(false);
    }

    /// <summary>Sends a server command (with or without the leading slash) on the wire that actually EXECUTES it.</summary>
    /// <remarks>
    /// <para>Below 1.19 (protocol 759), the command travels as ordinary chat text with a leading slash, which the server routes to its command dispatcher.</para>
    /// <para>From 1.19 onward, chat no longer executes commands. Protocols 759-765 send <c>minecraft:chat_command</c>, and 766 onward send the bare unsigned <c>minecraft:chat_command</c> when the parsed command has no signable arguments and <c>minecraft:chat_command_signed</c> when it does.</para>
    /// <para>When a signing session with certificates is present, each argument the reconstructed server command tree marks signable (for example the message of <c>/msg</c>) is signed and carried on the packet. Every argument is signed over the SAME timestamp, salt and last-seen snapshot the packet declares, which lets the server re-derive each argument body. Without certificates the argument list goes out empty, which an <c>enforce-secure-profile=false</c> server executes normally.</para>
    /// </remarks>
    public async Task SendCommandAsync(string command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        string trimmed = command.StartsWith('/') ? command[1..] : command;
        await ThrottleAsync(ct).ConfigureAwait(false);

        int protocol = _services.Version.Version.Protocol;
        if (protocol < FirstChatCommandProtocol)
        {
            await SendOneAsync("/" + trimmed, ct).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<SignedArgumentSpan> spans = GetSignedArguments(trimmed);
        if (protocol >= FirstSplitChatCommandProtocol && spans.Count == 0)
        {
            await _sink.SendAsync(new ServerboundChatCommandPacket(trimmed), ct).ConfigureAwait(false);
            return;
        }

        // Same ordering contract as signed chat: resolve, sign every argument and send, all inside one scope, so a concurrent profile-key rotation cannot slip its chat_session_update between the signatures and the frame they belong to.
        await _signing(
            async (signing, token) =>
                await SendCommandInScopeAsync(trimmed, protocol, spans, signing, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    /// <summary>The signed-command body, run with <paramref name="signing"/> pinned by the caller's scope.</summary>
    private async ValueTask SendCommandInScopeAsync(
        string trimmed,
        int protocol,
        IReadOnlyList<SignedArgumentSpan> spans,
        ChatSigningState? signing,
        CancellationToken ct)
    {
        long timestampMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long salt = Random.Shared.NextInt64();

        // One acknowledgement snapshot per packet: the window every argument signature covers and the window the packet declares must be the same one, so both come from a single read, exactly as SendMessageAsync does for signed chat. Which window that is, is an era property: v3 uses the offset/bitset tracker, v2 the 5-entry collector, v1 nothing at all.
        var ack = new LastSeenMessagesUpdate(0, new byte[AckBitsetBytes], 0);
        IReadOnlyList<AcknowledgedMessage> lastSeen = [];
        if (signing is { Era: ChatSignatureEra.V1_19_3, Tracker: { } tracker })
        {
            ack = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);
            var window = new AcknowledgedMessage[acknowledged.Count];
            for (int i = 0; i < acknowledged.Count; i++)
                window[i] = new AcknowledgedMessage(Guid.Empty, acknowledged[i]);

            lastSeen = window;
        }
        else if (signing is not null)
            lastSeen = LegacyWindow(signing);

        var arguments = new List<SignedCommandArgument>(spans.Count);
        if (signing is not null)
        {
            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampMillis);
            foreach (SignedArgumentSpan span in spans)
            {
                byte[] signature = signing.Session.Sign(
                    signing.Certificates, signing.Era, span.Value, timestamp, salt, lastSeen);
                arguments.Add(new SignedCommandArgument(span.Name, signature));
            }
        }

        object packet = protocol >= FirstSplitChatCommandProtocol
            ? new ServerboundChatCommandSignedPacket(trimmed, timestampMillis, salt, arguments, ack)
            : new ServerboundSignedChatCommandPacket(trimmed, timestampMillis, salt, arguments, ack)
            {
                // 760 repeats the signed window on the packet; 759 and 761+ leave it empty (761+ declare theirs through the ack bitset instead).
                LegacyLastSeen = signing?.Era == ChatSignatureEra.V1_19_1 ? ToWireEntries(lastSeen) : [],
            };
        await _sink.SendAsync(packet, ct).ConfigureAwait(false);
    }

    /// <summary>The v2 (1.19.1/1.19.2) last-seen window: a snapshot of the collector the chat applier feeds with every inbound signed message, newest first. Empty on any other era and when no collector is installed (which is the case for a non-signing session).</summary>
    private static IReadOnlyList<AcknowledgedMessage> LegacyWindow(ChatSigningState signing) =>
        signing is { Era: ChatSignatureEra.V1_19_1, LegacyCollector: { } collector } ? collector.Snapshot() : [];

    /// <summary>Maps the signing-side window onto the v2 packet entries; the ORDER is what was signed.</summary>
    private static LastSeenMessageEntry[] ToWireEntries(IReadOnlyList<AcknowledgedMessage> window)
    {
        var entries = new LastSeenMessageEntry[window.Count];
        for (int i = 0; i < window.Count; i++)
            entries[i] = new LastSeenMessageEntry(window[i].ProfileId, window[i].Signature);

        return entries;
    }

    private static LastSeenMessagesUpdate EmptyAck() => new(0, new byte[AckBitsetBytes], 0);

    /// <summary>Merged command completion at the cursor: unions host-command suggestions (<see cref="CommandService{TSource}"/>), the local server-command-tree walk (literals and locally resolvable arguments), and, when the tree marks the token as server-driven, the server tab-complete round trip. The input includes the leading slash for a command; a non-command input yields no suggestions from the trees.</summary>
    public async Task<CompletionResult> CompleteAsync(string input, int cursor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (cursor < 0 || cursor > input.Length)
            throw new ArgumentOutOfRangeException(nameof(cursor));

        var merged = ImmutableArray.CreateBuilder<CompletionSuggestion>();
        int rangeStart = cursor;
        int rangeEnd = cursor;
        bool any = false;

        // Host commands (registered against the client's CommandService).
        CompletionResult host = await _commands.CompleteAsync(input, cursor, _sourceFactory(), ct).ConfigureAwait(false);
        any = Absorb(host, merged, ref rangeStart, ref rangeEnd, any);

        // Server command tree: local literals plus, if needed, the server round trip. The tree is walked on the command body (without the leading slash).
        ServerCommandTree? tree = _services.State.ServerCommands.Tree;
        if (tree is not null && input.StartsWith('/'))
        {
            string body = input[1..];
            int bodyCursor = Math.Max(0, cursor - 1);

            CompletionResult local = tree.CompleteLocally(body, bodyCursor);
            any = Absorb(Shift(local, 1), merged, ref rangeStart, ref rangeEnd, any);

            if (tree.NeedsServerCompletion(body, bodyCursor))
                try
                {
                    CompletionResult server = await _completion.RequestAsync(input[..cursor], ct).ConfigureAwait(false);
                    any = Absorb(server, merged, ref rangeStart, ref rangeEnd, any);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _services.Logger.LogDebug(ex, "Server command suggestion round trip failed; using local completions only.");
                }

        }

        return any ? new CompletionResult(rangeStart, rangeEnd, merged.ToImmutable()) : CompletionResult.Empty;
    }

    /// <summary>The signed argument spans of a command body (without the leading slash), per the reconstructed server command tree. Empty when no server tree is known or nothing is signable.</summary>
    public IReadOnlyList<SignedArgumentSpan> GetSignedArguments(string commandBody)
    {
        ArgumentNullException.ThrowIfNull(commandBody);
        ServerCommandTree? tree = _services.State.ServerCommands.Tree;
        return tree is null ? [] : tree.GetSignedArguments(commandBody);
    }

    private static bool Absorb(
        CompletionResult result,
        ImmutableArray<CompletionSuggestion>.Builder into,
        ref int rangeStart,
        ref int rangeEnd,
        bool any)
    {
        if (result.Suggestions.IsDefaultOrEmpty)
            return any;

        if (!any)
        {
            rangeStart = result.RangeStart;
            rangeEnd = result.RangeEnd;
        }
        else
        {
            rangeStart = Math.Min(rangeStart, result.RangeStart);
            rangeEnd = Math.Max(rangeEnd, result.RangeEnd);
        }

        foreach (CompletionSuggestion s in result.Suggestions)
            into.Add(s);

        return true;
    }

    private static CompletionResult Shift(CompletionResult result, int offset)
    {
        if (offset == 0 || result.Suggestions.IsDefaultOrEmpty)
            return result;

        var builder = ImmutableArray.CreateBuilder<CompletionSuggestion>(result.Suggestions.Length);
        foreach (CompletionSuggestion s in result.Suggestions)
            builder.Add(s with { StartIndex = s.StartIndex + offset, EndIndex = s.EndIndex + offset });

        return new CompletionResult(result.RangeStart + offset, result.RangeEnd + offset, builder.MoveToImmutable());
    }

    private async Task SendOneAsync(string text, CancellationToken ct)
    {
        if (_services.Version.Features.ChatSigning == "none")
        {
            await _sink.SendAsync(new ServerboundLegacyChatPacket(text), ct).ConfigureAwait(false);
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long salt = Random.Shared.NextInt64();

        // The 1.19.3+ last-seen acknowledgment carries a fixed-width bitset over the 20-message window (ceil(20/8) = 3 bytes). An offline, unsigned client acknowledges nothing: offset 0, empty bitset, checksum 0.
        var lastSeen = new LastSeenMessagesUpdate(0, new byte[3], 0);
        await _sink.SendAsync(
            new ServerboundSignedChatPacket(text, now, salt, Signature: null, lastSeen), ct)
            .ConfigureAwait(false);
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        TimeSpan cooldown = _services.Options.ChatCooldown;
        if (cooldown <= TimeSpan.Zero)
            return;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < _nextSendAt)
            await Task.Delay(_nextSendAt - now, ct).ConfigureAwait(false);

        _nextSendAt = DateTimeOffset.UtcNow + cooldown;
    }

    private static IEnumerable<string> Split(string message)
    {
        if (message.Length <= MaxChatLength)
        {
            yield return message;
            yield break;
        }

        for (int i = 0; i < message.Length; i += MaxChatLength)
            yield return message.Substring(i, Math.Min(MaxChatLength, message.Length - i));

    }
}

/// <summary>The pieces the command send path needs to sign a signable argument: the session state machine, the player's certificates, and the signature era. Present only when the session is authenticated with a key pair; offline sessions supply null and commands go unsigned.</summary>
/// <param name="Session">The per-session signing state machine.</param>
/// <param name="Certificates">The player's key pair and Mojang signatures.</param>
/// <param name="Era">The signature payload era for this version.</param>
public sealed record ChatSigningState(ChatSigningSession Session, PlayerCertificates Certificates, ChatSignatureEra Era)
{
    /// <summary>The 1.19.3+ last-seen acknowledgement tracker that builds the offset/bitset window for outbound signed chat. Null on the 1.19/1.19.1 eras and on non-signing sessions.</summary>
    public LastSeenMessagesTracker? Tracker { get; init; }

    /// <summary>The 1.19.1/1.19.2 last-seen collector: the 5-entry add-to-front window whose (uuid, signature) pairs are folded into the v2 signed body and repeated on the packet. Null on the 1.19 and 1.19.3+ eras and on non-signing sessions.</summary>
    public LastSeenMessagesCollector? LegacyCollector { get; init; }
}
