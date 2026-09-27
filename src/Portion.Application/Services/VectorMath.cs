namespace Portion.Application.Services;

/// <summary>Vector similarity helpers used by the in-memory retrieval fallback.</summary>
public static class VectorMath
{
    /// <summary>Number of decimal places scores are rounded to before being sent to clients.</summary>
    public const int ScorePrecision = 4;

    /// <summary>
    /// Cosine distance in [0, 2] — zero for identical direction, one for orthogonal, greater than
    /// one for opposing. Mismatched lengths and zero-magnitude vectors are treated as maximally
    /// distant, which pushes them to the bottom of any ranking.
    /// </summary>
    public static float CosineDistance(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length || a.Length == 0)
        {
            return 1f;
        }

        float dot = 0f, magnitudeA = 0f, magnitudeB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0f || magnitudeB == 0f)
        {
            return 1f;
        }

        return 1f - dot / (MathF.Sqrt(magnitudeA) * MathF.Sqrt(magnitudeB));
    }

    /// <summary>Cosine similarity in [0, 1], derived as <c>1 - distance</c> and rounded for transport.</summary>
    public static double SimilarityScore(float[] a, float[] b) =>
        Math.Round(1d - CosineDistance(a, b), ScorePrecision, MidpointRounding.AwayFromZero);

    /// <summary>Ranks <paramref name="query" /> against <paramref name="candidate" /> and returns the similarity score.</summary>
    public static double Score(float[] query, float[] candidate) => SimilarityScore(query, candidate);
}
