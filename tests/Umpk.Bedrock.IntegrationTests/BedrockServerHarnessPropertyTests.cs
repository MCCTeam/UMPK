using Xunit;

namespace Umpk.Bedrock.IntegrationTests;

public sealed class BedrockServerHarnessPropertyTests
{
    [Fact]
    public void MissingPathOverridesKeepExistingDefaults()
    {
        var harness = new BedrockServerHarness(environmentVariableReader: static _ => null);

        Assert.Equal(BedrockServerHarness.SourceDistribution, harness.SelectedSourceDistribution);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "umpk-bedrock-live-join"), harness.EvidenceRootDirectory);
        Assert.Empty(harness.RunDirectory);
        Assert.Empty(harness.EvidenceDirectory);
        Assert.Equal(-1, harness.ProcessId);
    }

    [Fact]
    public void PathOverridesAreCapturedOnceForEachHarness()
    {
        string source = Path.Combine(Path.GetTempPath(), "umpk-selected-bds");
        string evidenceRoot = Path.Combine(Path.GetTempPath(), "umpk-selected-evidence");
        var environment = new Dictionary<string, string?>
        {
            [BedrockServerHarness.SourceDistributionEnvironmentVariable] = source,
            [BedrockServerHarness.EvidenceRootEnvironmentVariable] = evidenceRoot,
        };
        var reads = new List<string>();
        string? ReadEnvironment(string name)
        {
            reads.Add(name);
            return environment.GetValueOrDefault(name);
        }

        var first = new BedrockServerHarness(environmentVariableReader: ReadEnvironment);
        environment[BedrockServerHarness.SourceDistributionEnvironmentVariable] = source + "-second";
        environment[BedrockServerHarness.EvidenceRootEnvironmentVariable] = evidenceRoot + "-second";
        var second = new BedrockServerHarness(environmentVariableReader: ReadEnvironment);

        Assert.Equal(source, first.SelectedSourceDistribution);
        Assert.Equal(evidenceRoot, first.EvidenceRootDirectory);
        Assert.Equal(source + "-second", second.SelectedSourceDistribution);
        Assert.Equal(evidenceRoot + "-second", second.EvidenceRootDirectory);
        Assert.Equal(2, reads.Count(name => name == BedrockServerHarness.SourceDistributionEnvironmentVariable));
        Assert.Equal(2, reads.Count(name => name == BedrockServerHarness.EvidenceRootEnvironmentVariable));
    }

    [Theory]
    [InlineData("relative-path")]
    [InlineData("")]
    [InlineData(" ")]
    public void SourceOverrideRejectsNonAbsolutePaths(string value)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new BedrockServerHarness(environmentVariableReader: name => name == BedrockServerHarness.SourceDistributionEnvironmentVariable ? value : null));

        Assert.Contains(BedrockServerHarness.SourceDistributionEnvironmentVariable, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("relative-path")]
    [InlineData("")]
    [InlineData(" ")]
    public void EvidenceOverrideRejectsNonAbsolutePaths(string value)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new BedrockServerHarness(environmentVariableReader: name => name == BedrockServerHarness.EvidenceRootEnvironmentVariable ? value : null));

        Assert.Contains(BedrockServerHarness.EvidenceRootEnvironmentVariable, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PathOverridesNormalizeParentSegments()
    {
        string source = Path.Combine(Path.GetTempPath(), "umpk-selected-bds");
        string evidenceRoot = Path.Combine(Path.GetTempPath(), "umpk-selected-evidence");
        var harness = new BedrockServerHarness(environmentVariableReader: name => name switch
        {
            BedrockServerHarness.SourceDistributionEnvironmentVariable => Path.Combine(source, "nested", ".."),
            BedrockServerHarness.EvidenceRootEnvironmentVariable => Path.Combine(evidenceRoot, "nested", ".."),
            _ => null,
        });

        Assert.Equal(source, harness.SelectedSourceDistribution);
        Assert.Equal(evidenceRoot, harness.EvidenceRootDirectory);
    }

    [Fact]
    public void EvidenceOverrideRejectsDirectoriesInsideTheHarnessCheckout()
    {
        string evidenceRoot = Path.Combine(AppContext.BaseDirectory, "harness-evidence");

        ArgumentException error = Assert.Throws<ArgumentException>(() => new BedrockServerHarness(environmentVariableReader: name => name == BedrockServerHarness.EvidenceRootEnvironmentVariable ? evidenceRoot : null));

        Assert.Contains("outside the harness Git checkout", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(evidenceRoot));
    }

    [Fact]
    public void CheckoutMarkerRequiresAValidHeadAndRepositoryDirectories()
    {
        string checkout = Path.Combine(Path.GetTempPath(), $"umpk-harness-marker-test-{Guid.NewGuid():N}");
        string gitDirectory = Path.Combine(checkout, ".git");
        Directory.CreateDirectory(gitDirectory);
        try
        {
            Assert.False(BedrockServerHarness.HasGitCheckoutMarker(checkout));
            File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/test\n");
            Assert.False(BedrockServerHarness.HasGitCheckoutMarker(checkout));
            Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
            Assert.False(BedrockServerHarness.HasGitCheckoutMarker(checkout));
            Directory.CreateDirectory(Path.Combine(gitDirectory, "refs"));
            Assert.True(BedrockServerHarness.HasGitCheckoutMarker(checkout));
            File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "invalid head\n");
            Assert.False(BedrockServerHarness.HasGitCheckoutMarker(checkout));
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckoutGitFileResolvesWorktreeMetadata(bool absoluteTarget)
    {
        string root = Path.Combine(Path.GetTempPath(), $"umpk-harness-worktree-test-{Guid.NewGuid():N}");
        string checkout = Path.Combine(root, "checkout");
        string gitDirectory = Path.Combine(root, "metadata", "worktree");
        Directory.CreateDirectory(checkout);
        Directory.CreateDirectory(gitDirectory);
        Directory.CreateDirectory(Path.Combine(root, "metadata", "common", "objects"));
        Directory.CreateDirectory(Path.Combine(root, "metadata", "common", "refs"));
        try
        {
            File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/test\n");
            File.WriteAllText(Path.Combine(gitDirectory, "commondir"), "../common\n");
            string target = absoluteTarget ? gitDirectory : Path.GetRelativePath(checkout, gitDirectory);
            File.WriteAllText(Path.Combine(checkout, ".git"), $"gitdir: {target}\n");

            Assert.True(BedrockServerHarness.HasGitCheckoutMarker(checkout));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CheckoutGitFileRejectsMissingMetadata()
    {
        string checkout = Path.Combine(Path.GetTempPath(), $"umpk-harness-gitfile-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(checkout);
        try
        {
            File.WriteAllText(Path.Combine(checkout, ".git"), "gitdir: missing-target\n");
            Assert.False(BedrockServerHarness.HasGitCheckoutMarker(checkout));
        }
        finally
        {
            Directory.Delete(checkout, recursive: true);
        }
    }

    [Fact]
    public void EvidenceOverrideCannotWriteIntoTheSourceDistribution()
    {
        string source = Path.Combine(Path.GetTempPath(), "umpk-selected-bds");

        ArgumentException error = Assert.Throws<ArgumentException>(() => new BedrockServerHarness(environmentVariableReader: name => name switch
        {
            BedrockServerHarness.SourceDistributionEnvironmentVariable => source,
            BedrockServerHarness.EvidenceRootEnvironmentVariable => Path.Combine(source, "evidence"),
            _ => null,
        }));

        Assert.Contains("outside the source distribution", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongExecutableIdentityFailsBeforeCreatingAnOwnedRun()
    {
        string source = Path.Combine(Path.GetTempPath(), $"umpk-harness-source-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, "bedrock_server"), "invalid executable identity");
            string selectedSource = source;
            await using var harness = new BedrockServerHarness(environmentVariableReader: name => name == BedrockServerHarness.SourceDistributionEnvironmentVariable ? selectedSource : null);
            selectedSource = Path.Combine(source, "later-selection");

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.StartAsync());

            Assert.Equal(source, harness.SelectedSourceDistribution);
            Assert.Contains("pinned 1.26.45.1 identity", error.Message, StringComparison.Ordinal);
            Assert.NotEqual(BedrockServerHarness.ExpectedExecutableSha256, harness.ExecutableSha256);
            Assert.Empty(harness.RunDirectory);
            Assert.Empty(harness.EvidenceDirectory);
            Assert.Equal(0, harness.Port);
            Assert.Equal(-1, harness.ProcessId);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void DistributionCopyKeepsQuotedPathsAsSeparateArguments()
    {
        string source = Path.Combine(Path.GetTempPath(), "umpk-source with \"quotes\"");
        string destination = Path.Combine(Path.GetTempPath(), "umpk-owned run");

        System.Diagnostics.ProcessStartInfo startInfo = BedrockServerHarness.BuildDistributionCopyStartInfo(source, destination);

        Assert.Equal("cp", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["-a", "--", Path.Combine(source, "."), destination], startInfo.ArgumentList);
        Assert.Empty(startInfo.Arguments);
    }

    [Fact]
    public async Task CancelledDistributionCopyCreatesNoOwnedDirectory()
    {
        string source = Path.Combine(Path.GetTempPath(), "umpk-selected-bds");
        string destination = Path.Combine(Path.GetTempPath(), $"umpk-harness-copy-test-{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BedrockServerHarness.CopyDistributionAsync(source, destination, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void DefaultHarnessLeavesStrictMovementUnset()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.StrictMovementRequested);
        Assert.Equal("false", harness.EffectiveMovementStrict);
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-authoritative-movement-strict=false"], 19132, 19133, "run1", harness.StrictMovementRequested);
        Assert.Contains("server-authoritative-movement-strict=false", lines);
        Assert.DoesNotContain("server-authoritative-movement-strict=true", lines);
    }

    [Fact]
    public void StrictHarnessWritesStrictMovementTrue()
    {
        var harness = new BedrockServerHarness(strictMovement: true);
        Assert.True(harness.StrictMovementRequested);
        Assert.Equal("true", harness.EffectiveMovementStrict);
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-authoritative-movement-strict=false"], 19132, 19133, "run1", harness.StrictMovementRequested);
        Assert.Contains("server-authoritative-movement-strict=true", lines);
        Assert.DoesNotContain("server-authoritative-movement-strict=false", lines);
    }

    [Fact]
    public void StrictHarnessAppendsKeyWhenAbsent()
    {
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-port=19132"], 19132, 19133, "run1", true);
        Assert.Contains("server-authoritative-movement-strict=true", lines);
        Assert.Contains("server-port=19132", lines);
    }

    [Fact]
    public void CreativeHarnessRequestsCreativeGamemode()
    {
        var harness = new BedrockServerHarness(creativeMode: true);
        Assert.True(harness.CreativeModeRequested);
        List<string> lines = BedrockServerHarness.ApplyProperties(["gamemode=survival"], 19132, 19133, "run1", false, harness.CreativeModeRequested);
        Assert.Contains("gamemode=creative", lines);
        Assert.DoesNotContain("gamemode=survival", lines);
    }

    [Fact]
    public void DefaultHarnessLeavesGamemodeUnset()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.CreativeModeRequested);
        List<string> lines = BedrockServerHarness.ApplyProperties(["gamemode=survival"], 19132, 19133, "run1", false, harness.CreativeModeRequested);
        Assert.Contains("gamemode=survival", lines);
        Assert.DoesNotContain("gamemode=creative", lines);
    }

    [Fact]
    public void DefaultHarnessLeavesLanVisibilityFalse()
    {
        var harness = new BedrockServerHarness();
        Assert.False(harness.LanVisibilityRequested);
        Assert.Equal("false", harness.EffectiveLanVisibility);
        List<string> lines = BedrockServerHarness.ApplyProperties(["enable-lan-visibility=false"], 19132, 19133, "run1", false, false, harness.LanVisibilityRequested);
        Assert.Contains("enable-lan-visibility=false", lines);
        Assert.DoesNotContain("enable-lan-visibility=true", lines);
    }

    [Fact]
    public void LanVisibleHarnessWritesLanVisibilityTrue()
    {
        var harness = new BedrockServerHarness(lanVisibility: true);
        Assert.True(harness.LanVisibilityRequested);
        Assert.Equal("true", harness.EffectiveLanVisibility);
        List<string> lines = BedrockServerHarness.ApplyProperties(["enable-lan-visibility=false"], 19132, 19133, "run1", false, false, harness.LanVisibilityRequested);
        Assert.Contains("enable-lan-visibility=true", lines);
        Assert.DoesNotContain("enable-lan-visibility=false", lines);
    }

    [Fact]
    public void LanVisibilityAppendsKeyWhenAbsent()
    {
        List<string> lines = BedrockServerHarness.ApplyProperties(["server-port=19132"], 19132, 19133, "run1", false, false, true);
        Assert.Contains("enable-lan-visibility=true", lines);
        Assert.Contains("server-port=19132", lines);
    }
}
