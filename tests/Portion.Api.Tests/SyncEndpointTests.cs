using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Portion.Api.Tests;

/// <summary>Covers the repository reconciliation endpoints.</summary>
public class SyncEndpointTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task Schedule_AcceptsAJob_AndExposesTheResourceLocation()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/recruitment/sync",
            new { folderPath = Path.Combine(Path.GetTempPath(), "portion-sync-source") });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = document.RootElement.GetProperty("jobId").GetGuid();

        Assert.NotEqual(Guid.Empty, jobId);
        Assert.Equal($"/api/v1/recruitment/sync/{jobId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Schedule_RejectsAnAbsentBody_BecauseAFolderPathIsMandatory()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().PostAsync(
            "/api/v1/recruitment/sync",
            content: null);

        var problem = await ResumeEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");

        Assert.True(problem.GetProperty("errors").TryGetProperty("folderPath", out _), "The offending field must be named.");
    }

    [Fact]
    public async Task Schedule_RejectsABlankFolderPath_WithAValidationProblem()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/recruitment/sync",
            new { folderPath = "   " });

        var problem = await ResumeEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");

        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("errors").GetProperty("folderPath")[0].GetString()));
    }

    [Fact]
    public async Task GetJob_ReportsTheQueuedState_ForAScheduledJob()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/recruitment/sync",
            new { folderPath = Path.Combine(Path.GetTempPath(), "portion-sync-state") });

        using var accepted = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = accepted.RootElement.GetProperty("jobId").GetGuid();

        using var jobResponse = await factory.CreateClient().GetAsync($"/api/v1/recruitment/sync/{jobId}");

        Assert.Equal(HttpStatusCode.OK, jobResponse.StatusCode);

        using var job = JsonDocument.Parse(await jobResponse.Content.ReadAsStringAsync());

        Assert.Equal(jobId, job.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal("Queued", job.RootElement.GetProperty("state").GetString());
        Assert.Equal(0, job.RootElement.GetProperty("discovered").GetInt32());
    }

    [Fact]
    public async Task GetJob_ReturnsProblemDetails_ForAnUnknownJob()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync($"/api/v1/recruitment/sync/{Guid.NewGuid()}");

        await ResumeEndpointTests.AssertProblemAsync(response, HttpStatusCode.NotFound, "resource-not-found");
    }

    [Fact]
    public async Task GetJob_DoesNotMatchAMalformedIdentifier()
    {
        await factory.ResetDatabaseAsync();

        using var response = await factory.CreateClient().GetAsync("/api/v1/recruitment/sync/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
