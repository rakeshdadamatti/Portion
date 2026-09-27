using Portion.Application.Services;

namespace Portion.Application.Tests;

/// <summary>Covers the pure text utilities: chunking, similarity scoring and prompt assembly.</summary>
public class TextPipelineTests
{
    [Fact]
    public void Chunk_ReturnsNothing_ForEmptyInput()
    {
        Assert.Empty(new TextChunker().Chunk(string.Empty, 500, 50));
    }

    [Fact]
    public void Chunk_ReturnsASingleChunk_WhenTheTextFits()
    {
        var chunks = new TextChunker().Chunk("one two three four five", chunkSize: 500, overlap: 50);

        Assert.Equal("one two three four five", Assert.Single(chunks));
    }

    [Fact]
    public void Chunk_EmitsOverlappingWindows_ThatReassembleTheDocument()
    {
        const string text = "w1 w2 w3 w4 w5 w6 w7 w8 w9 w10 w11 w12";

        var chunks = new TextChunker().Chunk(text, chunkSize: 5, overlap: 2);

        Assert.True(chunks.Count > 1, "Expected more than one chunk for a twelve word document.");
        Assert.All(chunks, c => Assert.True(c.Split(' ').Length <= 5));

        // The first window starts at the document start and the last ends at the document end, so no
        // text is lost at either boundary.
        Assert.StartsWith("w1 w2", chunks[0]);
        Assert.EndsWith("w12", chunks[^1]);
    }

    [Fact]
    public void Chunk_AlwaysProducesAtLeastOneChunk_ForNonEmptyInput()
    {
        var chunks = new TextChunker().Chunk("token", chunkSize: 1, overlap: 0);

        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_RejectsANonPositiveChunkSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker().Chunk("text", 0, 0));
    }

    [Fact]
    public void CosineDistance_IsZeroForIdenticalVectors()
    {
        var vector = new[] { 0.1f, 0.9f, -0.4f };

        Assert.Equal(0f, VectorMath.CosineDistance(vector, vector), precision: 5);
    }

    [Fact]
    public void SimilarityScore_IsOneForIdenticalVectors_AndZeroForOrthogonalOnes()
    {
        Assert.Equal(1d, VectorMath.SimilarityScore([1f, 0f], [1f, 0f]));

        Assert.Equal(0d, VectorMath.SimilarityScore([1f, 0f], [0f, 1f]));
    }

    [Fact]
    public void SimilarityScore_TreatsMismatchedDimensionsAsMaximallyDistant()
    {
        Assert.Equal(0d, VectorMath.SimilarityScore([1f, 0f], [1f, 0f, 0f]));
    }

    [Fact]
    public void SimilarityScore_TreatsAZeroVectorAsMaximallyDistant_RatherThanDividingByZero()
    {
        Assert.Equal(0d, VectorMath.SimilarityScore([0f, 0f], [1f, 1f]));
    }

    [Fact]
    public void SimilarityScore_IsRoundedToFourDecimals_SoItIsStableOnTheWire()
    {
        var score = VectorMath.SimilarityScore([1f, 1f], [1f, 0f]);

        Assert.Equal(score, Math.Round(score, 4));
    }

    [Fact]
    public void BuildUserPrompt_IncludesTheGroundingContextAndTheQuestion()
    {
        var context = "--- Ada Lovelace ---\nDesigned the analytical engine.";

        var prompt = PromptBuilder.BuildUserPrompt(context, "Who worked on the engine?");

        Assert.Contains("Ada Lovelace", prompt);
        Assert.Contains("Who worked on the engine?", prompt);
    }

    [Fact]
    public void SystemPrompt_ForbidsInventingCandidates()
    {
        Assert.Contains("provided", PromptBuilder.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUserPrompt_SubstitutesTheNotice_WhenTheCorpusIsEmpty()
    {
        var prompt = PromptBuilder.BuildUserPrompt(PromptBuilder.EmptyCorpusNotice, "Who is available?");

        Assert.Contains(PromptBuilder.EmptyCorpusNotice, prompt);
    }

    [Theory]
    [InlineData("C:/uploads/Ada Lovelace.pdf", "Ada Lovelace.pdf")]
    [InlineData("/tmp/portion/Grace Hopper.docx", "Grace Hopper.docx")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Derive_ExtractsJustTheFileName(string? path, string expected)
    {
        Assert.Equal(expected, ResumeFileName.Derive(path));
    }
}
