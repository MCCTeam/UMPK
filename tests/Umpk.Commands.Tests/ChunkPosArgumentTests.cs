using Umpk.Geometry;
using Xunit;

namespace Umpk.Commands.Tests;

public sealed class ChunkPosArgumentTests
{
    private static async Task<(bool Success, ChunkPos Value)> RunAsync(string input)
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        ChunkPos captured = default;
        scope.Register(b => b.Literal("cmd", n =>
            n.ThenArgument("arg", Arguments.ChunkPos(), a =>
                a.Executes(ctx =>
                {
                    captured = ctx.GetArgument<ChunkPos>("arg");
                    return new ValueTask<int>(1);
                }))));

        var result = await service.ExecuteAsync("cmd " + input, new TestSource());
        return (result.Success, captured);
    }

    [Fact]
    public async Task A_chunk_coordinate_pair_parses_into_a_ChunkPos()
    {
        var (ok, value) = await RunAsync("3 7");

        Assert.True(ok);
        Assert.Equal(new ChunkPos(3, 7), value);
    }

    [Fact]
    public async Task A_negative_chunk_coordinate_pair_parses()
    {
        var (ok, value) = await RunAsync("-3 -7");

        Assert.True(ok);
        Assert.Equal(new ChunkPos(-3, -7), value);
    }

    [Fact]
    public async Task A_non_numeric_chunk_coordinate_is_rejected()
    {
        var (ok, _) = await RunAsync("abc 7");

        Assert.False(ok);
    }
}
