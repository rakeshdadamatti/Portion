namespace Portion.Server.Models.Dtos
{
    public record FolderSyncRequestDto(string FolderPath);

    public record UploadResponseDto(string Message, Guid ResumeId);

    public record SyncResponseDto(string Message);

    public class ApiResponse
    {
        public bool Success { get; init; }
        public string? Message { get; init; }

        public static ApiResponse Ok(string? message = null) =>
            new() { Success = true, Message = message };

        public static ApiResponse Fail(string message) =>
            new() { Success = false, Message = message };
    }

    public class ApiResponse<T> : ApiResponse
    {
        public T? Data { get; init; }

        public static ApiResponse<T> Ok(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message };

        public new static ApiResponse<T> Fail(string message) =>
            new() { Success = false, Message = message };
    }
}
