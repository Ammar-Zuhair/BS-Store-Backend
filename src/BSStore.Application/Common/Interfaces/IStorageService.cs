namespace BSStore.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string folder = "general", CancellationToken ct = default);
    Task<bool> DeleteFileAsync(string fileKey, CancellationToken ct = default);
    string GetFileUrl(string fileKey);
}
