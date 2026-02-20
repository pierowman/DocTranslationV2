# Container-Based Translation Fix - Matching Working Code Pattern

## Problem
The batch translation was failing validation because your implementation used folder paths within a single container, while Azure Translation Service expects container-level URIs.

## Key Differences Between Working Code and Your Code

### 1. **Container Strategy**
**Working Code:**
- Creates **separate containers per job**: `{requestId}-source` and `{requestId}-target`
- Each translation job gets its own isolated containers
- Containers are created with `PublicAccessType.None`

**Your Old Code:**
- Used a **single shared container** with folder paths: `jobs/{jobId}/source`
- Tried to pass folder-level URIs to Azure Translation Service

**Fix Applied:**
- Changed to create separate containers per job: `job-{jobId}-source` and `job-{jobId}-target`
- Containers are created explicitly with `CreateIfNotExistsAsync()`

### 2. **URI Construction**
**Working Code:**
```csharp
// Get container client URIs directly
var srcUri = sourceContainerClient.Uri;
var tgtUri = targetContainerClient.Uri;

// Pass container URIs (no paths)
var operationRequest = new DocumentTranslationInput(srcUri, tgtUri, targetLang);
```

**Your Old Code:**
```csharp
// Manually constructed URIs with folder paths
var sourceUri = new Uri($"{containerUriString}/{sourceFolderPath}");
var targetUri = new Uri($"{containerUriString}/{targetFolder}");
```

**Fix Applied:**
```csharp
// Get container URIs directly from container clients
var sourceUri = sourceContainerClient.Uri;
var targetUri = targetContainerClient.Uri;

// Pass container-level URIs (no folder paths)
var input = new DocumentTranslationInput(sourceUri, targetUri, targetLang);
```

### 3. **File Upload Pattern**
**Working Code:**
- Uploads files directly to container root
- Uses `BlobClient srcBlobClient = sourceContainerClient.GetBlobClient(fileName)`
- Simple, direct upload: `await srcBlobClient.UploadAsync(uploadFileStream, true)`

**Your Old Code:**
- Uploaded to folder paths within a single container
- Used `_blobStorageService.UploadFileAsync(stream, fileName, folderPath)`

**Fix Applied:**
- Added new method `UploadFileToContainerAsync` to `BlobStorageService`
- Uploads files directly to container root (no folder paths)
- Matches working code pattern exactly

### 4. **Translation Operation Invocation**
**Working Code:**
```csharp
// Single operation per job, one language
var operationRequest = new DocumentTranslationInput(srcUri, tgtUri, TargetLanguage);
var client = new DocumentTranslationClient(...);
DocumentTranslationOperation operationResult = await client.StartTranslationAsync(operationRequest);
await operationResult.WaitForCompletionAsync();
```

**Your Old Code:**
```csharp
// Created list of inputs, but only used first one
var inputs = new List<DocumentTranslationInput>();
// ... add inputs for each language
var operation = await _batchClient.StartTranslationAsync(inputs.FirstOrDefault(), cancellationToken);
// Didn't wait for completion, returned immediately
```

**Fix Applied:**
```csharp
// Single input, single language (like working code)
var input = new DocumentTranslationInput(sourceUri, targetUri, targetLang);
var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);

// Wait for completion (matching working code)
await operation.WaitForCompletionAsync(cancellationToken);
```

## Changes Made

### 1. DocumentTranslationService.cs
- **Modified `ProcessBatchTranslationAsync`**: Changed to use separate containers instead of folder paths
- **Added `ProcessAndUploadFilesForBatchAsync`**: New method to upload files to specific containers
- **Added `ProcessSingleFileForBatchDirectAsync`**: Uploads files directly to container root
- **Rewrote `StartBatchTranslationAsync`**: Completely refactored to match working code pattern:
  - Creates separate source and target containers
  - Gets container URIs directly from container clients
  - Passes container-level URIs (no folder paths)
  - Waits for operation completion before returning

### 2. BlobStorageService.cs
- **Added `UploadFileToContainerAsync`**: New method to upload files to a specific container
  - Creates container if it doesn't exist
  - Uploads file to container root (no folder path)
  - Returns blob URI

### 3. IServices.cs
- **Added interface method**: `Task<string> UploadFileToContainerAsync(...)`

## Why This Fixes the Problem

### Azure Translation Service Expectations
Azure Translation Service expects:
1. **Container-level URIs** - not folder paths within a container
2. **Direct container access** - it needs to enumerate all blobs in the source container
3. **Clean separation** - source and target should be different containers

### What Was Wrong
Your old code:
- Passed URIs like `https://storage.blob.core.windows.net/container/jobs/123/source`
- Azure Translation Service couldn't properly enumerate files in a "folder"
- The managed identity permissions might have been correct, but the URI structure was wrong

### What's Right Now
The new code:
- Passes URIs like `https://storage.blob.core.windows.net/job-123-source`
- Azure Translation Service can enumerate the entire container
- Files are at the container root, easy to access
- Matches the exact pattern of the working code

## Testing
1. Start a new translation job
2. Check Azure Storage - you should see new containers: `job-{guid}-source` and `job-{guid}-target`
3. Files should be uploaded to the container root (not in folders)
4. Translation should succeed if managed identity permissions are correct

## Note on Multiple Languages
The working code only translates to one language at a time. If you need multiple target languages, you'll need to:
1. Start separate translation operations for each language
2. Create separate target containers for each language: `job-{guid}-target-{lang}`
3. Or modify the working code pattern to support multiple languages in a single operation

## Cleanup Strategy
Since each job creates new containers, consider implementing cleanup:
- Delete containers after successful download
- Or implement a scheduled cleanup job to remove old containers
- The working code deletes blobs but comments out container deletion because multiple translations might be happening

## Migration Path
If you have existing jobs using the old folder-based approach:
- They will continue to fail validation
- New jobs will use the container-based approach and should work
- You may need to re-submit failed jobs

## Related Files
- `DocTranslationV2\Services\DocumentTranslationService.cs`
- `DocTranslationV2\Services\BlobStorageService.cs`
- `DocTranslationV2\Services\IServices.cs`
