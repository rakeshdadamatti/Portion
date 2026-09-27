// Entry point for src/Portion.Api. The pipeline itself lives in PortionApiHost so the compatibility
// host in Portion.Server can run exactly the same application.
Portion.Api.PortionApiHost.Run(args);

/// <summary>Exposed so the integration test host can reference the entry-point assembly.</summary>
public partial class Program;
