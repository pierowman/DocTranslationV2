using DocTranslationV2.Models;

namespace DocTranslationV2.Services;

/// <summary>
/// Manages translation status tracking, caching, and progress calculation
/// </summary>
public interface IStatusTrackingService
{
    /// <summary>
    /// Gets the current status of a translation job
    /// </summary>
    Task<JobStatus> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Calculates overall progress percentage including all phases
    /// </summary>
    int CalculateProgress(JobStatus jobStatus, bool hasImageProcessing);
    
    /// <summary>
    /// Builds a detailed human-readable status message
    /// </summary>
    string BuildDetailedStatusMessage(JobStatus jobStatus);
    
    /// <summary>
    /// Determines the current phase based on Azure operation status
    /// </summary>
    string DeterminePhase(string azureStatus, JobStatus jobStatus);
    
    /// <summary>
    /// Caches terminal status (completed/failed/cancelled) for faster retrieval
    /// </summary>
    void CacheTerminalStatus(string jobId, JobStatus status);
    
    /// <summary>
    /// Gets cached terminal status if available and not expired
    /// </summary>
    JobStatus? GetCachedTerminalStatus(string jobId);
    
    /// <summary>
    /// Aggregates status across multiple operations for multi-language jobs
    /// </summary>
    AggregatedStatus AggregateOperationStatuses(IEnumerable<TranslationStatusResult> statuses);
    
    /// <summary>
    /// Gets detailed error information for a failed job
    /// </summary>
    Task<string> GetErrorDetailsAsync(string jobId, CancellationToken cancellationToken = default);
}

public class AggregatedStatus
{
    public string Status { get; set; } = string.Empty;
    public int TotalDocuments { get; set; }
    public int TranslatedDocuments { get; set; }
    public int FailedDocuments { get; set; }
    public int DocumentsInProgress { get; set; }
    public int DocumentsNotStarted { get; set; }
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset LastModified { get; set; }
}
