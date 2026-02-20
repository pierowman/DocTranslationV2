using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using DocTranslationV2.Models;
using DocTranslationV2.Constants;

namespace DocTranslationV2.Services;

/// <summary>
/// Manages Azure Blob Storage container lifecycle for translation jobs
/// </summary>
public class ContainerManagementService : IContainerManagementService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<ContainerManagementService> _logger;
    private readonly AzureBlobStorageSettings _settings;

    public ContainerManagementService(
        IOptions<TranslationConfiguration> config,
        ICredentialService credentialService,
        ILogger<ContainerManagementService> logger)
    {
        _settings = config.Value.AzureBlobStorage;
        _logger = logger;

        var blobUri = new Uri($"https://{_settings.AccountName}.blob.core.windows.net");
        _blobServiceClient = new BlobServiceClient(blobUri, credentialService.GetBlobStorageCredential());
    }

    public async Task<string> CreateJobContainerAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 10;
        const int baseDelayMs = 2000;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully created/verified container {ContainerName}", containerName);
                return containerName;
            }
            catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "ContainerBeingDeleted")
            {
                if (attempt < maxRetries - 1)
                {
                    var delay = baseDelayMs * (attempt + 1);
                    _logger.LogWarning(
                        "Container {ContainerName} is being deleted, waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                        containerName, delay, attempt + 1, maxRetries);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    _logger.LogError("Container {ContainerName} still being deleted after {MaxRetries} retries",
                        containerName, maxRetries);
                    throw;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 409 && attempt < maxRetries - 1)
            {
                var delay = baseDelayMs * (attempt + 1);
                _logger.LogWarning("Conflict creating container {ContainerName}: {ErrorCode}, retrying in {Delay}ms",
                    containerName, ex.ErrorCode, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Failed to create container {containerName} after {maxRetries} attempts");
    }

    public Uri GetContainerUri(string containerName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        return containerClient.Uri;
    }

    public async Task<bool> ContainerExistsAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var response = await containerClient.ExistsAsync(cancellationToken);
            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if container {ContainerName} exists", containerName);
            return false;
        }
    }

    public async Task DeleteContainerAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting container {ContainerName}", containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            var exists = await containerClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogInformation("Container {ContainerName} does not exist, nothing to delete", containerName);
                return;
            }

            await containerClient.DeleteAsync(cancellationToken: cancellationToken);
            await WaitForContainerDeletionAsync(containerClient, cancellationToken);

            _logger.LogInformation("Successfully deleted container {ContainerName}", containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting container {ContainerName}", containerName);
            throw;
        }
    }

    public async Task CleanupJobContainersAsync(
        string jobId,
        List<string> targetLanguages,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cleaning up containers for job {JobId}", jobId);

        var containersToDelete = new List<string>
        {
            ContainerNamePatterns.GetSourceContainerName(jobId),
            ContainerNamePatterns.GetMetadataContainerName(jobId)
        };

        foreach (var language in targetLanguages)
        {
            containersToDelete.Add(ContainerNamePatterns.GetTargetContainerName(jobId, language));
        }

        var deletionTasks = containersToDelete.Select(container =>
            DeleteContainerAsync(container, cancellationToken));

        await Task.WhenAll(deletionTasks);

        _logger.LogInformation("Completed cleanup of {Count} containers for job {JobId}",
            containersToDelete.Count, jobId);
    }

    public async Task<List<string>> ListContainerFilesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing files in container {ContainerName}", containerName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            var exists = await containerClient.ExistsAsync(cancellationToken);
            if (!exists.Value)
            {
                _logger.LogWarning("Container {ContainerName} does not exist", containerName);
                return new List<string>();
            }

            var files = new List<string>();
            await foreach (var blob in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                files.Add(blob.Name);
            }

            _logger.LogInformation("Found {Count} files in container {ContainerName}", files.Count, containerName);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files in container {ContainerName}", containerName);
            throw;
        }
    }

    public async Task CleanupExistingContainersIfNeededAsync(
        string sourceContainerName,
        string targetContainerName,
        CancellationToken cancellationToken = default)
    {
        var sourceClient = _blobServiceClient.GetBlobContainerClient(sourceContainerName);
        var targetClient = _blobServiceClient.GetBlobContainerClient(targetContainerName);

        var sourceExists = await sourceClient.ExistsAsync(cancellationToken);
        var targetExists = await targetClient.ExistsAsync(cancellationToken);

        if (sourceExists.Value || targetExists.Value)
        {
            _logger.LogWarning(
                "UNEXPECTED: Containers already exist! Source: {Source} ({SourceExists}), Target: {Target} ({TargetExists})",
                sourceContainerName, sourceExists.Value, targetContainerName, targetExists.Value);

            if (sourceExists.Value)
            {
                _logger.LogInformation("Deleting existing source container {Container}", sourceContainerName);
                await sourceClient.DeleteAsync(cancellationToken: cancellationToken);
                await WaitForContainerDeletionAsync(sourceClient, cancellationToken);
            }

            if (targetExists.Value)
            {
                _logger.LogInformation("Deleting existing target container {Container}", targetContainerName);
                await targetClient.DeleteAsync(cancellationToken: cancellationToken);
                await WaitForContainerDeletionAsync(targetClient, cancellationToken);
            }

            _logger.LogInformation("Cleanup complete, ready for fresh upload");
        }
        else
        {
            _logger.LogInformation("No existing containers found - starting fresh (as expected)");
        }
    }

    private async Task WaitForContainerDeletionAsync(
        BlobContainerClient containerClient,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 30;
        const int delayMs = 1000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var exists = await containerClient.ExistsAsync(cancellationToken);
                if (!exists.Value)
                {
                    _logger.LogInformation("Container {ContainerName} deletion confirmed after {Seconds} seconds",
                        containerClient.Name, i + 1);
                    return;
                }
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogInformation("Container {ContainerName} deletion confirmed (404) after {Seconds} seconds",
                    containerClient.Name, i + 1);
                return;
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        _logger.LogWarning("Container {ContainerName} still exists after {Seconds} seconds, proceeding anyway",
            containerClient.Name, maxRetries);
    }
}
