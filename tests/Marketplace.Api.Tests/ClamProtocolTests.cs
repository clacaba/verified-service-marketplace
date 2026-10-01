using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Marketplace.Api.Verification;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed class ClamProtocolTests
{
    [Theory]
    [InlineData("stream: OK\0", true)]
    [InlineData("stream: Demo FOUND\0", false)]
    public async Task ScanStreamsAllChunksAndRequiresFramedReply(string reply, bool clean)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var bytes = new byte[131073]; Random.Shared.NextBytes(bytes);
            var daemon = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = socket.GetStream();
                var command = new byte[10]; await stream.ReadExactlyAsync(command, timeout.Token);
                Assert.Equal(Encoding.ASCII.GetBytes("zINSTREAM\0"), command);
                using var received = new MemoryStream(); var header = new byte[4];
                for (; ; )
                {
                    await stream.ReadExactlyAsync(header, timeout.Token); var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (length == 0) break;
                    Assert.InRange(length, 1u, 65536u);
                    var chunk = new byte[length]; await stream.ReadExactlyAsync(chunk, timeout.Token); await received.WriteAsync(chunk, timeout.Token);
                }
                Assert.Equal(bytes, received.ToArray()); await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), timeout.Token);
            }, timeout.Token);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Scanner:Host"] = "127.0.0.1", ["Scanner:Port"] = port.ToString() }).Build();
            Assert.Equal(clean, await new ClamEvidenceScanner(config).IsClean(bytes, timeout.Token));
            await daemon;
        }
        finally { listener.Stop(); }
    }
    [Fact]
    public async Task AmbiguousOrUnterminatedCleanReplyFailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var daemon = Task.Run(async () =>
            {
                using var socket = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = socket.GetStream();
                var command = new byte[10]; await stream.ReadExactlyAsync(command, timeout.Token);
                var header = new byte[4]; await stream.ReadExactlyAsync(header, timeout.Token);
                var chunk = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)]; await stream.ReadExactlyAsync(chunk, timeout.Token);
                await stream.ReadExactlyAsync(header, timeout.Token);
                await stream.WriteAsync("stream: OK"u8.ToArray(), timeout.Token); // Missing NUL terminator.
            }, timeout.Token);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Scanner:Host"] = "127.0.0.1", ["Scanner:Port"] = port.ToString() }).Build();
            await Assert.ThrowsAsync<EvidenceUnavailableException>(() => new ClamEvidenceScanner(config).IsClean([1, 2, 3], timeout.Token));
            await daemon;
        }
        finally { listener.Stop(); }
    }
}
