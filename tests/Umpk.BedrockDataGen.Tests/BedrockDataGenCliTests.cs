using Umpk.BedrockDataGen;
using Xunit;

namespace Umpk.BedrockDataGen.Tests;

public sealed class BedrockDataGenCliTests
{
    [Fact]
    public void HelpListsTheDeterministicPipelineCommands()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = BedrockDataGenCli.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("import|normalize|verify|diff|generate|verify-generated", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void UnknownCommandFailsWithoutGuessingACommand()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = BedrockDataGenCli.Run(["latest"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("unknown command: latest", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownCommandWithoutRequiredOptionsFailsClearly()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = BedrockDataGenCli.Run(["import"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--source-root", error.ToString(), StringComparison.Ordinal);
    }
}
