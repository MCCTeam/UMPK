using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.Session;

public sealed class ResourcePackDownloadPolicyTests
{
    [Fact]
    public async Task OptInDownload_VerifiesAndAtomicallyCaches_ThenReportsDownloaded()
    {
        byte[] body = "controlled resource pack"u8.ToArray();
        string hash = Convert.ToHexString(SHA1.HashData(body)).ToLowerInvariant();
        string cache = Path.Combine(Path.GetTempPath(), $"umpk-pack-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cache);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = ServeOnceAsync(listener, body);

        try
        {
            var policies = new ClientPolicies
            {
                ResourcePack = ResourcePackPolicy.Configure(new ResourcePackDownloadOptions
                {
                    Accept = true,
                    Download = true,
                    Cache = true,
                    CacheDirectory = cache,
                    MaxDownloadBytes = 1024,
                }),
            };
            var harness = new ApplierHarness(
                BoundPacketApplierHarness.Version(774), policies: policies);
            var id = Guid.NewGuid();

            await harness.ApplyAsync(new ClientboundResourcePackPushPacket(
                id, $"http://127.0.0.1:{port}/pack.zip", hash, Required: false, Prompt: null));
            await server;

            Assert.Collection(
                harness.Recorder.Packets.OfType<ServerboundResourcePackPacket>(),
                response => Assert.Equal(ResourcePackAction.Accepted, response.Action),
                response => Assert.Equal(ResourcePackAction.Downloaded, response.Action));

            string cached = Assert.Single(Directory.GetFiles(cache, "*.zip"));
            Assert.Equal(body, await File.ReadAllBytesAsync(cached));
            Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
        }
        finally
        {
            listener.Stop();
            Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task ServeOnceAsync(TcpListener listener, byte[] body)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = client.GetStream();
        var request = new byte[4096];
        _ = await stream.ReadAsync(request);
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(body);
    }
}
