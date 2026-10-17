using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class CollisionGridReaderTests
{
    [Theory]
    [InlineData(new byte[] { 1, 0, 0 })]
    [InlineData(new byte[] { 0, 1 })]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 1, 1 })]
    public void MalformedGeneratedGridsAreRefused(byte[] bytes)
    {
        Assert.Throws<ProtocolViolationException>(() => new JavaBlockCollisionGrids(bytes));
    }
}
