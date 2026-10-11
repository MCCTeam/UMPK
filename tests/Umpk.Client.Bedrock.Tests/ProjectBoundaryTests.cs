using System.Xml.Linq;
using Xunit;

namespace Umpk.Client.Bedrock.Tests;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void BedrockProjectsDoNotReferenceJavaProjects()
    {
        string root = FindRepositoryRoot();
        string[] projects =
        [
            "src/Umpk.Transport.RakNet/Umpk.Transport.RakNet.csproj",
            "src/Umpk.Protocol.Bedrock/Umpk.Protocol.Bedrock.csproj",
            "src/Umpk.Data.Bedrock/Umpk.Data.Bedrock.csproj",
            "src/Umpk.Client.Bedrock/Umpk.Client.Bedrock.csproj"
        ];

        foreach (string project in projects)
        {
            Assert.DoesNotContain(ReadProjectReferences(Path.Combine(root, project)), reference => reference.Contains(".Java", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ProtocolProjectDoesNotDependOnDataOrClient()
    {
        string root = FindRepositoryRoot();
        IReadOnlyList<string> references = ReadProjectReferences(Path.Combine(root, "src/Umpk.Protocol.Bedrock/Umpk.Protocol.Bedrock.csproj"));

        Assert.DoesNotContain(references, reference => reference.Contains("Umpk.Data.Bedrock", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.Contains("Umpk.Client.Bedrock", StringComparison.Ordinal));
    }

    [Fact]
    public void ClientComposesTheBedrockProtocolDataAndTransportProjects()
    {
        string root = FindRepositoryRoot();
        IReadOnlyList<string> references = ReadProjectReferences(Path.Combine(root, "src/Umpk.Client.Bedrock/Umpk.Client.Bedrock.csproj"));

        Assert.Contains(references, reference => reference.EndsWith("Umpk.Protocol.Bedrock.csproj", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.EndsWith("Umpk.Data.Bedrock.csproj", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.EndsWith("Umpk.Transport.RakNet.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void BedrockPublicApisDoNotExposeRakNetTypes()
    {
        string publicApi = ReadBedrockPublicApis();

        Assert.DoesNotContain("RakNet", publicApi, StringComparison.Ordinal);
    }

    [Fact]
    public void BedrockPublicApisDoNotExposeGeneratedPacketRecords()
    {
        string publicApi = ReadBedrockPublicApis();

        Assert.DoesNotContain("Umpk.Protocol.Bedrock.Packets.", publicApi, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ReadProjectReferences(string path)
    {
        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(reference => reference is not null)
            .Cast<string>()
            .ToArray();
    }

    private static string ReadBedrockPublicApis()
    {
        string root = FindRepositoryRoot();
        string[] projects = ["Umpk.Protocol.Bedrock", "Umpk.Data.Bedrock", "Umpk.Client.Bedrock"];
        return string.Concat(projects.SelectMany(project => new[]
        {
            File.ReadAllText(Path.Combine(root, "src", project, "PublicAPI.Shipped.txt")),
            File.ReadAllText(Path.Combine(root, "src", project, "PublicAPI.Unshipped.txt"))
        }));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UMPK.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the UMPK repository root.");
    }
}
