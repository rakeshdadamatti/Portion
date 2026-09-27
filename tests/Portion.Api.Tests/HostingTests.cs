using System.Net;
using System.Text.Json;

namespace Portion.Api.Tests;

/// <summary>Covers the cross-cutting pipeline: probes, OpenAPI, CORS and transport behaviour.</summary>
public class HostingTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Readiness_ServesATerseJsonReport(string path)
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(path);

        // Ollama is unreachable in tests, which is reported as Degraded. Readiness must stay 200:
        // an absent local model degrades retrieval but the API still serves traffic.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("Degraded", root.GetProperty("status").GetString());

        var checks = root.GetProperty("checks").EnumerateArray().ToList();

        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "database");
        Assert.Contains(checks, check => check.GetProperty("name").GetString() == "ollama");

        // The report must not leak connection strings or exception detail.
        Assert.DoesNotContain("Data Source", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Liveness_ReportsHealthy_WithoutProbingDependencies()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
        Assert.Single(document.RootElement.GetProperty("checks").EnumerateArray());
    }

    [Fact]
    public async Task OpenApi_DocumentsEveryRoute_AndKeepsProbesAnonymous()
    {
        using var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        Assert.Contains("/api/v1/recruitment/resumes", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/api/v1/recruitment/resumes/{id}", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/api/v1/recruitment/sync", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/api/v1/recruitment/sync/{jobId}", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/api/v1/screening/stream", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/health", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/health/live", paths.EnumerateObject().Select(p => p.Name));
        Assert.Contains("/health/ready", paths.EnumerateObject().Select(p => p.Name));

        // A load balancer cannot present a bearer token, so the probes must not require one.
        var live = paths.GetProperty("/health/live").GetProperty("get");

        Assert.False(live.TryGetProperty("security", out _), "The liveness probe must stay anonymous.");

        // Everything else is documented as requiring the gateway-issued token.
        var list = paths.GetProperty("/api/v1/recruitment/resumes").GetProperty("get");

        Assert.True(list.TryGetProperty("security", out _), "Business endpoints must declare their security requirement.");
    }

    [Fact]
    public async Task Preflight_FromAnAllowedOrigin_IsApproved_WithoutCredentials()
    {
        await factory.ResetDatabaseAsync();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/recruitment/resumes");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

        // Credentials are refused: the API is bearer-token authenticated, and a credentialed policy
        // would let any allowed origin read authenticated responses.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Preflight_FromAnUnknownOrigin_IsNotApproved()
    {
        await factory.ResetDatabaseAsync();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/recruitment/resumes");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Responses_DoNotAdvertiseTheServerHeader()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.False(response.Headers.Contains("Server"), "Kestrel must not identify itself.");
    }

    [Fact]
    public async Task ScreeningStream_IsNotCompressed()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/screening/stream?prompt=engineers",
            HttpCompletionOption.ResponseHeadersRead);

        // A compressing proxy would withhold the first token until the whole answer existed.
        Assert.True(response.Content.Headers.ContentEncoding.Count == 0);
    }

    [Fact]
    public async Task JsonResponses_AreCompressed_WhenTheClientAsks()
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedResumeAsync("Ada Lovelace");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/recruitment/resumes");
        request.Headers.Add("Accept-Encoding", "gzip, br");

        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Contains(response.Content.Headers.ContentEncoding, encoding => encoding == "gzip" || encoding == "br");
    }
}
