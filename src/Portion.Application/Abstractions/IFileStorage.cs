namespace Portion.Application.Abstractions;

/// <summary>A file that has been written to the managed upload directory.</summary>
/// <param name="AbsolutePath">Fully qualified path on disk.</param>
/// <param name="FileName">Original (sanitised) file name including extension.</param>
public sealed record StoredFile(string AbsolutePath, string FileName);

/// <summary>Managed storage for uploaded resume documents.</summary>
public interface IFileStorage
{
    /// <summary>Absolute path of the directory that owns managed uploads.</summary>
    string UploadRoot { get; }

    /// <summary>Writes a stream to a new GUID-prefixed file inside the upload root.</summary>
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a file, ignoring a missing target. No-ops for paths outside the upload root.</summary>
    Task DeleteAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>True when the path is inside <see cref="UploadRoot" />.</summary>
    bool IsWithinUploadRoot(string absolutePath);
}
