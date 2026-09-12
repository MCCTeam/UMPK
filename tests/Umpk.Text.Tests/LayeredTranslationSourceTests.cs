using System.Diagnostics.CodeAnalysis;
using Umpk.Text;
using Xunit;

namespace Umpk.Text.Tests;

public sealed class LayeredTranslationSourceTests
{
    private sealed class StubSource(Dictionary<string, string> map) : ITranslationSource
    {
        public bool TryResolve(string key, [NotNullWhen(true)] out string? template) =>
            map.TryGetValue(key, out template);
    }

    [Fact]
    public void Layer0_WinsOverLayer1()
    {
        var layer0 = new StubSource(new Dictionary<string, string> { ["k"] = "first" });
        var layer1 = new StubSource(new Dictionary<string, string> { ["k"] = "second" });
        var layered = new LayeredTranslationSource(layer0, layer1);

        Assert.True(layered.TryResolve("k", out string? template));
        Assert.Equal("first", template);
    }

    [Fact]
    public void FallsThroughWhenALayerMisses()
    {
        var layer0 = new StubSource([]);
        var layer1 = new StubSource(new Dictionary<string, string> { ["k"] = "fallback" });
        var layered = new LayeredTranslationSource(layer0, layer1);

        Assert.True(layered.TryResolve("k", out string? template));
        Assert.Equal("fallback", template);
    }

    [Fact]
    public void SetLayer_IsVisibleToTheNextResolve()
    {
        var before = new StubSource(new Dictionary<string, string> { ["k"] = "before" });
        var after = new StubSource(new Dictionary<string, string> { ["k"] = "after" });
        var layered = new LayeredTranslationSource(before);

        Assert.True(layered.TryResolve("k", out string? initial));
        Assert.Equal("before", initial);

        layered.SetLayer(0, after);

        Assert.True(layered.TryResolve("k", out string? updated));
        Assert.Equal("after", updated);
        Assert.Same(after, layered[0]);
    }

    [Fact]
    public void SetLayer_OutOfRange_Throws()
    {
        var layered = new LayeredTranslationSource(new StubSource([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => layered.SetLayer(-1, new StubSource([])));
        Assert.Throws<ArgumentOutOfRangeException>(() => layered.SetLayer(1, new StubSource([])));
    }

    /// <summary>A reader racing a writer that keeps swapping layer 0 between two sources must always resolve to ONE of the two sources' complete values, never a mix of the two and never a crash. Reference assignment is atomic in .NET, so this mainly pins the Volatile discipline down as a documented, tested guarantee rather than an unverified claim in a comment.</summary>
    [Fact]
    public async Task ConcurrentResolveDuringSetLayer_AlwaysSeesOneCompleteLayer()
    {
        const string valueA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string valueB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var sourceA = new StubSource(new Dictionary<string, string> { ["k"] = valueA });
        var sourceB = new StubSource(new Dictionary<string, string> { ["k"] = valueB });
        var layered = new LayeredTranslationSource(sourceA);

        const int iterations = 20_000;
        Exception? observed = null;

        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
                layered.SetLayer(0, (i % 2 == 0) ? sourceA : sourceB);

        });

        Task reader = Task.Run(() =>
        {
            for (int i = 0; i < iterations; i++)
                if (layered.TryResolve("k", out string? template) && template != valueA && template != valueB)
                    observed = new InvalidOperationException($"torn read: '{template}'");

        });

        await Task.WhenAll(writer, reader);
        Assert.Null(observed);
    }

    [Fact]
    public void NoLayers_ResolvesNothing()
    {
        var layered = new LayeredTranslationSource();

        Assert.Equal(0, layered.LayerCount);
        Assert.False(layered.TryResolve("k", out string? template));
        Assert.Null(template);
    }
}
