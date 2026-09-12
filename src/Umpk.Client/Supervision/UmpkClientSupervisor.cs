using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Protocol.Java;

namespace Umpk.Client;

/// <summary>Owns a <see cref="UmpkClient"/> session lifetime across many connections: one attempt to reach play, then a background watch that reconnects on an unexpected disconnect per an <see cref="IReconnectPolicyProvider"/>, until the policy gives up, refuses, or the host stops it.</summary>
/// <remarks>
/// <para><see cref="StartAsync"/> runs its first attempt inline and returns once play is live; from then on the session's fate is watched in the background. The sequence for one attempt is: transition to <see cref="ClientStatus.Connecting"/>, call <see cref="IClientSessionFactory.CreateAsync"/> (which may call <see cref="SessionAttempt.ReportAuthenticating"/>, transitioning to <see cref="ClientStatus.Authenticating"/> and back to <see cref="ClientStatus.Connecting"/>), then call <see cref="UmpkClient.ConnectAsync"/>. Success transitions to <see cref="ClientStatus.Playing"/>. On failure the partial client is released, the supervisor transitions to <see cref="ClientStatus.Disconnected"/>, and the failure is thrown as a typed exception: <see cref="ConnectionClosedException"/> carrying <see cref="CloseReason.DisconnectMessage"/> becomes <see cref="LoginRejectedException"/> (carrying the server's own decoded reason); any other non-cancellation fault out of <see cref="UmpkClient.ConnectAsync"/> becomes <see cref="ConnectFailedException"/>; an exception from the factory itself (a <see cref="VersionResolutionException"/>, an <c>Umpk.Auth</c> failure) propagates untouched, because the supervisor cannot name a failure in a step it does not own.</para>
/// <para>After a live session ends unexpectedly (the read side observed a close, or a host called <see cref="ReportSessionFault"/>), the retry decision follows the configured reconnect policy. A local stop or a lifetime cancellation is always terminal. Otherwise <see cref="IReconnectPolicyProvider.GetReconnectPolicy"/> is pulled and <see cref="ReconnectPolicy.IsRetryable"/> applied; a refusal is terminal. Otherwise the loop retries, bounded by <see cref="ReconnectPolicy.MaxAttempts"/> (negative is unlimited): each iteration transitions to <see cref="ClientStatus.Reconnecting"/>, releases the dead client, waits <see cref="ReconnectPolicy.DelayFor"/> on <see cref="ClientSupervisorOptions.TimeProvider"/>, then runs one attempt. A <see cref="ConnectFailedException"/> or <see cref="VersionResolutionException"/> on that attempt re-pulls the policy and retries (a null re-pull stops the loop); a <see cref="LoginRejectedException"/> or any other factory exception stops it, because a rejected login will not succeed on retry and the host's own step failing is the host's call, not the supervisor's.</para>
/// <para><see cref="Completed"/> never faults: whatever ended the supervisor - a clean stop, a policy refusal, exhausted attempts, or an unretryable failure - is reported through <see cref="LastDisconnect"/> and through the terminal <see cref="StatusChanged"/> transition, never through the awaitable's exception.</para>
/// </remarks>
public sealed class UmpkClientSupervisor : IAsyncDisposable
{
    private readonly IClientSessionFactory _factory;
    private readonly ClientSupervisorOptions _options;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _started;
    private int _stopped;
    private int _disposed;
    private ServerEndpoint? _endpoint;
    private UmpkClient? _client;
    private DisconnectInfo? _lastDisconnect;
    private ClientStatus _status = ClientStatus.Created;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _supervisionTask;
    private TaskCompletionSource<DisconnectInfo>? _sessionEnded;
    private TaskCompletionSource<ServerEndpoint>? _transferRequested;
    private IReadOnlyList<ExtensionSessionPlugin> _extensionSessions = [];

