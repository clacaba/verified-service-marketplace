using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Xunit;
namespace Marketplace.Api.Tests;

public sealed class FoundationTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Marketplace"] = "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1", ["AllowedHosts"] = "*" }));
    }
    [Fact]
    public async Task LivenessDoesNotDependOnDatabase()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
    [Fact]
    public async Task ReadinessFailsWhenDatabaseCannotConnect()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
}
