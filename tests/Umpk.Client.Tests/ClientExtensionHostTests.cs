using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.TestKit.Time;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="IClientExtension"/> and <see cref="ClientExtensionCollection"/>: an extension lifetime that spans reconnects, layered above <see cref="ClientPluginCollection"/>'s per-session model. A plugin (<c>client.Plugins</c>) lives for one session; an extension (<c>supervisor.Extensions</c>) lives for the whole supervised run and sees <see cref="ClientExtensionContext.SessionCreated"/>, <see cref="ClientExtensionContext.SessionStarted"/> and <see cref="ClientExtensionContext.SessionEnded"/> once per connection, including across an automatic reconnect.</summary>
public sealed class ClientExtensionHostTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly ServerEndpoint Endpoint = new("test", 25565);

    private static ReconnectPolicy ShortPolicy(int maxAttempts = 3) => new()
    {
        MaxAttempts = maxAttempts,
        InitialDelay = TimeSpan.FromMilliseconds(10),
        BackoffFactor = 1.0,
    };

    [Fact]
    public async Task AddAsync_ActivatesOnce_EvenWithNoSession()
    {
        var factory = new SequencedSessionFactory().Then(NeverDial());
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-a");

        ClientExtensionRegistration registration = await supervisor.Extensions.AddAsync(extension);

        Assert.Equal(1, extension.ActivateCount);
        Assert.True(supervisor.Extensions.Contains("ext-a"));
        Assert.Contains("ext-a", supervisor.Extensions.Ids);
        Assert.Equal("ext-a", registration.Id);
        Assert.NotNull(extension.Context);
        Assert.False(extension.Context!.InSession);
        Assert.Null(extension.Context.Session);
    }

    [Fact]
    public async Task AddAsync_DuringALiveSession_GetsTheLiveSessionImmediately()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);

        var extension = new RecordingExtension("ext-live");
        ClientExtensionRegistration registration = await supervisor.Extensions.AddAsync(extension, ct);

        Assert.Equal(1, extension.ActivateCount);
        // The client already dialed before this extension existed, so SessionCreated is not replayed.
        Assert.Empty(extension.SessionCreatedEvents);
        Assert.Single(extension.SessionStartedContexts);
        Assert.True(extension.Context!.InSession);
        Assert.NotNull(extension.Context.Session);
        Assert.Equal("ext-live", registration.Id);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task SessionCreated_FiresBeforeTheClientDials()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-created");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);

        (UmpkClient Client, ClientStatus Status) created = Assert.Single(extension.SessionCreatedEvents);
        Assert.Equal(ClientStatus.Created, created.Status);
        Assert.Same(supervisor.Client, created.Client);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task SessionStarted_FiresOncePerConnection()
    {
        FakeJavaServer server1 = FakeJavaServer.Create();
        FakeJavaServer server2 = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory()
            .Then(SucceedThenDrop(server1))
            .Then(Succeed(server2));

        var options = new ClientSupervisorOptions { ReconnectPolicy = new FixedReconnectPolicyProvider(ShortPolicy()) };
        await using var supervisor = new UmpkClientSupervisor(factory, options);
        var extension = new RecordingExtension("ext-started-twice");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        TaskCompletionSource secondPlaying = PlayingCounter(supervisor, atLeast: 2);

        await supervisor.StartAsync(Endpoint, ct);
        await secondPlaying.Task.WaitAsync(Budget, ct);

        Assert.Equal(2, extension.SessionStartedContexts.Count);
        Assert.NotSame(extension.SessionStartedContexts[0], extension.SessionStartedContexts[1]);
        Assert.Equal(2, extension.SessionCreatedEvents.Count);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task SessionEnded_FiresBeforeThePerSessionTeardown()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(SucceedThenDrop(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-teardown");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        bool? detachedAtSessionEnded = null;
        extension.Context!.SessionEnded += (_, _) =>
        {
            detachedAtSessionEnded = extension.SessionStartedContexts[^1].Detached.IsCancellationRequested;
        };

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.NotNull(detachedAtSessionEnded);
        Assert.False(detachedAtSessionEnded);
    }

    [Fact]
    public async Task SessionEnded_FiresWithoutWaitingForTheClientToBeReleased()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(SucceedThenDrop(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-norelease");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        bool? clientReleasedAtSessionEnded = null;
        extension.Context!.SessionEnded += (_, _) => clientReleasedAtSessionEnded = factory.ReleaseCalls > 0;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.NotNull(clientReleasedAtSessionEnded);
        Assert.False(clientReleasedAtSessionEnded);
        Assert.Equal(1, factory.ReleaseCalls);
    }

    [Fact]
    public async Task SessionEnded_IsRaisedExactlyOnce_WhenBothPathsFire()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(SucceedThenDrop(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-once");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.Completed.WaitAsync(Budget, ct);

        Assert.Single(extension.SessionEndedInfos);
        Assert.NotNull(extension.SessionEndedInfos[0]);
    }

    [Fact]
    public async Task RemoveAsync_EndsTheSessionThenDeactivates()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-remove");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);

        var order = new List<string>();
        extension.Context!.SessionEnded += (_, _) => order.Add("ended");
        extension.OnDeactivate = _ =>
        {
            order.Add("deactivated");
            return ValueTask.CompletedTask;
        };

        Assert.True(await supervisor.Extensions.RemoveAsync("ext-remove", ct));

        Assert.Equal(["ended", "deactivated"], order);
        Assert.Equal(1, extension.DeactivateCount);
        Assert.Single(extension.SessionEndedInfos);
        Assert.False(supervisor.Extensions.Contains("ext-remove"));
        Assert.True(extension.Context.Deactivated.IsCancellationRequested);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotDisturbTheOtherExtensions()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var a = new RecordingExtension("ext-a");
        var b = new RecordingExtension("ext-b");
        await supervisor.Extensions.AddAsync(a);
        await supervisor.Extensions.AddAsync(b);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);

        Assert.True(await supervisor.Extensions.RemoveAsync("ext-a", ct));

        Assert.Equal(0, b.DeactivateCount);
        Assert.Empty(b.SessionEndedInfos);
        Assert.True(supervisor.Extensions.Contains("ext-b"));
        Assert.False(supervisor.Extensions.Contains("ext-a"));
        Assert.False(b.Context!.Deactivated.IsCancellationRequested);
        Assert.True(b.Context.InSession);

        await supervisor.StopAsync(ct);
    }

    [Fact]
    public async Task AddAsync_WithADuplicateId_Throws()
    {
        var factory = new SequencedSessionFactory().Then(NeverDial());
        await using var supervisor = new UmpkClientSupervisor(factory);
        await supervisor.Extensions.AddAsync(new RecordingExtension("dup"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => supervisor.Extensions.AddAsync(new RecordingExtension("dup")));

        Assert.True(supervisor.Extensions.Contains("dup"));
    }

    [Fact]
    public async Task Deactivate_ThatNeverReturns_IsAbandonedAfterTheTimeout()
    {
        var clock = new VirtualTimeProvider(DateTimeOffset.UtcNow);
        var options = new ClientSupervisorOptions
        {
            TimeProvider = clock,
            ExtensionTeardownTimeout = TimeSpan.FromSeconds(5),
        };
        var factory = new SequencedSessionFactory().Then(NeverDial());
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        var extension = new RecordingExtension("ext-stuck")
        {
            OnDeactivate = _ => new ValueTask(new TaskCompletionSource().Task),
        };
        await supervisor.Extensions.AddAsync(extension);

        Task<bool> removeTask = supervisor.Extensions.RemoveAsync("ext-stuck");

        // Give the wait a chance to actually register against the virtual clock before advancing it.
        await Task.Delay(50);
        Assert.False(removeTask.IsCompleted, "RemoveAsync completed before the teardown timeout elapsed.");

        clock.Advance(TimeSpan.FromSeconds(6));

        bool removed = await removeTask.WaitAsync(Budget);
        Assert.True(removed);
        Assert.False(supervisor.Extensions.Contains("ext-stuck"));
        Assert.True(extension.Context!.Deactivated.IsCancellationRequested);
    }

    [Fact]
    public async Task Deactivate_ThatNeverReturns_HasItsFaultObserved()
    {
        var clock = new VirtualTimeProvider(DateTimeOffset.UtcNow);
        var options = new ClientSupervisorOptions
        {
            TimeProvider = clock,
            ExtensionTeardownTimeout = TimeSpan.FromMilliseconds(50),
        };
        var factory = new SequencedSessionFactory().Then(NeverDial());
        await using var supervisor = new UmpkClientSupervisor(factory, options);

        // Faults once its Deactivated token (handed as the DeactivateAsync ct) is cancelled - exactly what happens to the abandoned task once the timeout gives up on it.
        var extension = new RecordingExtension("ext-fault")
        {
            OnDeactivate = ct => new ValueTask(Task.Delay(Timeout.Infinite, ct)),
        };
        await supervisor.Extensions.AddAsync(extension);

        bool unobserved = false;
        void OnUnobserved(object? _, UnobservedTaskExceptionEventArgs e)
        {
            unobserved = true;
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            Task<bool> removeTask = supervisor.Extensions.RemoveAsync("ext-fault");
            await Task.Delay(50);
            clock.Advance(TimeSpan.FromSeconds(1));

            Assert.True(await removeTask.WaitAsync(Budget));

            // Give the now-cancelled, abandoned task a moment to actually fault and be collected.
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(50);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        Assert.False(unobserved, "The abandoned extension teardown's fault was never observed.");
    }

    [Fact]
    public async Task StopAsync_DeactivatesInReverseOrder_BeforeCompletedCompletes()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);

        var order = new List<string>();
        var a = new RecordingExtension("ext-a") { OnDeactivate = _ => { order.Add("a"); return ValueTask.CompletedTask; } };
        var b = new RecordingExtension("ext-b") { OnDeactivate = _ => { order.Add("b"); return ValueTask.CompletedTask; } };
        var c = new RecordingExtension("ext-c") { OnDeactivate = _ => { order.Add("c"); return ValueTask.CompletedTask; } };
        await supervisor.Extensions.AddAsync(a);
        await supervisor.Extensions.AddAsync(b);
        await supervisor.Extensions.AddAsync(c);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);

        List<string>? orderAtTerminal = null;
        supervisor.StatusChanged += (_, e) =>
        {
            if (e.Current == ClientStatus.Disconnected)
                orderAtTerminal = [.. order];

        };

        await supervisor.StopAsync(ct);

        Assert.NotNull(orderAtTerminal);
        Assert.Equal(["c", "b", "a"], orderAtTerminal);
        Assert.Equal(["c", "b", "a"], order);
        Assert.Equal(1, a.DeactivateCount);
        Assert.Equal(1, b.DeactivateCount);
        Assert.Equal(1, c.DeactivateCount);
    }

    /// <summary>Removing a plugin after the client stops must not tear down an already-deactivated entry again. A second teardown would cancel a disposed token source and make RemoveAsync throw <see cref="ObjectDisposedException"/>.</summary>
    [Fact]
    public async Task RemoveAsync_AfterStop_ReportsNoMember_AndDoesNotDeactivateTwice()
    {
        FakeJavaServer server = FakeJavaServer.Create();
        var factory = new SequencedSessionFactory().Then(Succeed(server));
        await using var supervisor = new UmpkClientSupervisor(factory);
        var extension = new RecordingExtension("ext-stopped");
        await supervisor.Extensions.AddAsync(extension);

        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await supervisor.StartAsync(Endpoint, ct);
        await supervisor.StopAsync(ct);

        Assert.False(await supervisor.Extensions.RemoveAsync("ext-stopped", ct));
        Assert.Equal(1, extension.DeactivateCount);
        Assert.False(supervisor.Extensions.Contains("ext-stopped"));
        Assert.Empty(supervisor.Extensions.Ids);
    }

    // Scripted attempt steps

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> Succeed(FakeJavaServer server) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToPlayAsync(server, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> SucceedThenDrop(FakeJavaServer server) =>
        (attempt, ct) =>
        {
            UmpkClient client = ScriptedServer.BuildClient(server);
            _ = ScriptedServer.DriveToPlayThenDropAsync(server, ct);
            return ValueTask.FromResult(client);
        };

    private static Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> NeverDial() =>
        (_, _) => throw new InvalidOperationException("This attempt must never be dialed.");

    // Helpers

    /// <summary>Completes once <see cref="UmpkClientSupervisor.StatusChanged"/> has reported Playing at least <paramref name="atLeast"/> times.</summary>
    private static TaskCompletionSource PlayingCounter(UmpkClientSupervisor supervisor, int atLeast)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        supervisor.StatusChanged += (_, e) =>
        {
            if (e.Current != ClientStatus.Playing)
                return;

            if (Interlocked.Increment(ref count) >= atLeast)
                tcs.TrySetResult();

        };
        return tcs;
    }

    /// <summary>Records every extension-lifecycle event, in order, for assertions.</summary>
    private sealed class RecordingExtension(string id) : IClientExtension
    {
        public string Id { get; } = id;

        public int ActivateCount { get; private set; }

        public int DeactivateCount { get; private set; }

        public ClientExtensionContext? Context { get; private set; }

        public List<(UmpkClient Client, ClientStatus Status)> SessionCreatedEvents { get; } = [];

        public List<ClientPluginContext> SessionStartedContexts { get; } = [];

        public List<DisconnectInfo?> SessionEndedInfos { get; } = [];

        public Func<CancellationToken, ValueTask>? OnDeactivate { get; set; }

        public ValueTask ActivateAsync(ClientExtensionContext context, CancellationToken ct)
        {
            ActivateCount++;
            Context = context;
            context.SessionCreated += (_, e) => SessionCreatedEvents.Add((e.Client, e.Client.Status));
            context.SessionStarted += (_, e) => SessionStartedContexts.Add(e.Session);
            context.SessionEnded += (_, e) => SessionEndedInfos.Add(e.Disconnect);
            return ValueTask.CompletedTask;
        }

        public async ValueTask DeactivateAsync(CancellationToken ct)
        {
            DeactivateCount++;
            if (OnDeactivate is not null)
                await OnDeactivate(ct).ConfigureAwait(false);

        }
    }
}
