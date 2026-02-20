using System.Collections.Concurrent;
using DocTranslationV2.Constants;

namespace DocTranslationV2.Services;

/// <summary>
/// Implementation of job metadata management with thread-safe operations
/// </summary>
public class JobManagementService : IJobManagementService
{
    private readonly ConcurrentDictionary<string, JobMetadata> _jobMetadata = new();
    private readonly ILogger<JobManagementService> _logger;

    public JobManagementService(ILogger<JobManagementService> logger)
    {
        _logger = logger;
    }

    public string CreateJob(TranslationJobRequest request)
    {
        var jobId = Guid.NewGuid().ToString();
        
        var metadata = new JobMetadata
        {
            JobId = jobId,
            CurrentPhase = request.ProcessImages ? JobPhases.UploadingFiles : JobPhases.Initializing,
            SourceContainerName = ContainerNamePatterns.GetSourceContainerName(jobId),
            OriginalFiles = request.Files,
            TargetLanguages = request.TargetLanguages,
            HasImageProcessing = request.ProcessImages,
            CreatedAt = DateTime.UtcNow
        };

        if (!_jobMetadata.TryAdd(jobId, metadata))
        {
            _logger.LogError("Failed to create job {JobId} - ID collision", jobId);
            throw new InvalidOperationException($"Job ID collision: {jobId}");
        }

        _logger.LogInformation("Created job {JobId} with {FileCount} files targeting {LanguageCount} languages",
            jobId, request.Files.Count, request.TargetLanguages.Count);

        return jobId;
    }

    public JobMetadata? GetJobMetadata(string jobId)
    {
        return _jobMetadata.TryGetValue(jobId, out var metadata) ? metadata : null;
    }

    public void UpdateJobPhase(string jobId, string phase)
    {
        _jobMetadata.AddOrUpdate(
            jobId,
            // Shouldn't happen, but provides safety
            key => new JobMetadata
            {
                JobId = jobId,
                CurrentPhase = phase,
                LastPhaseUpdate = DateTime.UtcNow
            },
            // Update existing
            (key, existing) =>
            {
                existing.CurrentPhase = phase;
                existing.LastPhaseUpdate = DateTime.UtcNow;
                return existing;
            });

        _logger.LogInformation("Job {JobId} phase updated to: {Phase}", jobId, phase);
    }

    public void RegisterOperation(string jobId, string operationId, string languageCode, string targetContainer)
    {
        _jobMetadata.AddOrUpdate(
            jobId,
            // Create new if doesn't exist
            key => new JobMetadata
            {
                JobId = jobId,
                OperationId = operationId,
                AllOperationIds = new List<string> { operationId },
                TargetContainersByLanguage = new Dictionary<string, string> { { languageCode, targetContainer } },
                OperationIdToLanguage = new Dictionary<string, string> { { operationId, languageCode } }
            },
            // Update existing
            (key, existing) =>
            {
                // Add to all operations
                existing.AllOperationIds.Add(operationId);
                
                // Set primary if not set
                if (string.IsNullOrEmpty(existing.OperationId))
                {
                    existing.OperationId = operationId;
                }
                
                // Map language and container
                existing.TargetContainersByLanguage[languageCode] = targetContainer;
                existing.OperationIdToLanguage[operationId] = languageCode;
                
                return existing;
            });

        _logger.LogInformation("Registered operation {OperationId} for job {JobId}, language {Language}",
            operationId, jobId, languageCode);
    }

    public List<string> GetOperationIds(string jobId)
    {
        if (_jobMetadata.TryGetValue(jobId, out var metadata))
        {
            return new List<string>(metadata.AllOperationIds);
        }
        
        _logger.LogWarning("No metadata found for job {JobId}", jobId);
        return new List<string>();
    }

    public string? GetTargetContainer(string jobId, string languageCode)
    {
        if (_jobMetadata.TryGetValue(jobId, out var metadata) &&
            metadata.TargetContainersByLanguage.TryGetValue(languageCode, out var container))
        {
            return container;
        }
        
        return null;
    }

    public void CompleteJob(string jobId, bool success, string? errorMessage = null)
    {
        _jobMetadata.AddOrUpdate(
            jobId,
            // Shouldn't happen
            key => new JobMetadata
            {
                JobId = jobId,
                IsCompleted = true,
                CurrentPhase = success ? JobPhases.Completed : JobPhases.Failed,
                ErrorMessage = errorMessage
            },
            // Update existing
            (key, existing) =>
            {
                existing.IsCompleted = true;
                existing.CurrentPhase = success ? JobPhases.Completed : JobPhases.Failed;
                existing.ErrorMessage = errorMessage;
                existing.LastPhaseUpdate = DateTime.UtcNow;
                return existing;
            });

        _logger.LogInformation("Job {JobId} completed with status: {Status}", jobId, success ? "Success" : "Failed");
    }

    public void CleanupJobMetadata(string jobId)
    {
        if (_jobMetadata.TryRemove(jobId, out var metadata))
        {
            _logger.LogInformation("Cleaned up metadata for job {JobId}", jobId);
        }
        else
        {
            _logger.LogWarning("No metadata found to cleanup for job {JobId}", jobId);
        }
    }
}
