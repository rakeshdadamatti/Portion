using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Portion.Application.Abstractions;
using Portion.Domain.Enums;
using Portion.Infrastructure.Ingestion;
using Portion.Infrastructure.Persistence;
using Portion.Infrastructure.Sourcing;

namespace Portion.Api.Tests;

/// <summary>
/// Proves that the background pipeline is actually wired into the host, and that an uploaded resume
/// reaches <see cref="IngestionStatus.Synced" /> without any further interaction.
/// </summary>
/// <remarks>
/// <para>
/// These tests exist because of a real defect. <c>DependencyInjection.cs</c> registered the workers
/// with <c>TryAddSingleton&lt;IHostedService&gt;(…)</c>, but <c>AddPortionPersistence</c> had already
/// registered an <see cref="IHostedService" /> (the database initializer) and <c>TryAdd</c> is a
/// no-op once a descriptor for the service type exists. Both workers were therefore never started:
/// uploads returned <c>202</c>, the bounded channel filled, nothing drained it, and every resume sat
/// in <see cref="IngestionStatus.Processing" /> forever with no error anywhere.
/// </para>
/// <para>
/// The existing <see cref="ApiTestFactory" /> calls <c>RemoveAll&lt;IHostedService&gt;()</c>, so it
/// is structurally incapable of detecting a missing worker registration. This fixture deliberately
/// does the opposite — it keeps the production hosted services and only swaps the embedding
/// generator, so the real <see cref="ResumeIngestionWorker" /> runs.
/// </para>
/// <para>
/// A file-backed SQLite database is used rather than the shared in-memory connection the other
/// fixture relies on: the worker opens its own DI scope and therefore its own
/// <see cref="ApplicationDbContext" />, and one <see cref="Microsoft.Data.Sqlite.SqliteConnection" />
/// cannot safely back two contexts used concurrently.
/// </para>
/// </remarks>
public sealed class IngestionPipelineTests : IClassFixture<IngestionPipelineFixture>
{
    private readonly IngestionPipelineFixture _fixture;

    public IngestionPipelineTests(IngestionPipelineFixture fixture) => _fixture = fixture;

    [Fact]
    public void ProductionCompositionRoot_RegistersTheIngestionAndSyncWorkersAsHostedServices()
    {
        using var provider = _fixture.BuildProductionContainer();
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(hosted, service => service is ResumeIngestionWorker);
        Assert.Contains(hosted, service => service is RepositorySyncWorker);
        Assert.Contains(hosted, service => service is IngestionRecoveryService);

        // Guards the specific regression: TryAdd silently dropped these because another
        // IHostedService descriptor already existed. The count is asserted as well so a future
        // removal cannot pass by leaving a different IHostedService registered.
        Assert.True(
            hosted.OfType<ResumeIngestionWorker>().Count() == 1,
            "Exactly one ResumeIngestionWorker must be registered.");
        Assert.True(
            hosted.OfType<RepositorySyncWorker>().Count() == 1,
            "Exactly one RepositorySyncWorker must be registered.");
    }

    [Fact]
    public void ProductionCompositionRoot_DoesNotReuseTheSameHostedServiceInstance()
    {
        using var provider = _fixture.BuildProductionContainer();
        var hosted = provider.GetServices<IHostedService>().ToList();

        // The worker is registered twice on purpose: once as a concrete singleton (so tests can
        // resolve and drive it) and once as the IHostedService the host starts. Both descriptors
        // must yield the same instance, or the started worker would be a different object from the
        // one the queue is wired into.
        var worker = provider.GetRequiredService<ResumeIngestionWorker>();

        Assert.Same(worker, hosted.OfType<ResumeIngestionWorker>().Single());
    }

    [Fact]
    public async Task UploadedResume_ReachesSynced_ThroughTheRealBackgroundWorker()
    {
        using var client = _fixture.CreateClient();

        var content = BuildResumeText("Ada Lovelace", "Distributed systems", "PostgreSQL tuning");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "ada-lovelace.txt");

        var response = await client.PostAsync("/api/v1/recruitment/resumes", form);
        response.EnsureSuccessStatusCode();

        var accepted = await response.Content.ReadFromJsonAsync<UploadAccepted>();
        Assert.NotNull(accepted);
        Assert.False(accepted!.AlreadyIndexed);

        // Polled rather than awaited directly: the whole point is that the request returns before
        // the work is done, and that a hosted service picks it up on its own.
        var status = await _fixture.WaitForStatusAsync(accepted.ResumeId, TimeSpan.FromSeconds(30));

        Assert.Equal(IngestionStatus.Synced, status);

        var chunks = await _fixture.ReadChunkCountAsync(accepted.ResumeId);
        Assert.True(chunks > 0, "A synced resume must have at least one indexed chunk.");
    }

    [Fact]
    public async Task Recovery_ReQueuesAResumeLeftProcessingByAPreviousProcess()
    {
        using var client = _fixture.CreateClient();

        var path = Path.Combine(_fixture.DataRoot, "UploadedResumes", "orphaned.txt");
        await File.WriteAllTextAsync(path, BuildResumeText("Grace Hopper", "COBOL compilers"));

        var resumeId = Guid.NewGuid();

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Resumes.Add(new Domain.Entities.Resume
            {
                Id = resumeId,
                CandidateName = "Grace Hopper",
                FilePath = path,
                FileHash = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                Status = IngestionStatus.Processing,
                FailureReason = null,
                CreatedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await _fixture.RunRecoveryAsync();

        var status = await _fixture.WaitForStatusAsync(resumeId, TimeSpan.FromSeconds(30));
        Assert.Equal(IngestionStatus.Synced, status);
    }

    [Fact]
    public async Task Recovery_MarksFailedWhenTheSourceFileIsGone()
    {
        using var client = _fixture.CreateClient();

        var resumeId = Guid.NewGuid();
        var missingPath = Path.Combine(_fixture.DataRoot, "UploadedResumes", "no-longer-here.txt");

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Resumes.Add(new Domain.Entities.Resume
            {
                Id = resumeId,
                CandidateName = "Deleted Source",
                FilePath = missingPath,
                FileHash = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                Status = IngestionStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                LastSyncedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await _fixture.RunRecoveryAsync();

        var resume = await _fixture.ReadResumeAsync(resumeId);

        Assert.Equal(IngestionStatus.Failed, resume!.Status);
        Assert.Equal(IngestionRecoveryService.MissingSourceReason, resume.FailureReason);
    }

    private static string BuildResumeText(params string[] skills) =>
        string.Join(
            ' ',
            new[] { "Curriculum Vitae." }
                .Concat(skills)
                .Concat(["Experience spanning platform engineering and distributed systems."])
                .Concat(Enumerable.Repeat("Delivered measurable reliability improvements.", 40)));

    private sealed record UploadAccepted(string Message, Guid ResumeId, bool AlreadyIndexed);
}
