using System.Net;

namespace Passless.IntegrationTests;

[Collection(PasslessCollection.Name)]
public sealed class HealthEndpointTests(PasslessFixture fixture)
{
    [Fact]
    public async Task Health_endpoint_reports_the_host_is_up()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_is_not_found()
    {
        using var client = fixture.Api.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
