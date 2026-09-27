using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Portion.Application.Abstractions;
using Portion.Domain.Entities;
using Portion.Domain.Enums;
using Portion.Infrastructure.Persistence;

namespace Portion.Application.Tests;

/// <summary>
/// Provides a real SQLite-backed <see cref="ApplicationDbContext" /> for tests.
/// </summary>
/// <remarks>
/// An in-memory SQLite database is used rather than an EF in-memory provider because these tests
/// exercise actual SQL translation — the <c>LIKE</c> keyword fallback and the ordering of the
/// first-available pass are the behaviour under test, and a provider that skips SQL generation
/// cannot verify either. The connection is kept open for the lifetime of the fixture because closing
/// it destroys an in-memory database.
/// </remarks>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options);

        Db.Database.EnsureCreated();
    }

    public ApplicationDbContext Db { get; }

    public Resume AddResume(string candidateName, string? filePath = null, IngestionStatus status = IngestionStatus.Synced)
    {
        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            CandidateName = candidateName,
            FilePath = filePath ?? $"C:/uploads/{candidateName}.txt",
            FileHash = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Status = status,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 1000)),
            LastSyncedAt = DateTime.UtcNow
        };

        Db.Resumes.Add(resume);

        return resume;
    }

    public ResumeChunk AddChunk(Resume resume, int index, string text)
    {
        var chunk = new ResumeChunk
        {
            Id = Guid.NewGuid(),
            ResumeId = resume.Id,
            TextContent = text,
            ChunkIndex = index
        };

        Db.ResumeChunks.Add(chunk);

        return chunk;
    }

    public void Save() => Db.SaveChanges();

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
