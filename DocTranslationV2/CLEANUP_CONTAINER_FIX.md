# Container-Based Cleanup Fix

## Problem
When clicking "Delete Temporary Files" button, the cleanup was trying to delete folders like:
- `jobs/{jobId}/source`
- `jobs/{jobId}/target`

But batch translations actually create **separate containers**:
- `job-{jobId}-source`
- `job-{jobId}-target`

This meant the cleanup button didn't actually delete anything from blob storage!

## Root Cause
The `CleanupJob` controller method was only calling `DeleteFolderAsync()`, which deletes folders within the default container. It had no way to delete entire containers created for batch translations.

## Solution

### 1. Added `DeleteContainerAsync` Method
**File**: `BlobStorageService.cs`

Added a new method to delete entire containers:

```csharp
public async Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default)
{
    try
    {
        _logger.LogInformation("Deleting container {ContainerName} from blob storage", containerName);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        
        // Check if container exists
        var exists = await containerClient.ExistsAsync(cancellationToken);
        if (!exists.Value)
        {
            _logger.LogWarning("Container {ContainerName} does not exist, nothing to delete", containerName);
            return true; // Not an error - container already gone
        }

        // Delete the entire container
        await containerClient.DeleteAsync(cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully deleted container {ContainerName}", containerName);
        return true;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error deleting container {ContainerName}", containerName);
        return false;
    }
}
```

### 2. Updated Interface
**File**: `IServices.cs`

Added the method signature to the `IBlobStorageService` interface:

```csharp
Task<bool> DeleteContainerAsync(string containerName, CancellationToken cancellationToken = default);
```

### 3. Updated CleanupJob Controller
**File**: `TranslationController.cs`

Updated the cleanup logic to:
1. First try deleting job-specific containers (batch translations)
2. Fall back to folder-based deletion (sync translations)

```csharp
[HttpPost]
public async Task<IActionResult> CleanupJob([FromBody] CleanupRequest request)
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
        {
            return BadRequest(new { error = "Job ID is required" });
        }

        _logger.LogInformation("Cleanup request for job {JobId}", request.JobId);

        // For batch translations, we use separate containers per job
        // Container names: job-{jobId}-source and job-{jobId}-target
        var sourceContainerName = $"job-{request.JobId}-source";
        var targetContainerName = $"job-{request.JobId}-target";

        // Try deleting containers first (batch translation)
        var sourceDeleted = await _blobStorageService.DeleteContainerAsync(sourceContainerName);
        var targetDeleted = await _blobStorageService.DeleteContainerAsync(targetContainerName);

        // Also try folder-based cleanup (sync translation fallback)
        if (!sourceDeleted || !targetDeleted)
        {
            _logger.LogInformation("Container deletion failed or not found, trying folder-based cleanup for job {JobId}", request.JobId);
            var sourcePath = $"jobs/{request.JobId}/source";
            var targetPath = $"jobs/{request.JobId}/target";
            sourceDeleted = await _blobStorageService.DeleteFolderAsync(sourcePath);
            targetDeleted = await _blobStorageService.DeleteFolderAsync(targetPath);
        }

        if (sourceDeleted && targetDeleted)
        {
            return Ok(new { message = "Cleanup completed successfully" });
        }
        else
        {
            return StatusCode(500, new { error = "Partial cleanup failure" });
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error cleaning up job {JobId}", request.JobId);
        return StatusCode(500, new { error = ex.Message });
    }
}
```

## How It Works

When you click "Delete Temporary Files":

1. The cleanup tries to delete containers:
   - `job-{jobId}-source`
   - `job-{jobId}-target`

2. If containers don't exist (e.g., for sync translations), it falls back to folder deletion:
   - `jobs/{jobId}/source`
   - `jobs/{jobId}/target`

This ensures cleanup works for both:
- **Batch translations** (container-based)
- **Sync translations** (folder-based)

## Testing
After restarting the application:

1. Upload and translate a document (batch mode)
2. Wait for completion
3. Click "Delete Temporary Files"
4. Check Azure Portal ? Storage Account ? Containers
5. Verify that `job-{jobId}-source` and `job-{jobId}-target` containers are deleted

## Log Messages to Expect

**Successful Container Deletion:**
```
info: Cleanup request for job 4672ef41-0cad-4d95-80c8-1666b9c92599
info: Deleting container job-4672ef41-0cad-4d95-80c8-1666b9c92599-source from blob storage
info: Successfully deleted container job-4672ef41-0cad-4d95-80c8-1666b9c92599-source
info: Deleting container job-4672ef41-0cad-4d95-80c8-1666b9c92599-target from blob storage
info: Successfully deleted container job-4672ef41-0cad-4d95-80c8-1666b9c92599-target
```

**Container Not Found (Already Deleted):**
```
warn: Container job-4672ef41-0cad-4d95-80c8-1666b9c92599-source does not exist, nothing to delete
```