    /// <summary>Creates the supervisor. It owns nothing until <see cref="StartAsync"/> is called.</summary>
    public UmpkClientSupervisor(IClientSessionFactory factory, ClientSupervisorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _options = options ?? new ClientSupervisorOptions();
        _logger = _options.LoggerFactory.CreateLogger("Umpk.Client.Supervision.UmpkClientSupervisor");
        Completed = _completed.Task;
        Extensions = new ClientExtensionCollection(this, _options);
    }

    /// <summary>Raised on every status transition; see <see cref="UmpkClient.StatusChanged"/> for the contract.</summary>
    public event EventHandler<ClientStatusChangedEventArgs>? StatusChanged;

    /// <summary>The supervisor's own lifecycle status, distinct from any one <see cref="UmpkClient"/>'s: it spans reconnects, so <see cref="ClientStatus.Reconnecting"/> and the terminal <see cref="ClientStatus.Disconnected"/> are its to report and no single session's. The one thing it takes FROM the live session is the play-to-configuration re-entry, so that a host reading this during a proxy's backend switch is not told it is playing; see <see cref="OnSessionStatusChanged"/>.</summary>
    public ClientStatus Status => _status;

    /// <summary>The currently live client, or null before the first successful attempt and after the terminal transition.</summary>
    public UmpkClient? Client => _client;

    /// <summary>The endpoint the supervisor is currently dialing or connected to.</summary>
    public ServerEndpoint? Endpoint => _endpoint;

    /// <summary>The reason the most recent session ended, or null before any session has ended.</summary>
    public DisconnectInfo? LastDisconnect => _lastDisconnect;

    /// <summary>Completes when the supervisor reaches its terminal <see cref="ClientStatus.Disconnected"/>. Never faults.</summary>
    public Task Completed { get; }

    /// <summary>Extensions kept activated for as long as they are registered, spanning every reconnect this supervisor makes. See <see cref="ClientExtensionContext"/> for how this differs from a per-session <see cref="Plugins.IClientPlugin"/> registered through one <see cref="UmpkClient.Plugins"/>.</summary>
    public ClientExtensionCollection Extensions { get; }

    /// <summary>Resolves the endpoint, builds and connects the first client, and returns once play is live with background supervision armed. Throws <see cref="ConnectFailedException"/>, <see cref="LoginRejectedException"/>, or whatever the factory itself threw, on failure.</summary>
    public async Task StartAsync(ServerEndpoint endpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (_started)
            throw new InvalidOperationException("This supervisor has already been started.");

        _started = true;
        _endpoint = endpoint;

        var lifetime = new CancellationTokenSource();
        _lifetimeCts = lifetime;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);

        UmpkClient client;
        try
        {
            client = await RunOneAttemptAsync(endpoint, attemptNumber: 0, previous: null, linked.Token).ConfigureAwait(false);
        }
        catch
        {
            CompleteTerminal(_lastDisconnect ?? new DisconnectInfo { Reason = CloseReason.Local });
            throw;
        }

