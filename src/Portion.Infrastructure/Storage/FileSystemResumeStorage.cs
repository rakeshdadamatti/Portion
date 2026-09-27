using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Storage;

/// <summary>
/// Stores uploaded resumes as GUID-prefixed files inside the managed upload directory.
/// </summary>
/// <remarks>
/// <para>
/// The GUID prefix is the collision guard: two candidates uploading <c>Resume.pdf</c> must not
/// overwrite each other, and the original name is preserved for display and for the extractor to
/// infer the format from.
/// </para>
/// <para>
/// <see cref="IsWithinUploadRoot" /> is a security boundary, not a convenience. A resume indexed by
/// folder reconciliation can point anywhere on disk, so the delete path is only allowed to remove
/// files the application itself wrote.
/// </para>
/// </remarks>
public sealed class FileSystemResumeStorage : IFileStorage
{
    private const int CopyBufferSize = 81_920;

    private readonly ILogger<FileSystemResumeStorage> _logger;
    private readonly string _uploadRootWithSeparator;

    /// <summary>Creates the storage over a resolved layout.</summary>
    public FileSystemResumeStorage(StorageLayout layout, ILogger<FileSystemResumeStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        UploadRoot = layout.UploadRoot;
        _uploadRootWithSeparator = UploadRoot.EndsWith(Path.DirectorySeparatorChar)
            ? UploadRoot
            : UploadRoot + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc />
    public string UploadRoot { get; }

    /// <inheritdoc />
    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Directory.CreateDirectory(UploadRoot);

        var safeName = SanitizeFileName(fileName);
        var target = Path.Combine(UploadRoot, $"{Guid.NewGuid()}_{safeName}");

        long bytesWritten;

        await using (var stream = new FileStream(
                         target,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         CopyBufferSize,
                         FileOptions.Asynchronous))
        {
            await content.CopyToAsync(stream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
            bytesWritten = stream.Length;
        }

        _logger.LogInformation("Stored upload {Target} ({Bytes} bytes).", target, bytesWritten);

        return new StoredFile(target, safeName);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !IsWithinUploadRoot(absolutePath))
        {
            return Task.CompletedTask;
        }

        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be removed (locked, read-only) must not fail the HTTP request that
            // triggered the delete; the orphan is reported and swept later.
            _logger.LogWarning(ex, "Could not delete managed upload {Path}.", absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsWithinUploadRoot(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(absolutePath);
            return full.StartsWith(_uploadRootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// Strips directory components and characters that are invalid in a file name, so a crafted
    /// client-supplied name cannot escape the upload directory.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(leaf))
        {
            leaf = "resume";
        }

        var sanitized = string.Concat(leaf.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

        return sanitized.Length == 0 ? "resume" : sanitized;
    }
}
