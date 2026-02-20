# JobId vs OperationId Cache Key Fix

## Problem Identified

The translation status polling was failing with "Job not found" errors because of a **cache key mismatch**:

### What Was Happening:
```
1. User starts translation ? JobId generated: "11cf7e76-8f91-42bc-9c56-f5e0f6955a63"
2. Azure operation created ? OperationId returned: "bf9486da-7b93-45fd-a235-52d58a15a47b"
3. Operation cached with KEY = operationId ?
4. Client polls with jobId: "11cf7e76-8f91-42bc-9c56-f5e0f6955a63"
5. Code searches cache with KEY = jobId ? NOT FOUND ?
6. Code searches Azure operations for jobId ? NOT FOUND ?
   (Azure only knows about operationId)
```

### Why This Failed:
- **Cache stored by operationId** but **searched by jobId**
- **Client only knows about jobId** (returned in response)
- **Azure only knows about operationId** (its internal ID)
- **No mapping between the two!**

## Root Cause

In the old code:

```csharp
// StartBatchTranslationAsync returned the operationId as a string
var operationId = await StartBatchTranslationAsync(...);

// Response used jobId
response.JobId = jobId;

// But cache used operationId! ?
lock (_operationsLock)
{
    _activeOperations[operation.Id] = operation; // Using operationId as key!
}
```

When `GetTranslationStatusAsync(jobId)` was called:
```csharp
// Searched cache with jobId - NOT FOUND
lock (_operationsLock)
{
    _activeOperations.TryGetValue(jobId, out operation); // jobId != operationId!
}

// Then searched Azure by iterating all operations
await foreach (var status in _batchClient.GetTranslationStatusesAsync())
{
    if (status.Id == jobId) // Comparing Azure's operationId to our jobId - NO MATCH!
    {
        // Never reached
    }
}
```

## The Fix

### 1. Changed Cache Key to JobId ?

**Before:**
```csharp
private async Task<string> StartBatchTranslationAsync(...)
{
    var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
    
    // Cached with operationId ?
    lock (_operationsLock)
    {
        _activeOperations[operation.Id] = operation;
    }
    
    return operation.Id; // Returned operationId as string
}
```

**After:**
```csharp
private async Task<DocumentTranslationOperation> StartBatchTranslationAsync(...)
{
    var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
    
    // Don't cache here - return the operation object
    return operation; // Return entire operation object
}

private async Task ProcessBatchTranslationAsync(...)
{
    var operation = await StartBatchTranslationAsync(...);
    
    // Cache with jobId as the key! ?
    lock (_operationsLock)
    {
        _activeOperations[jobId] = operation;
    }
    
    response.JobId = jobId; // Return jobId to client
}
```

### 2. Updated Status Check to Use Cache ?

**Before:**
```csharp
public async Task<JobStatus> GetTranslationStatusAsync(string jobId, ...)
{
    // Searched all operations in Azure (didn't use cache effectively)
    await foreach (var status in _batchClient.GetTranslationStatusesAsync())
    {
        if (status.Id == jobId) // This never matched!
        {
            // ...
        }
    }
}
```

**After:**
```csharp
public async Task<JobStatus> GetTranslationStatusAsync(string jobId, ...)
{
    // First, check our cache (now keyed by jobId) ?
    DocumentTranslationOperation? cachedOperation = null;
    lock (_operationsLock)
    {
        _activeOperations.TryGetValue(jobId, out cachedOperation);
    }
    
    if (cachedOperation != null)
    {
        // Found it! Update status from Azure
        await cachedOperation.UpdateStatusAsync(cancellationToken);
        
        // Use the operation to get status, documents, etc.
        var jobStatus = new JobStatus
        {
            JobId = jobId,
            Status = cachedOperation.Status.ToString(),
            TotalDocuments = cachedOperation.DocumentsTotal,
            // ...
        };
        
        return jobStatus;
    }
    
    // If not in cache (e.g., service restarted), return NotFound
    return new JobStatus
    {
        JobId = jobId,
        Status = "NotFound",
        ErrorMessage = "Job not found (may have been lost due to service restart)"
    };
}
```

### 3. Fixed Method Signature

Changed `StartBatchTranslationAsync` to return the full operation object instead of just the ID string:

```csharp
// Before ?
private async Task<string> StartBatchTranslationAsync(...)

// After ?
private async Task<DocumentTranslationOperation> StartBatchTranslationAsync(...)
```

This allows the caller to cache the operation with the correct key (jobId).

