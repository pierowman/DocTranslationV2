using System.Collections.Concurrent;
using DocTranslationV2.Models;
using DocTranslationV2.Constants;
using DocTranslationV2.Services;

/// <summary>
/// Manages translation status tracking, caching, and progress calculation
/// </summary>
public class StatusTrackingService : IStatusTrackingService
{
    private readonly ConcurrentDictionary<string, (JobStatus Status, DateTime CachedAt)> _terminalStatusCache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
    private readonly IJobManagementService _jobManagement;
    private readonly ITranslationOperationService _translationOps;
    private readonly ILogger<StatusTrackingService> _logger;
    private readonly AzureBlobStorageSettings _blobSettings;

    public StatusTrackingService(
        IJobManagementService jobManagement,
        ITranslationOperationService translationOps,
        ILogger<StatusTrackingService> logger,
        Microsoft.Extensions.Options.IOptions<TranslationConfiguration> config)
    {
        _jobManagement = jobManagement;
        _translationOps = translationOps;
        _logger = logger;
        _blobSettings = config.Value.AzureBlobStorage;
    }

    public async Task<JobStatus> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cachedStatus = GetCachedTerminalStatus(jobId);
        if (cachedStatus != null)
        {
            _logger.LogInformation("Returning cached status for terminal job {JobId}: {Status}",
                jobId, cachedStatus.Status);
            return cachedStatus;
        }

        _logger.LogInformation("Checking status for translation job {JobId}", jobId);

        // Get job metadata
        var metadata = _jobManagement.GetJobMetadata(jobId);
        if (metadata == null)
        {
            _logger.LogWarning("No metadata found for job {JobId}", jobId);
            return new JobStatus
            {
                JobId = jobId,
                Status = TranslationStatus.NotFound,
                ErrorMessage = $"Job metadata not found for: {jobId}",
                DetailedStatus = "Job information not found. The job may not have started yet or was never created.",
                CurrentPhase = JobPhases.NotFound
            };
        }

        var operationIds = metadata.AllOperationIds;
        if (!operationIds.Any())
        {
            _logger.LogWarning("No operation IDs found for job {JobId}", jobId);
            return new JobStatus
            {
                JobId = jobId,
                Status = TranslationStatus.NotFound,
                ErrorMessage = $"No operations found for job: {jobId}",
                DetailedStatus = "Operations not found in Azure Translation Service.",
                CurrentPhase = JobPhases.NotFound
            };
        }

        // Collect operation statuses
        var operationStatuses = new List<TranslationStatusResult>();
        await foreach (var status in _translationOps.GetAllOperationsAsync(cancellationToken))
        {
            if (operationIds.Contains(status.Id))
            {
                operationStatuses.Add(status);
                _logger.LogInformation("Found status for operation {OperationId}: {Status}",
                    status.Id, status.Status);
            }

            if (operationStatuses.Count == operationIds.Count)
                break;
        }

        if (!operationStatuses.Any())
        {
            _logger.LogWarning("Translation operations not found in Azure for job {JobId}", jobId);
            return new JobStatus
            {
                JobId = jobId,
                Status = TranslationStatus.NotFound,
                ErrorMessage = $"Translation operations not found: {jobId}",
                DetailedStatus = "Operations not found in Azure Translation Service. They may have been deleted or never existed.",
                CurrentPhase = JobPhases.NotFound
            };
        }

        // Aggregate status
        var aggregated = AggregateOperationStatuses(operationStatuses);

        // Determine the actual current phase based on both job metadata and Azure status
        var currentPhase = DetermineActualPhase(metadata, aggregated);

        var jobStatus = new JobStatus
        {
            JobId = jobId,
            Status = aggregated.Status,
            TotalDocuments = aggregated.TotalDocuments,
            TranslatedDocuments = aggregated.TranslatedDocuments,
            FailedDocuments = aggregated.FailedDocuments,
            DocumentsInProgress = aggregated.DocumentsInProgress,
            DocumentsNotStarted = aggregated.DocumentsNotStarted,
            CreatedOn = aggregated.CreatedOn,
            LastModified = aggregated.LastModified,
            ElapsedTime = DateTime.UtcNow - metadata.CreatedAt,
            CurrentPhase = currentPhase,
            TargetLanguages = metadata.TargetLanguages
        };

        jobStatus.PercentComplete = CalculateProgress(jobStatus, metadata.HasImageProcessing);
        jobStatus.DetailedStatus = BuildDetailedStatusMessage(jobStatus);

