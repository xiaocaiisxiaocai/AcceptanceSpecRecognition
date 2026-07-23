namespace AcceptanceSpecSystem.Application.Services;

public readonly record struct MatchingOperationResult<T>(T Data, string Message);

public readonly record struct MatchingDownloadResult(Stream Content, string ContentType, string FileName);

public sealed class MatchingApiException : Exception
{
    public MatchingApiException(int code, string message, bool isNotFound = false)
        : base(message)
    {
        Code = code;
        IsNotFound = isNotFound;
    }

    public int Code { get; }
    public bool IsNotFound { get; }
}

public sealed class GeneratedArtifactFile
{
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}