## How It Works Now

### Translation Start Flow:
```
1. Client requests translation
2. JobId generated: "11cf7e76-..."
3. Files uploaded to containers: "job-11cf7e76-...-source" and "job-11cf7e76-...-target"
4. Azure operation started, returns operation: { Id: "bf9486da-..." }
5. Operation cached: _activeOperations["11cf7e76-..."] = operation ?
6. Response sent to client: { JobId: "11cf7e76-...", Status: "InProgress" }
```

### Status Polling Flow:
```
1. Client polls: GET /api/translation/status?jobId=11cf7e76-...
2. Server checks cache: _activeOperations["11cf7e76-..."] ? FOUND! ?
3. Server updates operation status from Azure: operation.UpdateStatusAsync()
4. Server returns current status with all details
```

### Container Naming Consistency:
```
JobId: 11cf7e76-8f91-42bc-9c56-f5e0f6955a63

Containers created:
  - job-11cf7e76-8f91-42bc-9c56-f5e0f6955a63-source
  - job-11cf7e76-8f91-42bc-9c56-f5e0f6955a63-target

Cache key: 11cf7e76-8f91-42bc-9c56-f5e0f6955a63
Response JobId: 11cf7e76-8f91-42bc-9c56-f5e0f6955a63

Everything matches! ?
```

## Benefits of This Approach

? **Consistent ID throughout** - JobId used everywhere client-facing  
? **Fast status checks** - Direct cache lookup instead of iterating all operations  
? **Correct operation tracking** - Cache key matches what client knows about  
? **Container name matches** - JobId in container names matches JobId in API  
? **Proper encapsulation** - OperationId is internal, JobId is external  

## Trade-offs

### Service Restart Scenario
If the service restarts while a translation is in progress:
- The `_activeOperations` cache is lost (it's in-memory)
- Client will get "NotFound" status when polling
- The translation continues in Azure, but we can't track it without the operationId

**Potential Solutions:**
1. **Store jobId-to-operationId mapping in database** (recommended for production)
2. **Use distributed cache** (Redis) instead of in-memory dictionary
3. **Accept the limitation** - users can check Azure Portal for operation status

### Why Not Search Azure by OperationId?
You might wonder: "Why not search Azure operations by iterating all of them?"

**Answer:** We could, but:
- **Inefficient** - Iterating all operations is slow
- **Still doesn't help** - Azure doesn't know about our jobId, only its operationId
- **Cache is better** - Direct lookup is O(1) instead of O(n)

The fundamental issue is that **Azure's operationId and our jobId are different**, and there's no built-in way to query Azure by our custom jobId.

## Testing

### Test Case 1: Normal Flow ?
```
1. Start translation ? Returns jobId
2. Poll with jobId ? Returns "InProgress"
3. Poll again ? Returns "InProgress" or "Succeeded"
4. When succeeded ? Returns translated files
```

### Test Case 2: Service Restart ???
```
1. Start translation ? Returns jobId
2. Restart service (simulated)
3. Poll with jobId ? Returns "NotFound" with explanation
   (In production, would query database for operationId)
```

### Test Case 3: Multiple Concurrent Jobs ?
```
1. Start job A ? jobId A
2. Start job B ? jobId B
3. Poll job A ? Correct status for job A
4. Poll job B ? Correct status for job B
5. No mixing or confusion
```

## Related Documents
- `CRITICAL_JOBID_OPERATIONID_MISMATCH_FIX.md` - Original diagnosis
- `CONTAINER_BASED_TRANSLATION_FIX.md` - Why we use separate containers per job
- `MULTIPLE_LANGUAGES_POLLING_FIX.md` - Multi-language support with polling
- `STATUS_CHECK_NOT_FOUND_EXPLANATION.md` - Why old jobs aren't found

## Code Changes Summary

| File | Method | Change |
|------|--------|--------|
| DocumentTranslationService.cs | `ProcessBatchTranslationAsync()` | Cache operation with jobId as key |
| DocumentTranslationService.cs | `StartBatchTranslationAsync()` | Return `DocumentTranslationOperation` instead of `string` |
| DocumentTranslationService.cs | `GetTranslationStatusAsync()` | Check cache first using jobId |
| DocumentTranslationService.cs | `PopulateTranslatedFilesAsync()` | Accept operation parameter directly |
| DocumentTranslationService.cs | `GetDocumentErrorDetailsFromOperationAsync()` | New helper method for error extraction |
