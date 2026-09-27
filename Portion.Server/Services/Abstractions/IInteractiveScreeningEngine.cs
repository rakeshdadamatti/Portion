namespace Portion.Server.Services.Abstractions
{
    public interface IInteractiveScreeningEngine
    {
        IAsyncEnumerable<string> ExecuteStreamedScreeningAsync(string hrQuery, CancellationToken cancellationToken = default);
    }
}
