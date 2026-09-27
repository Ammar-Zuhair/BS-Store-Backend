using BSStore.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace BSStore.Infrastructure.Services;

public class LocalStorageService : IStorageService
{
    private readonly string _uploadRoot;
    private readonly string _baseUrl;

    public LocalStorageService(IConfiguration config)
    {
        var configuredPath = config["Storage:LocalPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            _uploadRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        }
        else
        {
            _uploadRoot = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        }

        if (!Directory.Exists(_uploadRoot))
        {
            Directory.CreateDirectory(_uploadRoot);
        }

        _baseUrl = config["App:BaseUrl"] ?? "http://localhost:5295";
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string folder = "general", CancellationToken ct = default)
    {
        var targetFolder = Path.Combine(_uploadRoot, folder);
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var ext = Path.GetExtension(fileName);
        var uniqueName = $"{Guid.NewGuid():N}{ext}";
        var relativeKey = $"{folder}/{uniqueName}";
        var fullPath = Path.Combine(_uploadRoot, folder, uniqueName);

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, ct);

        return relativeKey;
    }

    public Task<bool> DeleteFileAsync(string fileKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_uploadRoot, fileKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public string GetFileUrl(string fileKey)
    {
        if (string.IsNullOrWhiteSpace(fileKey)) return string.Empty;
        if (fileKey.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            fileKey.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return fileKey;
        }

        return $"{_baseUrl}/uploads/{fileKey.TrimStart('/')}";
    }
}
