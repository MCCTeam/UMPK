using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockItemStackRequestIdAllocatorTests
{
    [Fact]
    public void DefaultSequenceStartsAtMinusThreeAndDescendsByTwo()
    {
        var allocator = new BedrockItemStackRequestIdAllocator();

        Assert.True(allocator.TryAllocate(out int first));
        Assert.True(allocator.TryAllocate(out int second));
        Assert.True(allocator.TryAllocate(out int third));

        Assert.Equal(-3, first);
        Assert.Equal(-5, second);
        Assert.Equal(-7, third);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InitialIdentityMustBelongToTheSequence(int firstRequestId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BedrockItemStackRequestIdAllocator(firstRequestId));
    }

    [Fact]
    public void LastTwoIdentitiesAreEmittedOnceAndExhaustionDoesNotWrap()
    {
        var allocator = new BedrockItemStackRequestIdAllocator(int.MinValue + 3);

        Assert.True(allocator.TryAllocate(out int penultimate));
        Assert.True(allocator.TryAllocate(out int last));
        Assert.False(allocator.TryAllocate(out int exhausted));
        Assert.False(allocator.TryAllocate(out int stillExhausted));

        Assert.Equal(int.MinValue + 3, penultimate);
        Assert.Equal(int.MinValue + 1, last);
        Assert.Equal(0, exhausted);
        Assert.Equal(0, stillExhausted);
    }

    [Fact]
    public void FinalOddNegativeIdentityIsAvailableBeforeExhaustion()
    {
        var allocator = new BedrockItemStackRequestIdAllocator(int.MinValue + 1);

        Assert.True(allocator.TryAllocate(out int last));
        Assert.Equal(int.MinValue + 1, last);
        Assert.True(last < 0);
        Assert.Equal(1, last & 1);
        Assert.False(allocator.TryAllocate(out int exhausted));
        Assert.Equal(0, exhausted);
    }

    [Fact]
    public void ConcurrentAllocationNeverReusesAnIdentity()
    {
        var allocator = new BedrockItemStackRequestIdAllocator();
        var requestIds = new int[256];

        Parallel.For(0, requestIds.Length, index => Assert.True(allocator.TryAllocate(out requestIds[index])));

        Assert.Equal(requestIds.Length, requestIds.Distinct().Count());
        Assert.All(requestIds, requestId =>
        {
            Assert.True(requestId < -1);
            Assert.Equal(1, requestId & 1);
        });
        Assert.Equal(Enumerable.Range(0, requestIds.Length).Select(index => -3 - 2 * index).Order(), requestIds.Order());
    }
}
