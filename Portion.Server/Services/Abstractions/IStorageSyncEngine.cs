namespace Portion.Server.Services.Abstractions
{
    public interface IStorageSyncEngine
    {
        Task ReconcileRepositoryAsync(string directoryPath, CancellationToken token);
        string ComputeFileHash(string filePath);
    }
}