        _client = client;
        SetStatus(ClientStatus.Playing);
        ArmSupervision(lifetime.Token);
    }

    /// <summary>Tears the current session down, optionally adopts a new endpoint, and dials one fresh attempt inline, re-arming background supervision on success. The host's own credential switch is simply what its <see cref="IClientSessionFactory"/> returns on the next <see cref="SessionAttempt"/>.</summary>
    public async Task ReconnectAsync(ServerEndpoint? endpoint = null, CancellationToken ct = default)
    {
        if (!_started)
            throw new InvalidOperationException("The supervisor must be started before it can reconnect.");

        if (Volatile.Read(ref _stopped) == 1)
            throw new InvalidOperationException("The supervisor has already been stopped.");

        await TeardownCurrentSessionAsync(ct).ConfigureAwait(false);

        ServerEndpoint target = endpoint ?? _endpoint
            ?? throw new InvalidOperationException("No endpoint to reconnect to.");
        _endpoint = target;

        SetStatus(ClientStatus.Reconnecting);

        var lifetime = new CancellationTokenSource();
        _lifetimeCts = lifetime;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);

        UmpkClient client;
        try
        {
            client = await RunOneAttemptAsync(target, attemptNumber: 0, previous: null, linked.Token).ConfigureAwait(false);
        }
        catch
        {
            CompleteTerminal(_lastDisconnect ?? new DisconnectInfo { Reason = CloseReason.Local });
            throw;
        }

        _client = client;
        SetStatus(ClientStatus.Playing);
        ArmSupervision(lifetime.Token);
    }

    /// <summary>Disconnects the current session cleanly and stops any further reconnect. Idempotent.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;

        CancellationTokenSource? lifetime = Interlocked.Exchange(ref _lifetimeCts, null);
        if (lifetime is not null)
            await lifetime.CancelAsync().ConfigureAwait(false);

        await SafeAwaitAsync(_supervisionTask).ConfigureAwait(false);

        UmpkClient? client = _client;
        if (client is not null)
            try
            {
                await client.DisconnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting the live client during stop.");
            }

        var info = new DisconnectInfo { Reason = CloseReason.Local };
        await ReleaseCurrentClientAsync(info).ConfigureAwait(false);

        // Every registered extension is deactivated, in reverse registration order, before the terminal completion below: a host awaiting Completed may then assume every extension is down.
        await Extensions.DeactivateAllAsync(ct).ConfigureAwait(false);

        CompleteTerminal(info);

        lifetime?.Dispose();
    }

    /// <summary>Reports a session-fatal fault the read side cannot see on its own - a failed write on a half-open connection, invisible until the idle backstop eventually fires. Ends the current session as if the read side had observed <paramref name="info"/>; the reconnect policy then applies exactly as it would for a real read-side loss. A no-op before the first session starts or after the terminal transition.</summary>
    public void ReportSessionFault(DisconnectInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _sessionEnded?.TrySetResult(info);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during supervisor dispose.");
        }
    }

    // Run one connection attempt.

    /// <summary>Runs exactly one dial: build through the factory, then connect. On any failure the built client (when one was built) is released, <see cref="_lastDisconnect"/> is updated, and a typed exception is thrown per the class-level contract. Factory exceptions are not caught here, so they propagate untouched to the caller.</summary>
    private async Task<UmpkClient> RunOneAttemptAsync(
        ServerEndpoint endpoint,
        int attemptNumber,
        DisconnectInfo? previous,
        CancellationToken ct,
        bool transferIntent = false,
        IReadOnlyDictionary<Identifier, byte[]>? transferredCookies = null,
        GameProfile? expectedProfile = null,
        int transferHop = 0)
    {
        SetStatus(ClientStatus.Connecting);

        var attempt = new SessionAttempt(
            endpoint, attemptNumber, previous, () => SetStatus(ClientStatus.Authenticating), transferIntent);
        UmpkClient client = await _factory.CreateAsync(attempt, ct).ConfigureAwait(false);

        if (expectedProfile is { } expected
            && ((expected.Id != Guid.Empty && client.ConfiguredProfile.Id != expected.Id)
                || !string.Equals(client.ConfiguredProfile.Name, expected.Name, StringComparison.Ordinal)))
        {
            var mismatch = new InvalidOperationException(
                "A server transfer must preserve the source session's configured profile identity.");
            var mismatchInfo = new DisconnectInfo { Reason = CloseReason.Local, Fault = mismatch };
            await _factory.ReleaseAsync(client, mismatchInfo, CancellationToken.None).ConfigureAwait(false);
            throw mismatch;
        }

        if (transferredCookies is not null)
            client.Cookies.ImportOwned(transferredCookies);

        // The factory may have sent this attempt somewhere else (SessionAttempt.Redirect). Read the target back rather than reusing the argument, or the client is built for one address and dialed at another.
        ServerEndpoint target = attempt.Endpoint;

        // Back to Connecting whether or not the factory reported Authenticating: that step is done, and dialing is next.
        SetStatus(ClientStatus.Connecting);

        // Subscribed BEFORE ConnectAsync is even called, not after it returns: the session can end on its own receive loop the instant play begins - a kick or a dropped peer racing the return from ConnectAsync - and a subscription installed only after that return can miss the event outright.
        var ended = new TaskCompletionSource<DisconnectInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<Disconnected>(d => ended.TrySetResult(d.Info));
        var transfer = new TaskCompletionSource<ServerEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<ServerTransferRequested>(request =>
            transfer.TrySetResult(new ServerEndpoint(request.Host, checked((ushort)request.Port))));

        // Same reason, for the play <-> configuration re-entry: see OnSessionStatusChanged.
        client.StatusChanged += OnSessionStatusChanged;

        // Every currently-registered extension gets its shot at this client before it ever dials: raise SessionCreated (the client is still unconnected) and bridge it into the client's own plugin collection so SessionStarted fires once play is reached.
        IReadOnlyList<ExtensionSessionPlugin> extensionSessions =
            await Extensions.AttachForNewAttemptAsync(client, ct).ConfigureAwait(false);

        try
        {
            if (transferIntent)
                await client.ConnectForTransferAsync(target, ct).ConfigureAwait(false);
            else
                await client.ConnectAsync(target, ct).ConfigureAwait(false);
            _sessionEnded = ended;
            _transferRequested = transfer;
            _extensionSessions = extensionSessions;
            _endpoint = client.Session?.Endpoint ?? target;
            return client;
        }
        catch (OperationCanceledException)
        {
            _lastDisconnect = new DisconnectInfo { Reason = CloseReason.Cancelled };
            EndExtensionSessions(extensionSessions, _lastDisconnect);
            await _factory.ReleaseAsync(client, _lastDisconnect, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (_options.FollowServerTransfers
            && !ct.IsCancellationRequested
            && transfer.Task.IsCompletedSuccessfully)
        {
            DisconnectInfo info = client.LastDisconnect
                ?? new DisconnectInfo { Reason = CloseReason.SocketEof, Fault = ex };
            IReadOnlyDictionary<Identifier, byte[]> snapshot = client.Cookies.SnapshotOwned();
            GameProfile profile = client.ConfiguredProfile;
            ServerEndpoint destination = await transfer.Task.ConfigureAwait(false);
            EndExtensionSessions(extensionSessions, info);
            await _factory.ReleaseAsync(client, info, CancellationToken.None).ConfigureAwait(false);

            if (transferHop >= _options.MaxTransferHops)
                throw new ConnectFailedException(
                    $"Server transfer chain exceeded {_options.MaxTransferHops} hops.", destination, ex);

            _endpoint = destination;
            return await RunOneAttemptAsync(
                destination,
                attemptNumber: 0,
                previous: info,
                ct,
                transferIntent: true,
                transferredCookies: snapshot,
                expectedProfile: profile,
                transferHop: transferHop + 1).ConfigureAwait(false);
        }
        catch (ConnectionClosedException ex) when (ex.Reason == CloseReason.DisconnectMessage)
        {
            DisconnectInfo info = client.LastDisconnect
                ?? new DisconnectInfo { Reason = ex.Reason, Message = ex.Disconnect, Fault = ex };
            _lastDisconnect = info;
            EndExtensionSessions(extensionSessions, info);
            await _factory.ReleaseAsync(client, info, CancellationToken.None).ConfigureAwait(false);
            throw new LoginRejectedException(ex.Message, ex.Disconnect, ex);
        }
        catch (Exception ex)
        {
            DisconnectInfo info = client.LastDisconnect
                ?? new DisconnectInfo { Reason = CloseReason.ProtocolViolation, Fault = ex };
            _lastDisconnect = info;
            EndExtensionSessions(extensionSessions, info);
            await _factory.ReleaseAsync(client, info, CancellationToken.None).ConfigureAwait(false);
            throw new ConnectFailedException($"Could not connect to {target}.", target, ex);
        }
    }

    // Supervise a connected client in the background.

    /// <summary>Arms the background watch for the client <see cref="RunOneAttemptAsync"/> just connected. <see cref="_sessionEnded"/> is already subscribed (set by that call, before it ever dialed), so this only has to start the loop that awaits it.</summary>
    private void ArmSupervision(CancellationToken lifetimeCt) => _supervisionTask = SuperviseAsync(_sessionEnded!, lifetimeCt);

    /// <summary>The background loop that owns a running session's fate: wait for it to end, then either report a terminal stop or drive policy-based reconnect until it re-joins or gives up. Cancelled cleanly by an intentional teardown (<see cref="StopAsync"/> or <see cref="ReconnectAsync"/> owns the resulting transition in that case, so this returns without completing anything).</summary>
    private async Task SuperviseAsync(TaskCompletionSource<DisconnectInfo> ended, CancellationToken lifetimeCt)
    {
        int transferHops = 0;
        while (true)
        {
            DisconnectInfo info;
            try
            {
                Task<ServerEndpoint>? transferTask = _options.FollowServerTransfers
                    ? _transferRequested?.Task
                    : null;
                Task winner = transferTask is null
                    ? ended.Task
                    : await Task.WhenAny(transferTask, ended.Task).WaitAsync(lifetimeCt).ConfigureAwait(false);

                if (transferTask is not null && ReferenceEquals(winner, transferTask))
                {
                    ServerEndpoint destination = await transferTask.ConfigureAwait(false);
                    UmpkClient source = _client
                        ?? throw new InvalidOperationException("A transfer was reported without a live client.");
                    IReadOnlyDictionary<Identifier, byte[]> snapshot = source.Cookies.SnapshotOwned();
                    GameProfile profile = source.ConfiguredProfile;
                    info = new DisconnectInfo { Reason = CloseReason.SocketEof };
                    _lastDisconnect = info;
                    EndExtensionSessions(_extensionSessions, info);
                    await ReleaseCurrentClientAsync(info).ConfigureAwait(false);

                    transferHops++;
                    if (transferHops > _options.MaxTransferHops)
                    {
                        CompleteTerminal(new DisconnectInfo
                        {
                            Reason = CloseReason.Local,
                            Fault = new InvalidOperationException(
                                $"Server transfer chain exceeded {_options.MaxTransferHops} hops."),
                        });
                        return;
                    }

                    try
                    {
                        _endpoint = destination;
                        _client = await RunOneAttemptAsync(
                            destination,
                            attemptNumber: 0,
                            previous: info,
                            lifetimeCt,
                            transferIntent: true,
                            transferredCookies: snapshot,
                            expectedProfile: profile,
                            transferHop: transferHops).ConfigureAwait(false);
                        SetStatus(ClientStatus.Playing);
                        ended = _sessionEnded!;
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception transferError)
                    {
                        CompleteTerminal(_lastDisconnect ?? new DisconnectInfo
                        {
                            Reason = CloseReason.ProtocolViolation,
                            Fault = transferError,
                        });
                        return;
                    }
                }

                info = await ended.Task.WaitAsync(lifetimeCt).ConfigureAwait(false);
                transferHops = 0;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _lastDisconnect = info;

            // Every extension still bridged into the session that just ended gets its SessionEnded here, before the client is released below (by this method directly or, for the retry path, inside ReconnectLoopAsync). This is the eager path: it runs off the session-end signal alone, well before the client's own per-session teardown (Detached) ever fires. See ExtensionSessionPlugin's remarks for why a second, backstop path also exists.
            EndExtensionSessions(_extensionSessions, info);

            if (info.WasLocal || lifetimeCt.IsCancellationRequested)
            {
                await ReleaseCurrentClientAsync(info).ConfigureAwait(false);
                CompleteTerminal(info);
                return;
            }

            ReconnectPolicy? policy = _options.ReconnectPolicy?.GetReconnectPolicy();
            if (!ReconnectPolicy.IsRetryable(policy, info))
            {
                await ReleaseCurrentClientAsync(info).ConfigureAwait(false);
                CompleteTerminal(info);
                return;
            }

            ReconnectAttemptResult result = await ReconnectLoopAsync(info, policy!, lifetimeCt).ConfigureAwait(false);
            if (result.Cancelled)
                return;

            if (result.Client is null)
            {
                CompleteTerminal(_lastDisconnect ?? info);
                return;
            }

            _client = result.Client;
            SetStatus(ClientStatus.Playing);

            // RunOneAttemptAsync already subscribed _sessionEnded for this client before it dialed.
            ended = _sessionEnded!;
        }
    }

    private readonly record struct ReconnectAttemptResult(UmpkClient? Client, bool Cancelled);

    /// <summary>Bounded retry loop for one dead session: delay, then one attempt, repeated per <see cref="ReconnectPolicy.MaxAttempts"/> and re-pulling the policy after each failed attempt. See the class-level remarks for the exact per-attempt exception classification.</summary>
    private async Task<ReconnectAttemptResult> ReconnectLoopAsync(
        DisconnectInfo previousInfo, ReconnectPolicy policy, CancellationToken lifetimeCt)
    {
        int attemptNumber = 0;
        while (!lifetimeCt.IsCancellationRequested)
        {
            if (policy.MaxAttempts >= 0 && attemptNumber >= policy.MaxAttempts)
                return new ReconnectAttemptResult(null, false);

            SetStatus(ClientStatus.Reconnecting);
            await ReleaseCurrentClientAsync(previousInfo).ConfigureAwait(false);

            try
            {
                await Task.Delay(policy.DelayFor(attemptNumber), _options.TimeProvider, lifetimeCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ReconnectAttemptResult(null, true);
            }

            attemptNumber++;

            UmpkClient client;
            try
            {
                client = await RunOneAttemptAsync(_endpoint!, attemptNumber, previousInfo, lifetimeCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ReconnectAttemptResult(null, true);
            }
            catch (LoginRejectedException)
            {
                // A rejected login will not succeed on retry.
                return new ReconnectAttemptResult(null, false);
            }
            catch (Exception ex) when (ex is ConnectFailedException or VersionResolutionException)
            {
                ReconnectPolicy? next = _options.ReconnectPolicy?.GetReconnectPolicy();
                if (next is null)
                    return new ReconnectAttemptResult(null, false);

                policy = next;
                continue;
            }
            catch (Exception ex)
            {
                // Anything else out of the factory is the host's own step failing; retrying is the host's call, not the supervisor's.
                _logger.LogDebug(ex, "Reconnect attempt {Attempt} failed with an unclassified factory fault; stopping.", attemptNumber);
                return new ReconnectAttemptResult(null, false);
            }

            return new ReconnectAttemptResult(client, false);
        }

        return new ReconnectAttemptResult(null, true);
    }

    // Tear down the current session and update its status.

    /// <summary>Cancels and awaits the current supervision loop, then closes and releases the live client.</summary>
    private async Task TeardownCurrentSessionAsync(CancellationToken ct)
    {
        CancellationTokenSource? lifetime = Interlocked.Exchange(ref _lifetimeCts, null);
        if (lifetime is not null)
            await lifetime.CancelAsync().ConfigureAwait(false);

        await SafeAwaitAsync(_supervisionTask).ConfigureAwait(false);

        UmpkClient? client = _client;
        if (client is not null)
            try
            {
                await client.DisconnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting the previous client before reconnecting.");
            }

        await ReleaseCurrentClientAsync(new DisconnectInfo { Reason = CloseReason.Local }).ConfigureAwait(false);
        lifetime?.Dispose();
    }

    /// <summary>Idempotently ends every given attempt's extension sessions (see <see cref="ExtensionSessionPlugin.EndSessionOnce"/>).</summary>
    private static void EndExtensionSessions(IReadOnlyList<ExtensionSessionPlugin> sessions, DisconnectInfo? info)
    {
        foreach (ExtensionSessionPlugin session in sessions)
            session.EndSessionOnce(info);

    }

    private async ValueTask ReleaseCurrentClientAsync(DisconnectInfo? info)
    {
        UmpkClient? client = Interlocked.Exchange(ref _client, null);
        if (client is not null)
        {
            // Detached before the factory disposes it: the mirror below already ignores a non-current client, so this is hygiene rather than correctness, but it keeps a released client from holding the supervisor alive through a handler that can no longer do anything.
            client.StatusChanged -= OnSessionStatusChanged;
            await _factory.ReleaseAsync(client, info, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void CompleteTerminal(DisconnectInfo info)
    {
        _lastDisconnect = info;
        SetStatus(ClientStatus.Disconnected, info);
        _completed.TrySetResult();
    }

    /// <summary>Mirrors the live session's play-to-configuration re-entry (<c>minecraft:start_configuration</c>, 1.20.2+) onto the supervisor's own status, so <see cref="Status"/> stays true for the whole of it.</summary>
    /// <remarks>
    /// The supervisor owns the CONNECT lifecycle and the session owns the PHASE within one connection, and only the first of those two was ever visible here: <see cref="Status"/> was set to <see cref="ClientStatus.Playing"/> once, after the dial, and then stood still until the session ended. A re-entry is neither a connect nor a disconnect, so nothing moved it, and a host asking "am I playing?" during one was told yes while the connection was in fact in configuration and would refuse every play packet.
    /// <para>That is not a hypothetical: a proxy (Velocity, BungeeCord) moves a player between backend servers with exactly this re-entry, and it lasts as long as the new backend's configuration takes. A host that sent a chat message or a command in that window - a perfectly ordinary thing for a user to do while a "sending you to survival" line sits on screen - had the send rejected by <c>JavaConnection.SendAsync</c> as <c>Packet ServerboundChatCommandPacket is not valid to send in phase Configuration</c>, a protocol-layer error surfacing for what is really "not right now".</para>
    /// <para>Two guards keep this to the case it is for. The sender must be the CURRENT client, so a client that was built and then abandoned by a failed attempt can never move the supervisor; and the supervisor must already have reached play, because the inner client walks Connecting -&gt; Configuring -&gt; Playing on its own during every dial and that walk is the supervisor's own <see cref="ClientStatus.Connecting"/>, not a re-entry. Only those two statuses are mirrored: <see cref="ClientStatus.Disconnected"/> is the supervisor's to decide, since it is what the reconnect policy answers to.</para>
    /// </remarks>
    private void OnSessionStatusChanged(object? sender, ClientStatusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _client)
            || _status is not (ClientStatus.Playing or ClientStatus.Configuring))
            return;

        if (e.Current is ClientStatus.Configuring or ClientStatus.Playing)
            SetStatus(e.Current);

    }

    private void SetStatus(ClientStatus next, DisconnectInfo? disconnect = null)
    {
        if (_status == next)
            return;

        ClientStatus previous = _status;
        _status = next;

        EventHandler<ClientStatusChangedEventArgs>? handler = StatusChanged;
        if (handler is null)
            return;

        var args = new ClientStatusChangedEventArgs(previous, next, next == ClientStatus.Disconnected ? disconnect : null);
        try
        {
            handler(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A StatusChanged handler threw for the {Previous} -> {Current} transition.", previous, next);
        }
    }

    private static async Task SafeAwaitAsync(Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The supervision loop's own exception handling never lets an exception escape it; this is a defensive backstop only.
        }
    }
}
