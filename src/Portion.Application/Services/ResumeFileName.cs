namespace Portion.Application.Services;

/// <summary>Derives display metadata from a stored resume file path.</summary>
public static class ResumeFileName
{
    /// <summary>
    /// Returns the file name portion of <paramref name="filePath" />, falling back to the path
    /// itself when it is empty. Deliberately derived rather than persisted: the resume schema is
    /// frozen for compatibility with the existing local SQLite database.
    /// </summary>
    public static string Derive(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(filePath);
        return string.IsNullOrWhiteSpace(name) ? filePath : name;
    }
}
