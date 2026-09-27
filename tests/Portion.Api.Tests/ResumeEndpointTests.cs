using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portion.Domain.Enums;
using Portion.Infrastructure.Persistence;

namespace Portion.Api.Tests;

/// <summary>Covers the resume endpoints, including the RFC 7807 error contract.</summary>
public class ResumeEndpointTests(ApiTestFactory factory) : IClassFixture<ApiTestFactory>
{
    [Fact]
    public async Task List_ReturnsAnEmptyPage_WhenNothingIsIndexed()
    {
        await factory.ResetDatabaseAsync();

        var response = await factory.CreateClient().GetAsync("/api/v1/recruitment/resumes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(25, root.GetProperty("pageSize").GetInt32());
        Assert.Empty(root.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task List_ReturnsSeededResumes_WithDerivedFileNameAndChunkCounts()
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedResumeAsync("Ada Lovelace", chunkCount: 3);

        var response = await factory.CreateClient().GetAsync("/api/v1/recruitment/resumes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();

        Assert.Equal("Ada Lovelace", item.GetProperty("candidateName").GetString());

        // The name is derived from the stored path rather than persisted, which is why the schema can
        // stay frozen.
        Assert.Equal("Ada Lovelace.txt", item.GetProperty("fileName").GetString());

        // Enums travel as names, not ordinals.
        Assert.Equal("Synced", item.GetProperty("status").GetString());
        Assert.Equal(3, item.GetProperty("chunkCount").GetInt32());
        Assert.Equal(0, item.GetProperty("embeddedChunkCount").GetInt32());
    }

    [Fact]
    public async Task List_FiltersByCandidateName()
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedResumeAsync("Ada Lovelace");
        await factory.SeedResumeAsync("Grace Hopper");

        var response = await factory.CreateClient().GetAsync("/api/v1/recruitment/resumes?q=grace");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "Grace Hopper",
            document.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("candidateName").GetString());
    }

    [Fact]
    public async Task List_ReturnsAValidationProblem_ForAnOutOfRangePageSize()
    {
        await factory.ResetDatabaseAsync();

        var response = await factory.CreateClient().GetAsync("/api/v1/recruitment/resumes?pageSize=5000");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Get_ReturnsTheResumeWithItsChunks()
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedResumeAsync("Ada Lovelace", chunkCount: 2);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Resumes.Select(r => r.Id).FirstAsync();

        var response = await factory.CreateClient().GetAsync($"/api/v1/recruitment/resumes/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("Ada Lovelace", document.RootElement.GetProperty("candidateName").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("chunks").GetArrayLength());
    }

    [Fact]
    public async Task Get_ReturnsAProblemDetailsDocument_ForAnUnknownResume()
    {
        await factory.ResetDatabaseAsync();

        var response = await factory.CreateClient().GetAsync($"/api/v1/recruitment/resumes/{Guid.NewGuid()}");

        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "resource-not-found");

        Assert.Contains("resource-not-found", problem.GetProperty("type").GetString()!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Delete_RemovesTheResume_AndReturnsNoContent()
    {
        await factory.ResetDatabaseAsync();
        await factory.SeedResumeAsync("Ada Lovelace");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await db.Resumes.Select(r => r.Id).FirstAsync();

        var response = await factory.CreateClient().DeleteAsync($"/api/v1/recruitment/resumes/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(await verifyDb.Resumes.ToListAsync());
    }

    [Fact]
    public async Task Delete_ReturnsProblemDetails_ForAnUnknownResume()
    {
        await factory.ResetDatabaseAsync();

        var response = await factory.CreateClient().DeleteAsync($"/api/v1/recruitment/resumes/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "resource-not-found");
    }

    [Fact]
    public async Task Upload_RejectsARequestWithNoFilePart_AsAValidationProblem()
    {
        await factory.ResetDatabaseAsync();

        using var content = new MultipartFormDataContent();
        using var response = await factory.CreateClient().PostAsync("/api/v1/recruitment/resumes", content);

        // Form binding fails before the handler runs, so only the status and the document shape are
        // guaranteed here; the named-field contract is asserted by the extension test below.
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");
    }

    [Fact]
    public async Task Upload_RejectsAnUnsupportedExtension_AndNamesTheOffendingField()
    {
        await factory.ResetDatabaseAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("MZ")), "file", "payload.exe");

        using var response = await factory.CreateClient().PostAsync("/api/v1/recruitment/resumes", content);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation-failed");

        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("errors").GetProperty("file")[0].GetString()));
    }

    [Fact]
    public async Task Upload_AcceptsASupportedDocument_AndQueuesIt()
    {
        await factory.ResetDatabaseAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Ada Lovelace\nEngine programmer.")), "file", "Ada Lovelace.txt");

        using var response = await factory.CreateClient().PostAsync("/api/v1/recruitment/resumes", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.False(document.RootElement.GetProperty("alreadyIndexed").GetBoolean());
        Assert.Equal("Processing", await ReadStatusAsync());
    }

    [Fact]
    public async Task Upload_DeduplicatesContent_ThatIsAlreadySynced()
    {
        await factory.ResetDatabaseAsync();

        using var first = await UploadAsync("Ada Lovelace.txt");

        using (var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.False(document.RootElement.GetProperty("alreadyIndexed").GetBoolean());
        }

        // The ingestion worker is disabled in tests, so the row is promoted by hand to the state a
        // completed ingestion would leave behind.
        await factory.WithDatabaseAsync(async db =>
        {
            var resume = await db.Resumes.FirstAsync();
            resume.Status = IngestionStatus.Synced;
            await db.SaveChangesAsync();
        });

        using var second = await UploadAsync("Ada Lovelace.txt");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        Assert.True(secondBody.RootElement.GetProperty("alreadyIndexed").GetBoolean());
    }

    [Fact]
    public async Task Upload_ReUsesTheRow_OfAPreviouslyFailedAttempt()
    {
        await factory.ResetDatabaseAsync();

        using var first = await UploadAsync("Grace Hopper.txt");

        Guid resumeId;

        using (var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            resumeId = document.RootElement.GetProperty("resumeId").GetGuid();
        }

        await factory.WithDatabaseAsync(async db =>
        {
            var resume = await db.Resumes.FirstAsync();
            resume.Status = IngestionStatus.Failed;
            resume.FailureReason = "extractor crashed";
            await db.SaveChangesAsync();
        });

        using var second = await UploadAsync("Grace Hopper.txt");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        using var retry = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        // Same row, retried rather than duplicated, with the stale failure cleared.
        Assert.Equal(resumeId, retry.RootElement.GetProperty("resumeId").GetGuid());
        Assert.False(retry.RootElement.GetProperty("alreadyIndexed").GetBoolean());

        var rows = await factory.ReadResumesAsync();

        Assert.Single(rows);
        Assert.Equal(IngestionStatus.Processing, rows[0].Status);
        Assert.Null(rows[0].FailureReason);
    }

    private async Task<HttpResponseMessage> UploadAsync(string fileName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Ada Lovelace\nEngine programmer.")), "file", fileName);

        return await factory.CreateClient().PostAsync("/api/v1/recruitment/resumes", content);
    }

    private async Task<string?> ReadStatusAsync()
    {
        var rows = await factory.ReadResumesAsync();

        return rows.Count == 0 ? null : rows[0].Status.ToString();
    }

    internal static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTypeSuffix)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement.Clone();

        Assert.EndsWith(expectedTypeSuffix, problem.GetProperty("type").GetString()!, StringComparison.Ordinal);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        Assert.StartsWith("/", problem.GetProperty("instance").GetString()!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));

        return problem;
    }
}
