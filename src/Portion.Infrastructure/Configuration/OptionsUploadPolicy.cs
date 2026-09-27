using Portion.Application.Features.Resumes.Commands;

namespace Portion.Infrastructure.Configuration;

/// <summary>
/// Adapts <see cref="IngestionOptions" /> to the upload contract the application layer depends on,
/// so that the command handler never has to know about configuration binding.
/// </summary>
public sealed class OptionsUploadPolicy : IUploadPolicy
{
    private readonly HashSet<string> _supported;

    /// <summary>Creates the policy from validated options.</summary>
    public OptionsUploadPolicy(IngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        MaxUploadBytes = options.MaxUploadBytes;
        _supported = new HashSet<string>(options.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        SupportedExtensions = _supported.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <inheritdoc />
    public long MaxUploadBytes { get; }

    /// <inheritdoc />
    public bool IsSupported(string extension) =>
        !string.IsNullOrWhiteSpace(extension) && _supported.Contains(extension);
}
