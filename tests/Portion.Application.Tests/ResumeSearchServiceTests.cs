using Portion.Application.Abstractions;
using Portion.Application.Services;

namespace Portion.Application.Tests;

/// <summary>Covers the retrieval cascade, which decides what grounds a screening answer.</summary>
public class ResumeSearchServiceTests
{
    [Fact]
    public async Task RetrieveAsync_PrefersVectorSearch_WhenAnEmbeddingIsAvailable()
    {
        using var database = new SqliteTestDatabase();

        var resume = database.AddResume("Ada");
        database.AddChunk(resume, 0, "analytical engine notes");
        database.Save();

        var expected = new ScoredChunk(resume.Id, "Ada", Guid.NewGuid(), "vector hit", 0.93);
        var vectorSearch = new TestDoubles.StubVectorSearch(expected);

        var service = new ResumeSearchService(database.Db, vectorSearch);

        var result = await service.RetrieveAsync(new ScreeningRequest("who worked on the engine?", 5, [0.1f, 0.2f]));

        Assert.Equal(RetrievalMode.Vector, result.Mode);
        Assert.Equal(expected, Assert.Single(result.Chunks));
        Assert.Equal(1, vectorSearch.CallCount);
    }

    [Fact]
    public async Task RetrieveAsync_RespectsTopK_ForVectorResults()
    {
        using var database = new SqliteTestDatabase();

        var vectorSearch = new TestDoubles.StubVectorSearch(
            new ScoredChunk(Guid.NewGuid(), "A", Guid.NewGuid(), "one", 0.9),
            new ScoredChunk(Guid.NewGuid(), "B", Guid.NewGuid(), "two", 0.8),
            new ScoredChunk(Guid.NewGuid(), "C", Guid.NewGuid(), "three", 0.7));

        var service = new ResumeSearchService(database.Db, vectorSearch);

        var result = await service.RetrieveAsync(new ScreeningRequest("question", 2, [0.5f]));

        Assert.Equal(2, result.Chunks.Count);
    }

    [Fact]
    public async Task RetrieveAsync_FallsBackToKeywordMatching_WhenVectorSearchReturnsNothing()
    {
        using var database = new SqliteTestDatabase();

        var ada = database.AddResume("Ada");
        database.AddChunk(ada, 0, "Designed the analytical engine programme.");

        var grace = database.AddResume("Grace");
        database.AddChunk(grace, 0, "Compiled the first compiler.");

        database.Save();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        var result = await service.RetrieveAsync(new ScreeningRequest("analytical engine", 5));

        Assert.Equal(RetrievalMode.Keyword, result.Mode);
        Assert.Equal("Ada", Assert.Single(result.Chunks).CandidateName);
    }

    [Fact]
    public async Task RetrieveAsync_DoesNotTreatLikeMetacharactersAsWildcards()
    {
        using var database = new SqliteTestDatabase();

        var ada = database.AddResume("Ada");
        database.AddChunk(ada, 0, "nothing relevant here at all");

        var grace = database.AddResume("Grace");
        database.AddChunk(grace, 0, "worked on the analytical engine");

        database.Save();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        // '%' is escaped, so this matches nothing and the cascade degrades rather than returning
        // every row in the table.
        var result = await service.RetrieveAsync(new ScreeningRequest("100%", 5));

        Assert.Equal(RetrievalMode.FirstAvailable, result.Mode);
    }

    [Fact]
    public async Task RetrieveAsync_FallsBackToTheFirstChunks_WhenNothingMatches()
    {
        using var database = new SqliteTestDatabase();

        var resume = database.AddResume("Ada");
        database.AddChunk(resume, 0, "unrelated content");
        database.Save();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        var result = await service.RetrieveAsync(new ScreeningRequest("kubernetes operator", 5));

        Assert.Equal(RetrievalMode.FirstAvailable, result.Mode);
        Assert.Single(result.Chunks);
    }

    [Fact]
    public async Task RetrieveAsync_ReportsNone_ForAnEmptyCorpus()
    {
        using var database = new SqliteTestDatabase();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        var result = await service.RetrieveAsync(new ScreeningRequest("anything", 5));

        Assert.Equal(RetrievalMode.None, result.Mode);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task RetrieveAsync_SkipsVectorSearchEntirely_WhenNoEmbeddingWasSupplied()
    {
        using var database = new SqliteTestDatabase();

        var resume = database.AddResume("Ada");
        database.AddChunk(resume, 0, "the analytical engine");
        database.Save();

        var vectorSearch = new TestDoubles.StubVectorSearch();
        var service = new ResumeSearchService(database.Db, vectorSearch);

        var result = await service.RetrieveAsync(new ScreeningRequest("engine", 5));

        Assert.Equal(RetrievalMode.Keyword, result.Mode);
        Assert.Equal(0, vectorSearch.CallCount);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNone_ForABlankQuery()
    {
        using var database = new SqliteTestDatabase();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        var result = await service.RetrieveAsync(new ScreeningRequest("   ", 5));

        Assert.Equal(RetrievalMode.None, result.Mode);
    }

    [Fact]
    public async Task RetrieveAsync_RejectsANonPositiveTopK()
    {
        using var database = new SqliteTestDatabase();

        var service = new ResumeSearchService(database.Db, new TestDoubles.EmptyVectorSearch());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RetrieveAsync(new ScreeningRequest("query", 0)));
    }
}
