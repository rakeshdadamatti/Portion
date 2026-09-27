using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Portion.Application.Features.Resumes.Commands;
using Portion.Application.Features.Sync;
using Portion.Domain.Common;
using Portion.Application.Abstractions;
using Portion.Domain.Enums;

namespace Portion.Application.Tests;

/// <summary>Covers the resume upload, delete, list and reconciliation-scheduling slices.</summary>
public class FeatureHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Digest = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    private static Stream Content(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Upload_RejectsAnEmptyFile_WithAValidationError()
    {
        using var database = new SqliteTestDatabase();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            new TestDoubles.StubHashCalculator(Digest),
            new TestDoubles.RecordingIngestionQueue(),
            new TestDoubles.StubUploadPolicy(),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("empty.txt", Content(string.Empty), 0));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.True(result.Error.FieldErrors.ContainsKey("file"));
    }

    [Fact]
    public async Task Upload_RejectsAnUnsupportedExtension_WithAValidationError()
    {
        using var database = new SqliteTestDatabase();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            new TestDoubles.StubHashCalculator(Digest),
            new TestDoubles.RecordingIngestionQueue(),
            new TestDoubles.StubUploadPolicy(),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("malware.exe", Content("MZ"), 2));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Upload_RejectsAnOversizedFile_WithAPayloadTooLargeError()
    {
        using var database = new SqliteTestDatabase();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            new TestDoubles.StubHashCalculator(Digest),
            new TestDoubles.RecordingIngestionQueue(),
            new TestDoubles.StubUploadPolicy(maxBytes: 16),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("big.pdf", Content("pdf bytes"), 100));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.PayloadTooLarge, result.Error.Type);
    }

    [Fact]
    public async Task Upload_PersistsTheResume_AndQueuesItForIngestion()
    {
        using var database = new SqliteTestDatabase();
        var queue = new TestDoubles.RecordingIngestionQueue();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            new TestDoubles.StubHashCalculator(Digest),
            queue,
            new TestDoubles.StubUploadPolicy(),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("Ada Lovelace.txt", Content("resume body"), 12));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.AlreadyIndexed);
        Assert.Single(queue.Enqueued);

        var resume = Assert.Single(database.Db.Resumes);
        Assert.Equal("Ada Lovelace", resume.CandidateName);
        Assert.Equal(IngestionStatus.Processing, resume.Status);
        Assert.Equal(queue.Enqueued[0].ResumeId, resume.Id);
    }

    [Fact]
    public async Task Upload_DeduplicatesByContentHash_AndDiscardsTheDuplicateFile()
    {
        using var database = new SqliteTestDatabase();

        var existing = database.AddResume("Ada Lovelace", status: IngestionStatus.Synced);
        existing.FileHash = Digest;
        database.Save();

        var storage = new TestDoubles.InMemoryFileStorage();
        var queue = new TestDoubles.RecordingIngestionQueue();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            storage,
            new TestDoubles.StubHashCalculator(Digest),
            queue,
            new TestDoubles.StubUploadPolicy(),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("Ada Lovelace.txt", Content("same bytes"), 10));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyIndexed);
        Assert.Equal(existing.Id, result.Value.ResumeId);
        Assert.Empty(queue.Enqueued);
        Assert.Single(storage.Deleted);
    }

    [Fact]
    public async Task Upload_ReusesTheRowOfAPreviouslyFailedAttempt()
    {
        using var database = new SqliteTestDatabase();

        var failed = database.AddResume("Ada", status: IngestionStatus.Failed);
        failed.FileHash = Digest;
        failed.FailureReason = "extractor crashed";
        database.Save();

        var handler = new UploadResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            new TestDoubles.StubHashCalculator(Digest),
            new TestDoubles.RecordingIngestionQueue(),
            new TestDoubles.StubUploadPolicy(),
            new TestDoubles.FixedClock(Now),
            NullLogger<UploadResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new UploadResumeCommand("Ada.txt", Content("retry"), 5));

        Assert.True(result.IsSuccess);
        Assert.Single(database.Db.Resumes);
        Assert.Null(database.Db.Resumes.Local.Single().FailureReason);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_ForAnUnknownResume()
    {
        using var database = new SqliteTestDatabase();

        var handler = new DeleteResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            NullLogger<DeleteResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new DeleteResumeCommand(Guid.NewGuid(), DeleteFile: true));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndItsChunks()
    {
        using var database = new SqliteTestDatabase();

        var resume = database.AddResume("Ada");
        database.AddChunk(resume, 0, "content");
        database.Save();

        var handler = new DeleteResumeCommandHandler(
            database.Db,
            new TestDoubles.InMemoryFileStorage(),
            NullLogger<DeleteResumeCommandHandler>.Instance);

        var result = await handler.HandleAsync(new DeleteResumeCommand(resume.Id, DeleteFile: false));

        Assert.True(result.IsSuccess);
        Assert.Empty(database.Db.Resumes);
        Assert.Empty(database.Db.ResumeChunks);
    }

    [Fact]
    public async Task Delete_RetainsAFileOutsideTheManagedUploadRoot()
    {
        using var database = new SqliteTestDatabase();

        var externalPath = Path.Combine(Path.GetTempPath(), "external-resumes", "Ada.txt");
        var resume = database.AddResume("Ada", externalPath);
        database.Save();

        var storage = new TestDoubles.InMemoryFileStorage();

        var handler = new DeleteResumeCommandHandler(
            database.Db,
            storage,
            NullLogger<DeleteResumeCommandHandler>.Instance);

        await handler.HandleAsync(new DeleteResumeCommand(resume.Id, DeleteFile: true));

        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task Schedule_RejectsABlankFolderPath()
    {
        var tracker = new Infrastructure.Sync.InMemorySyncJobTracker(NullLogger<Infrastructure.Sync.InMemorySyncJobTracker>.Instance);
        var queue = new Infrastructure.Ingestion.ChannelSyncJobQueue();

        var handler = new ScheduleRepositorySyncCommandHandler(
            tracker,
            queue,
            NullLogger<ScheduleRepositorySyncCommandHandler>.Instance);

        var result = await handler.HandleAsync(new ScheduleRepositorySyncCommand("   "));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Schedule_RegistersATrackedJob_ThatThePollingQueryCanFind()
    {
        var tracker = new Infrastructure.Sync.InMemorySyncJobTracker(NullLogger<Infrastructure.Sync.InMemorySyncJobTracker>.Instance);
        var queue = new Infrastructure.Ingestion.ChannelSyncJobQueue();

        var schedule = new ScheduleRepositorySyncCommandHandler(
            tracker,
            queue,
            NullLogger<ScheduleRepositorySyncCommandHandler>.Instance);

        var scheduled = await schedule.HandleAsync(new ScheduleRepositorySyncCommand("C:/resumes"));

        Assert.True(scheduled.IsSuccess);

        var poll = new GetSyncJobQueryHandler(tracker);
        var polled = await poll.HandleAsync(new GetSyncJobQuery(scheduled.Value!.JobId));

        Assert.True(polled.IsSuccess);
        Assert.Equal(SyncJobState.Queued, polled.Value!.State);
    }

    [Fact]
    public async Task Poll_ReturnsNotFound_ForAnUnknownJob()
    {
        var handler = new GetSyncJobQueryHandler(new Infrastructure.Sync.InMemorySyncJobTracker(NullLogger<Infrastructure.Sync.InMemorySyncJobTracker>.Instance));

        var result = await handler.HandleAsync(new GetSyncJobQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
