using System.Buffers;
using Umpk.Transport;
using Xunit;

namespace Umpk.Core.Tests.Transport;

public sealed class OwnedMessageTests
{
    [Fact]
    public void ConstructorRejectsLengthOutsideOwnerMemory()
    {
        using var owner = new TrackingMemoryOwner(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnedMessage(owner, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnedMessage(owner, 5));
    }

    [Fact]
    public void MemoryExposesOnlyTheLogicalMessage()
    {
        using var owner = new TrackingMemoryOwner(8);
        owner.Memory.Span.Fill(0x7f);
        using var message = new OwnedMessage(owner, 3);

        Assert.Equal(3, message.Memory.Length);
        Assert.Equal(new byte[] { 0x7f, 0x7f, 0x7f }, message.Memory.ToArray());
    }

    [Fact]
    public void DisposeReleasesTheOwnerExactlyOnce()
    {
        var owner = new TrackingMemoryOwner(4);
        var message = new OwnedMessage(owner, 4);

        message.Dispose();
        message.Dispose();

        Assert.Equal(1, owner.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => _ = message.Memory);
    }

    [Fact]
    public void AdmissionResultStatesWhetherOwnershipTransferred()
    {
        Assert.False(default(MessageAdmissionResult).OwnershipTransferred);
        Assert.True(MessageAdmissionResult.Accepted.OwnershipTransferred);
        Assert.False(MessageAdmissionResult.Closed.OwnershipTransferred);
        Assert.False(MessageAdmissionResult.CapacityExceeded.OwnershipTransferred);
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private readonly byte[] _buffer = new byte[length];

        public int DisposeCount { get; private set; }

        public Memory<byte> Memory => _buffer;

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
