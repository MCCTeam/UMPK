using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Hosting;
using Umpk.TestKit.Time;

namespace Umpk.TestKit.Hosting;

/// <summary>Groups a deterministic scheduler, tick source, and logger for session-runtime tests.</summary>
public sealed class FakeClientHost : IAsyncDisposable
{
    private readonly List<Exception> _schedulerErrors = [];

    /// <summary>Creates a host with a fresh scheduler and tick source.</summary>
    public FakeClientHost(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
        Scheduler = new TestScheduler(_schedulerErrors.Add);
        Ticks = new TestTickSource();
    }

    /// <summary>The manually pumped session scheduler.</summary>
    public TestScheduler Scheduler { get; }

    /// <summary>The virtual-time tick source.</summary>
    public TestTickSource Ticks { get; }

    /// <summary>The host logger.</summary>
    public ILogger Logger { get; }

    /// <summary>Exceptions captured from fire-and-forget scheduler work (for assertions).</summary>
    public IReadOnlyList<Exception> SchedulerErrors => _schedulerErrors;

    /// <summary>Advances virtual time by <paramref name="ticks"/> and drains the scheduler to idle.</summary>
    public async Task AdvanceAsync(int ticks = 1)
    {
        Ticks.Advance(ticks);
        await Scheduler.RunUntilIdleAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Ticks.Complete();
        await Scheduler.DisposeAsync().ConfigureAwait(false);
    }
}