        // Handle error states
        if (aggregated.Status == TranslationStatus.ValidationFailed || 
            aggregated.Status == TranslationStatus.Failed || 
            jobStatus.FailedDocuments > 0)
        {
            jobStatus.ErrorMessage = await GetErrorDetailsAsync(jobId, cancellationToken);
            
            if (aggregated.Status == TranslationStatus.ValidationFailed || aggregated.Status == TranslationStatus.Failed)
            {
                CacheTerminalStatus(jobId, jobStatus);
            }
        }
        else if (aggregated.Status == TranslationStatus.Cancelled)
        {
            jobStatus.ErrorMessage = "Translation job was cancelled";
            CacheTerminalStatus(jobId, jobStatus);
        }
        else if (aggregated.Status == TranslationStatus.Succeeded)
        {
            if (jobStatus.CurrentPhase == JobPhases.Completed)
            {
                CacheTerminalStatus(jobId, jobStatus);
            }
            else if (jobStatus.CurrentPhase == JobPhases.ReplacingImages || 
                     jobStatus.CurrentPhase == JobPhases.TranslatingDocuments)
            {
                jobStatus.Status = TranslationStatus.Processing;
            }
        }

        return jobStatus;
    }

    public int CalculateProgress(JobStatus jobStatus, bool hasImageProcessing)
    {
        if (!hasImageProcessing)
        {
            if (jobStatus.TotalDocuments > 0)
            {
                return (int)((double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments * 100);
            }
            return 0;
        }

        return jobStatus.CurrentPhase switch
        {
            JobPhases.Initializing => 0,
            JobPhases.UploadingFiles => 5,
            JobPhases.ExtractingImages => 15,
            JobPhases.Starting or JobPhases.StartingTranslation => 22,
            JobPhases.TranslatingDocuments or JobPhases.Translating or JobPhases.Processing =>
                25 + (int)((jobStatus.TotalDocuments > 0
                    ? (double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments
                    : 0) * 60),
            JobPhases.ReplacingImages => 90,
            JobPhases.Completed => 100,
            JobPhases.Failed or JobPhases.Cancelled or JobPhases.ValidationFailed => jobStatus.PercentComplete,
            _ => jobStatus.TotalDocuments > 0
                ? (int)((double)jobStatus.TranslatedDocuments / jobStatus.TotalDocuments * 100)
                : 0
        };
    }

    public string BuildDetailedStatusMessage(JobStatus jobStatus)
    {
        var messages = new List<string>();

        switch (jobStatus.CurrentPhase)
        {
            case JobPhases.Initializing:
                messages.Add("Initializing translation job...");
                break;
            case JobPhases.Starting:
            case JobPhases.StartingTranslation:
                messages.Add("Starting document translation...");
                break;
            case JobPhases.UploadingFiles:
                messages.Add("Uploading files to storage...");
                break;
            case JobPhases.ExtractingImages:
                messages.Add("Extracting images from documents...");
                break;
            case JobPhases.TranslatingDocuments:
            case JobPhases.Translating:
                messages.Add($"Translating documents... ({jobStatus.TranslatedDocuments}/{jobStatus.TotalDocuments} completed)");
                break;
            case JobPhases.Processing:
                messages.Add($"Processing documents... ({jobStatus.TranslatedDocuments}/{jobStatus.TotalDocuments} completed)");
                break;
            case JobPhases.ReplacingImages:
                messages.Add("Replacing translated images in documents...");
                break;
            case JobPhases.Completed:
                messages.Add($"Translation completed successfully! All {jobStatus.TotalDocuments} document(s) translated.");
                break;
            case JobPhases.Failed:
                messages.Add($"Translation failed. {jobStatus.FailedDocuments} document(s) failed.");
                break;
            case JobPhases.Cancelled:
                messages.Add("Translation job was cancelled.");
                break;
            case JobPhases.ValidationFailed:
                messages.Add("Validation failed. Check permissions and configuration.");
                break;
        }

        if (jobStatus.CurrentPhase == JobPhases.Translating || jobStatus.CurrentPhase == JobPhases.Processing)
        {
            if (jobStatus.DocumentsInProgress > 0)
                messages.Add($"   • In Progress: {jobStatus.DocumentsInProgress}");
            if (jobStatus.DocumentsNotStarted > 0)
                messages.Add($"   • Pending: {jobStatus.DocumentsNotStarted}");
            if (jobStatus.FailedDocuments > 0)
                messages.Add($"   • Failed: {jobStatus.FailedDocuments}");
        }

        if (jobStatus.ElapsedTime.HasValue)
        {
            var elapsed = jobStatus.ElapsedTime.Value;
            var timeStr = elapsed.TotalMinutes < 1
                ? $"{elapsed.Seconds} second(s)"
                : $"{(int)elapsed.TotalMinutes} minute(s) {elapsed.Seconds} second(s)";

            messages.Add($"Elapsed time: {timeStr}");
        }

        return string.Join("\n", messages);
    }

    public string DeterminePhase(string azureStatus, JobStatus jobStatus)
    {
        return azureStatus switch
        {
            "NotStarted" => JobPhases.Initializing,
            "Running" when jobStatus.DocumentsNotStarted == jobStatus.TotalDocuments => JobPhases.Starting,
            "Running" when jobStatus.DocumentsInProgress > 0 => JobPhases.Translating,
            "Running" => JobPhases.Processing,
            "Succeeded" => JobPhases.Completed,
            "Failed" => JobPhases.Failed,
            "Cancelled" => JobPhases.Cancelled,
            "ValidationFailed" => JobPhases.ValidationFailed,
            _ => azureStatus
        };
    }

    private string DetermineActualPhase(JobMetadata metadata, AggregatedStatus aggregated)
    {
        // If we're in the initial setup phases (before Azure translation starts), use metadata phase
        var preTranslationPhases = new[] 
        { 
            JobPhases.Initializing,
            JobPhases.UploadingFiles, 
            JobPhases.ExtractingImages,
            JobPhases.StartingTranslation
        };

        if (preTranslationPhases.Contains(metadata.CurrentPhase))
        {
            // Check if Azure has actually started - if so, override the phase
            if (aggregated.Status == TranslationStatus.Running || 
                aggregated.Status == TranslationStatus.Succeeded)
            {
                // Azure has started, use Azure-derived phase
                return DeterminePhaseFromAzureStatus(aggregated);
            }

            // Still in setup, use metadata phase
            return metadata.CurrentPhase;
        }

        // For post-translation phases (image replacement, completed), use metadata phase
        var postTranslationPhases = new[] 
        { 
            JobPhases.ReplacingImages,
            JobPhases.Completed,
            JobPhases.Failed,
            JobPhases.Cancelled
        };

        if (postTranslationPhases.Contains(metadata.CurrentPhase))
        {
            return metadata.CurrentPhase;
        }

        // For translation phase, derive from Azure status.
        // Guard against a race condition where Azure reports Succeeded before the background
        // image-replacement task has had a chance to update the phase to ReplacingImages.
        // Without this guard, DeterminePhaseFromAzureStatus would return Completed and the
        // status would be cached as terminal before image replacement actually runs.
        if (metadata.HasImageProcessing && aggregated.Status == TranslationStatus.Succeeded)
        {
            return JobPhases.ReplacingImages;
        }

        return DeterminePhaseFromAzureStatus(aggregated);
    }

    private string DeterminePhaseFromAzureStatus(AggregatedStatus aggregated)
    {
        // Determine phase based on Azure translation status
        if (aggregated.Status == TranslationStatus.ValidationFailed)
        {
            return JobPhases.ValidationFailed;
        }
        else if (aggregated.Status == TranslationStatus.Failed)
        {
            return JobPhases.Failed;
        }
        else if (aggregated.Status == TranslationStatus.Cancelled)
        {
            return JobPhases.Cancelled;
        }
        else if (aggregated.Status == TranslationStatus.Succeeded)
        {
            return JobPhases.Completed;
        }
        else if (aggregated.Status == TranslationStatus.Running)
        {
            // More granular Running phases
            if (aggregated.DocumentsNotStarted == aggregated.TotalDocuments)
            {
                return JobPhases.Starting;
            }
            else if (aggregated.DocumentsInProgress > 0 || aggregated.TranslatedDocuments > 0)
            {
                return JobPhases.TranslatingDocuments;
            }
            else
            {
                return JobPhases.Processing;
            }
        }
        else if (aggregated.Status == TranslationStatus.NotStarted)
        {
            return JobPhases.Initializing;
        }

        return JobPhases.Processing;
    }

    public void CacheTerminalStatus(string jobId, JobStatus status)
    {
        _terminalStatusCache[jobId] = (status, DateTime.UtcNow);
        _logger.LogInformation("Cached terminal status for job {JobId}: {Status}", jobId, status.Status);
    }

    public JobStatus? GetCachedTerminalStatus(string jobId)
    {
        if (_terminalStatusCache.TryGetValue(jobId, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
            {
                return cached.Status;
            }
            else
            {
                _terminalStatusCache.TryRemove(jobId, out _);
            }
        }
        return null;
    }

    public AggregatedStatus AggregateOperationStatuses(IEnumerable<TranslationStatusResult> statuses)
    {
        var statusList = statuses.ToList();

        var totalDocs = statusList.Sum(s => s.DocumentsTotal);
        var succeededDocs = statusList.Sum(s => s.DocumentsSucceeded);
        var failedDocs = statusList.Sum(s => s.DocumentsFailed);
        var inProgressDocs = statusList.Sum(s => s.DocumentsInProgress);
        var notStartedDocs = statusList.Sum(s => s.DocumentsNotStarted);

        var createdOn = statusList.Min(s => s.CreatedOn);
        var lastModified = statusList.Max(s => s.LastModified);

        var statusStrings = statusList.Select(s => s.Status.ToString()).ToList();

        string overallStatus;
        if (statusStrings.Any(s => s == "ValidationFailed" || s == "Failed"))
        {
            overallStatus = statusStrings.Contains("ValidationFailed") ? TranslationStatus.ValidationFailed : TranslationStatus.Failed;
        }
        else if (statusStrings.All(s => s == "Succeeded"))
        {
            overallStatus = TranslationStatus.Succeeded;
        }
        else if (statusStrings.Any(s => s == "Running"))
        {
            overallStatus = TranslationStatus.Running;
        }
        else if (statusStrings.All(s => s == "NotStarted"))
        {
            overallStatus = TranslationStatus.NotStarted;
        }
        else if (statusStrings.Any(s => s == "Cancelled"))
        {
            overallStatus = TranslationStatus.Cancelled;
        }
        else
        {
            overallStatus = TranslationStatus.Running;
        }

        _logger.LogInformation("Aggregated status across {Count} operations: {Status} ({Succeeded}/{Total} documents)",
            statusList.Count, overallStatus, succeededDocs, totalDocs);

        return new AggregatedStatus
        {
            Status = overallStatus,
            TotalDocuments = totalDocs,
            TranslatedDocuments = succeededDocs,
            FailedDocuments = failedDocs,
            DocumentsInProgress = inProgressDocs,
            DocumentsNotStarted = notStartedDocs,
            CreatedOn = createdOn,
            LastModified = lastModified
        };
    }

    public async Task<string> GetErrorDetailsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Retrieving detailed error information for job {JobId}", jobId);

            var metadata = _jobManagement.GetJobMetadata(jobId);
            if (metadata == null)
            {
                return "Job metadata not found. Cannot retrieve error details.";
            }

            var operationId = metadata.OperationId;
            if (string.IsNullOrEmpty(operationId))
            {
                return "No operation ID found for job. Cannot retrieve error details.";
            }

            var status = await _translationOps.GetOperationStatusAsync(operationId, cancellationToken);

            if (status.Status.ToString() == "ValidationFailed")
            {
                return BuildValidationFailedMessage(status);
            }
            else if (status.DocumentsFailed > 0)
            {
                return BuildDocumentFailedMessage(status);
            }

            return $"Job Status: {status.Status}\n" +
                   $"Total Documents: {status.DocumentsTotal}\n" +
                   $"Succeeded: {status.DocumentsSucceeded}\n" +
                   $"Failed: {status.DocumentsFailed}\n" +
                   $"In Progress: {status.DocumentsInProgress}\n" +
                   $"Not Started: {status.DocumentsNotStarted}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document error details for job {JobId}", jobId);
            return $"Could not retrieve detailed error information: {ex.Message}";
        }
    }

    private string BuildValidationFailedMessage(TranslationStatusResult status)
    {
        var message = "Validation Failed\n\n";
        message += $"Total Documents: {status.DocumentsTotal}\n";
        message += $"Documents Not Started: {status.DocumentsNotStarted}\n";
        message += $"Failed Documents: {status.DocumentsFailed}\n\n";
        message += "Common causes:\n";
        message += "1. PERMISSION ISSUES - Translation Service needs 'Storage Blob Data Contributor' role\n";
        message += "2. STORAGE ACCOUNT FIREWALL - Check network rules\n";
        message += "3. INCORRECT URIs - Verify container URIs\n";
        message += $"Storage Account: {_blobSettings.AccountName}\n";
        message += $"Created: {status.CreatedOn:yyyy-MM-dd HH:mm:ss} UTC";
        return message;
    }

    private string BuildDocumentFailedMessage(TranslationStatusResult status)
    {
        var message = "Translation Failed\n\n";
        message += $"Total Documents: {status.DocumentsTotal}\n";
        message += $"Succeeded: {status.DocumentsSucceeded}\n";
        message += $"Failed: {status.DocumentsFailed}\n";
        message += $"In Progress: {status.DocumentsInProgress}\n\n";
        message += "Common causes:\n";
        message += "1. UNSUPPORTED DOCUMENT FORMAT\n";
        message += "2. DOCUMENT TOO LARGE (>40 MB)\n";
        message += "3. UNSUPPORTED LANGUAGE PAIR\n";
        message += "4. PROTECTED/ENCRYPTED DOCUMENTS\n";
        message += $"Last Modified: {status.LastModified:yyyy-MM-dd HH:mm:ss} UTC";
        return message;
    }
}
