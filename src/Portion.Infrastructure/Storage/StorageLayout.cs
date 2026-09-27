using Portion.Application.Abstractions;

namespace Portion.Infrastructure.Storage;

/// <summary>
/// Resolved, process-wide locations used by the application: where uploads live and where a relative
/// SQLite data file should be anchored.
/// </summary>
public sealed class StorageLayout
{
    /// <summary>Creates a layout from a resolved data root and an upload directory name.</summary>
    public StorageLayout(string dataRoot, string uploadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadDirectory);

        DataRoot = Path.GetFullPath(dataRoot);
        UploadRoot = Path.GetFullPath(Path.Combine(DataRoot, uploadDirectory));
    }

    /// <summary>Base directory for every relative runtime path.</summary>
    public string DataRoot { get; }

    /// <summary>Absolute path of the managed upload directory.</summary>
    public string UploadRoot { get; }
}
