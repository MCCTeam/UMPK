using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Unit tests for the role-neutral session components: keep-alive, cookie store, registry tracker.</summary>
public class SessionComponentsTests
{
    [Fact]
    public void KeepAlive_Initiator_IssuesOnCadence_AndAcceptsEcho()
    {
        var svc = new KeepAliveService(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        DateTimeOffset t0 = DateTimeOffset.UnixEpoch;

        Assert.True(svc.TryIssue(t0, out long id0), "first issue fires immediately (last-sent is min)");
        Assert.True(svc.HasPending);

        // No second issue while one is pending or before the cadence.
        Assert.False(svc.TryIssue(t0 + TimeSpan.FromSeconds(5), out _));

        // Correct echo clears pending.
        Assert.True(svc.AcceptResponse(id0, t0 + TimeSpan.FromSeconds(1)));
        Assert.False(svc.HasPending);

        // Wrong echo is rejected.
        Assert.True(svc.TryIssue(t0 + TimeSpan.FromSeconds(20), out long id1));
        Assert.False(svc.AcceptResponse(id1 ^ 1, t0 + TimeSpan.FromSeconds(21)));
        Assert.True(svc.HasPending);
    }

    [Fact]
    public void KeepAlive_TimesOut_WhenUnanswered()
    {
        var svc = new KeepAliveService(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        DateTimeOffset t0 = DateTimeOffset.UnixEpoch;
        Assert.True(svc.TryIssue(t0, out _));
        Assert.False(svc.IsTimedOut(t0 + TimeSpan.FromSeconds(29)));
        Assert.True(svc.IsTimedOut(t0 + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void KeepAlive_Responder_EchoesRequestId()
    {
        Assert.Equal(0xDEADBEEFL, KeepAliveService.BuildResponse(0xDEADBEEFL));
    }

    [Fact]
    public void CookieStore_SetGetRemove()
    {
        var store = new CookieStore();
        Identifier key = Identifier.Minecraft("token");
        Assert.Null(store.Get(key));

        store.Set(key, [1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Get(key));
        Assert.True(store.Contains(key));
        Assert.Equal(1, store.Count);

        store.Remove(key);
        Assert.Null(store.Get(key));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void RegistryTracker_CollectsRegistryData()
    {
        var tracker = new RegistryTracker();
        Identifier biome = Identifier.Minecraft("worldgen/biome");
        tracker.Accept(new ClientboundConfigRegistryDataPacket(biome,
            [new PackedRegistryEntry(Identifier.Minecraft("plains"), null)]));

        Assert.Equal(1, tracker.Count);
        Assert.NotNull(tracker.EntriesFor(biome));
        Assert.Single(tracker.EntriesFor(biome)!);
        Assert.NotNull(tracker.BuildRegistryAccess());
    }
}
