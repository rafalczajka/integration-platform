using System.IO;
using System.Runtime.Serialization;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Integrations.GoogleDrive.Options;

namespace Integrations.GoogleDrive.Drive;

internal sealed class DriveClient : IDisposable
{
    private readonly DriveService _driveService;
    private readonly SemaphoreSlim _semaphore;

    private DriveClient(DriveService driveService, int concurrentDownloads)
    {
        _driveService = driveService;
        _semaphore = new SemaphoreSlim(concurrentDownloads);
    }

    public static async Task<DriveClient> CreateAsync(
        GoogleDriveOptions options,
        CancellationToken cancellationToken)
    {
        var (clientId, clientSecret, refreshToken) =
            JsonSerializer.Deserialize<DriveCredentials>(options.JsonCredentials)
            ?? throw new SerializationException("Could not deserialize google credentials");

        var secrets = new ClientSecrets
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
        };

        var token = new TokenResponse
        {
            RefreshToken = refreshToken,
        };

        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = [DriveService.Scope.Drive]
        });

        var credential = new UserCredential(flow, "user", token);
        await credential.RefreshTokenAsync(cancellationToken);

        var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = options.ApplicationName
        });

        return new DriveClient(drive, options.ConcurrentDownloads);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _driveService.Dispose();
    }

    public async Task<IEnumerable<FileInfo>> ListFilesInFolderAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        return await ListFilesInFolderAsync(folderId, "", cancellationToken);
    }

    public async Task<IEnumerable<File>> DownloadFilesAsync(
        IEnumerable<FileInfo> files,
        CancellationToken cancellationToken)
    {
        var tasks = files.Select(async file =>
        {
            await _semaphore.WaitAsync(cancellationToken);

            try
            {
                await using var stream = new MemoryStream();
                var getRequest = _driveService.Files.Get(file.Id);
                await getRequest.DownloadAsync(stream, cancellationToken);
                var bytes = stream.ToArray();
                return new File(file.Path, bytes);
            }
            finally
            {
                _semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        return tasks.Select(t => t.Result);
    }

    public async Task UploadFileAsync(
        string fileName,
        string destinationFolderId,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = [destinationFolderId]
        };

        await using var stream = new ReadOnlyMemoryStream(content);

        var request = _driveService.Files.Create(metadata, stream, contentType);
        await request.UploadAsync(cancellationToken);
    }

    private async Task<IEnumerable<FileInfo>> ListFilesInFolderAsync(
        string folderId,
        string pathPrefix,
        CancellationToken cancellationToken)
    {
        string? nextPageToken = null;
        var files = new List<FileInfo>();

        do
        {
            var request = _driveService.Files.List();

            request.Q = $"'{folderId}' in parents and trashed = false";
            request.Spaces = "drive";
            request.Fields = "nextPageToken, files(id, name, mimeType)";
            request.PageToken = nextPageToken;

            var result = await request.ExecuteAsync(cancellationToken);

            foreach (var file in result.Files)
            {
                var fileId = file.Id;
                var filePath = pathPrefix + file.Name;
                var isFolderType = file.MimeType == "application/vnd.google-apps.folder";

                if (isFolderType)
                {
                    files.AddRange(await ListFilesInFolderAsync(file.Id, $"{filePath}/", cancellationToken));
                    continue;
                }

                files.Add(new FileInfo(fileId, filePath));
            }

            nextPageToken = result.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(nextPageToken));

        return files;
    }
}

internal sealed record DriveCredentials(string ClientId, string ClientSecret, string RefreshToken);
