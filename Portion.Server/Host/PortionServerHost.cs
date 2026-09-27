namespace Portion.Server.Host;

/// <summary>
/// Compatibility entry point for the project that <c>start-all.ps1</c> launches.
/// </summary>
/// <remarks>
/// This type contains no behaviour. It forwards to <see cref="Api.PortionApiHost" />, which is the
/// single composition root for the refactored backend, so the legacy start-up path and
/// <c>dotnet run --project src/Portion.Api</c> execute an identical pipeline. Configuration comes
/// from the <c>appsettings.json</c> files linked into this project's output, so the two hosts resolve
/// their settings from the same source.
/// </remarks>
public static class PortionServerHost
{
    /// <summary>Starts the API.</summary>
    public static void Main(string[] args) => Api.PortionApiHost.Run(args);
}
