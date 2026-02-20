namespace DocTranslationV2.Services;

/// <summary>
/// Manages Azure Blob Storage container lifecycle for translation jobs
/// </summary>
public interface IContainerManagementService
{
    /// <summary>
    /// Creates a new container for a translation job with retry logic
    /// </summary>
    Task<string> CreateJobContainerAsync(
        string containerName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the URI for a container (for Azure Translation Service)
    /// </summary>
    Uri GetContainerUri(string containerName);
    
    /// <summary>
    /// Checks if a container exists
    /// </summary>
    Task<bool> ContainerExistsAsync(
        string containerName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a container and waits for deletion to complete
    /// </summary>
    Task DeleteContainerAsync(
        string containerName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cleans up all containers associated with a job
    /// </summary>
    Task CleanupJobContainersAsync(
        string jobId,
        List<string> targetLanguages,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all files in a container
    /// </summary>
    Task<List<string>> ListContainerFilesAsync(
        string containerName,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks for and cleans up existing containers if they unexpectedly exist
    /// </summary>
    Task CleanupExistingContainersIfNeededAsync(
        string sourceContainerName,
        string targetContainerName,
        CancellationToken cancellationToken = default);
}
