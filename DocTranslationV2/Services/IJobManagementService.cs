using DocTranslationV2.Models;

namespace DocTranslationV2.Services;

/// <summary>
/// Manages translation job metadata, lifecycle, and phase tracking
/// </summary>
public interface IJobManagementService
{
    /// <summary>
    /// Creates a new translation job with initial metadata
    /// </summary>
    string CreateJob(TranslationJobRequest request);
    
    /// <summary>
    /// Gets job metadata by ID
    /// </summary>
    JobMetadata? GetJobMetadata(string jobId);
    
    /// <summary>
    /// Updates the current phase of a job
    /// </summary>
    void UpdateJobPhase(string jobId, string phase);
    
    /// <summary>
    /// Registers an operation ID with a job
    /// </summary>
    void RegisterOperation(string jobId, string operationId, string languageCode, string targetContainer);
    
    /// <summary>
    /// Gets all operation IDs for a job
    /// </summary>
    List<string> GetOperationIds(string jobId);
    
    /// <summary>
    /// Gets the target container for a specific language
    /// </summary>
    string? GetTargetContainer(string jobId, string languageCode);
    
    /// <summary>
    /// Marks a job as completed or failed
    /// </summary>
    void CompleteJob(string jobId, bool success, string? errorMessage = null);
    
    /// <summary>
    /// Cleans up job metadata (after files are downloaded/deleted)
    /// </summary>
    void CleanupJobMetadata(string jobId);
}

public class TranslationJobRequest
{
    public List<IFormFile> Files { get; set; } = new();
    public List<string> TargetLanguages { get; set; } = new();
    public string? SourceLanguage { get; set; }
    public bool ProcessImages { get; set; }
    public bool AutoDetectLanguage { get; set; }
    public ImageFilteringOptions? ImageFiltering { get; set; }
}

public class JobMetadata
{
    public string JobId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty; // Primary operation
    public List<string> AllOperationIds { get; set; } = new();
    public bool HasImageProcessing { get; set; }
    public string SourceContainerName { get; set; } = string.Empty;
    public Dictionary<string, string> TargetContainersByLanguage { get; set; } = new();
    public Dictionary<string, string> OperationIdToLanguage { get; set; } = new();
    public List<IFormFile> OriginalFiles { get; set; } = new();
    public List<string> TargetLanguages { get; set; } = new();
    public string CurrentPhase { get; set; } = "Initializing";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastPhaseUpdate { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public bool IsCompleted { get; set; }
}
