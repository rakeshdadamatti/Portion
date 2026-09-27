using Portion.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Portion.Infrastructure.Storage;

/// <summary>
/// Resolves the base directory that all relative runtime paths are anchored to.
/// </summary>
/// <remarks>
/// <para>
/// The API is launched from three different working directories in normal use: the repository root
/// (via <c>start-all.ps1</c>), a project directory (via <c>dotnet run --project src/Portion.Api</c>),
/// and a published output folder. A bare relative path would silently create a second, empty
/// database in whichever of those happened to be the working directory, orphaning the existing
/// developer data.
/// </para>
/// <para>Resolution order, first match wins:</para>
/// <list type="number">
///   <item><description><see cref="StorageOptions.AppDataRoot" />, when explicitly configured.</description></item>
///   <item><description><c>&lt;cwd&gt;/Portion.Server</c>, when launched from the repository root.</description></item>
///   <item><description><c>&lt;cwd&gt;/../Portion.Server</c>, when launched from a project directory.</description></item>
///   <item><description>The working directory itself, for a fresh deployment with no prior data.</description></item>
/// </list>
/// </remarks>
public static class DataRootResolver
{
    /// <summary>Legacy folder that holds the local SQLite database and the checked-in upload samples.</summary>
    public const string LegacyDataFolderName = "Portion.Server";

    /// <summary>Resolves the effective data root.</summary>
    public static string Resolve(string? appDataRoot, string? workingDirectory = null)
    {
        var cwd = workingDirectory ?? Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(appDataRoot))
        {
            var configured = Path.GetFullPath(Path.Combine(cwd, appDataRoot));

            if (Directory.Exists(configured) || ShouldCreate(configured, appDataRoot))
            {
                return configured;
            }
        }

        var nested = Path.Combine(cwd, LegacyDataFolderName);
        if (Directory.Exists(nested))
        {
            return nested;
        }

        var sibling = Path.GetFullPath(Path.Combine(cwd, "..", LegacyDataFolderName));
        if (Directory.Exists(sibling))
        {
            return sibling;
        }

        return cwd;
    }

    /// <summary>Combines <paramref name="relativePath" /> onto the resolved data root.</summary>
    public static string Combine(string dataRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(dataRoot, relativePath));

    /// <summary>
    /// An explicitly configured root is honoured even when it does not exist yet — a fresh deployment
    /// points at a directory it is about to create — but only when the value names a concrete
    /// location rather than the bare current directory.
    /// </summary>
    private static bool ShouldCreate(string candidate, string configured) =>
        !string.IsNullOrWhiteSpace(configured) &&
        !Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured))
            .Equals(Path.GetFullPath(Directory.GetCurrentDirectory()), StringComparison.OrdinalIgnoreCase);
}
