using System.Security.Cryptography;
using Portion.Domain.Common;

namespace Portion.Domain.ValueObjects;

/// <summary>
/// Content-addressed identity of a document: the upper-case hex SHA-256 of its bytes.
/// Used to deduplicate uploads and to make folder reconciliation idempotent.
/// </summary>
public readonly record struct FileHash
{
    private FileHash(string value) => Value = value;

    /// <summary>Number of hex characters in a SHA-256 digest.</summary>
    public const int Length = 64;

    /// <summary>The upper-case hex digest.</summary>
    public string Value { get; }

    /// <summary>True when the hash is well-formed.</summary>
    public bool IsValid => Value.Length == Length;

    /// <summary>Wraps an already-computed digest.</summary>
    public static FileHash FromString(string value)
    {
        Guard.Against.NotNullOrWhiteSpace(value);
        return new FileHash(value.Trim().ToUpperInvariant());
    }

    /// <summary>Computes the digest of a byte buffer.</summary>
    public static FileHash FromBytes(ReadOnlySpan<byte> content) =>
        new(Convert.ToHexString(SHA256.HashData(content)));

    /// <summary>Reads a file and computes its digest without loading the whole file into memory.</summary>
    public static FileHash FromFile(string filePath)
    {
        Guard.Against.NotNullOrWhiteSpace(filePath);

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        return new FileHash(Convert.ToHexString(SHA256.HashData(stream)));
    }

    public override string ToString() => Value;
}
