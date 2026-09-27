using System.Security.Cryptography;

namespace Portion.Application.Abstractions;

/// <summary>Computes content hashes used for resume deduplication.</summary>
public interface IHashCalculator
{
    /// <summary>Computes the upper-case hex SHA-256 of a byte buffer.</summary>
    string ComputeSha256(ReadOnlySpan<byte> content);

    /// <summary>Computes the upper-case hex SHA-256 of a file, streaming to bound memory usage.</summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IHashCalculator" /> using <see cref="SHA256" />.</summary>
public sealed class DefaultHashCalculator : IHashCalculator
{
    private const int StreamBufferSize = 64 * 1024;

    /// <inheritdoc />
    public string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content));

    /// <inheritdoc />
    public async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest);
    }
}
