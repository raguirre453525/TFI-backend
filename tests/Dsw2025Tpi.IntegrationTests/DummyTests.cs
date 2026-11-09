namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class DummyTests
{
    private readonly HttpClient _client;

    public DummyTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Api_Levanta_Y_Healthcheck_Responde()
    {
        var response = await _client.GetAsync("/healthcheck");

        response.EnsureSuccessStatusCode();
    }
}
