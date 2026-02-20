# Synchronous vs Asynchronous Translation - Architecture Update

## Overview

The DocumentTranslationService has been refactored to properly separate synchronous and asynchronous translation workflows using the correct Azure SDK clients for each scenario.

## Key Changes

### 1. Two Separate Clients

The service now uses **two different Azure SDK clients**:

#### **DocumentTranslationClient** (`_batchClient`)
- **Purpose**: Batch/folder-based translation
- **Use Case**: Async operations with multiple files
- **How it works**: 
  - Files uploaded to blob storage
  - Translation processes entire folders
  - Returns job ID for status polling
  - Used when `UseAsyncProcessing = true`

#### **SingleDocumentTranslationClient** (`_singleDocClient`)
- **Purpose**: Direct document-to-document translation
- **Use Case**: Sync operations with single file
- **How it works**:
  - Translates directly from stream to stream
  - No blob storage involvement during translation
  - Returns translated document immediately
  - Used when `UseAsyncProcessing = false` and single file

### 2. Routing Logic

```csharp
public async Task<TranslationResponse> TranslateDocumentsAsync(TranslationRequest request, ...)
{
    // Force async for multiple files
    if (request.Files.Count > 1 && !request.UseAsyncProcessing)
    {
        request.UseAsyncProcessing = true;
        response.IsAsync = true;
    }

    if (request.UseAsyncProcessing)
    {
        // Route to BATCH translation
        await ProcessBatchTranslationAsync(request, jobId, response, cancellationToken);
    }
    else
    {
        // Route to SYNCHRONOUS translation
        await ProcessSynchronousTranslationAsync(request, jobId, response, cancellationToken);
    }
}
```

## Workflow Comparison

### Asynchronous (Batch) Translation

```
User Request
    |
    v
ProcessBatchTranslationAsync()
    |
    v
ProcessAndUploadFilesAsync()
    |---- Upload file 1 to blob storage
    |---- Upload file 2 to blob storage (parallel)
    |---- Upload file N to blob storage (up to 4 concurrent)
    |
    v
StartBatchTranslationAsync()
    |---- Create folder URIs
    |---- Call _batchClient.StartTranslationAsync()
    |---- Return job ID immediately
    |
    v
Response with JobId + Status="InProgress"
    |
    v
[User polls GetTranslationStatusAsync() separately]
    |
    v
When complete, user downloads from blob storage
```

**Characteristics:**
- ? Supports multiple files
- ? Supports large files
- ? Non-blocking - returns immediately
- ? Blob storage used for source and target
- ?? Requires polling for status
- ?? Long-running operations supported

### Synchronous (Single Document) Translation

```
User Request (single file only)
    |
    v
ProcessSynchronousTranslationAsync()
    |
    v
For each target language:
    |
    |---- Read file stream
    |---- Create MultipartFormFileData
    |---- Create DocumentTranslateContent
    |
    v
_singleDocClient.TranslateAsync()
    |---- Send file directly to Azure
    |---- Receive translated BinaryData
    |---- Convert to stream
    |
    v
Upload translated file to blob storage
    |
    v
Response with TranslatedFiles + Status="Completed"
```

**Characteristics:**
- ?? Single file only (enforced)
- ? Immediate results - no polling needed
- ? Direct stream-to-stream translation
- ?? No blob storage during translation
- ? Blob storage only for final download
- ?? Blocks until complete
- ?? Smaller files recommended

## API Usage

### Synchronous Translation API

```csharp
// Create the document content
var fileData = new MultipartFormFileData(
    fileName, 
    fileStream, 
    contentType);
    
var documentContent = new DocumentTranslateContent(fileData);

// Translate synchronously
var result = await _singleDocClient.TranslateAsync(
    targetLanguage,
    documentContent,
    sourceLanguage: sourceLanguage, // or null for auto-detect
    cancellationToken: cancellationToken);

// Result is BinaryData
var translatedStream = result.Value.ToStream();
```

### Asynchronous Batch Translation API

```csharp
// Create folder URIs
var sourceUri = new Uri("https://{account}.blob.core.windows.net/{container}/source");
var targetUri = new Uri("https://{account}.blob.core.windows.net/{container}/target/es");

var input = new DocumentTranslationInput(sourceUri, targetUri, "es");

// Start batch translation
var operation = await _batchClient.StartTranslationAsync(
    new[] { input }, 
    cancellationToken);

// Cache operation for status checks
_activeOperations[operation.Id] = operation;

// Return job ID
return operation.Id;
```

## File Processing Differences

### Batch Translation File Processing

```csharp
ProcessAndUploadFilesAsync()
    |
    v
ProcessSingleFileForBatchAsync() [for each file]
    |
    v
    IF processImages AND (.docx OR .pdf)
        |-> ProcessDocumentWithImages()
            |-> Upload original
            |-> Extract images
            |-> Create images PDF
            |-> Upload metadata
    ELSE
        |-> Upload file directly
```

**Used by:** Async batch operations

### Sync Translation File Processing

```csharp
ProcessSynchronousTranslationAsync()
    |
    v
    Open file stream directly
    |
    v
    Create MultipartFormFileData
    |
    v
    Send to SingleDocumentTranslationClient
```

**Notes:** 
- Image processing NOT supported in sync mode (would require blob storage)
- Direct stream translation only

## When to Use Each Mode

### Use Synchronous Translation When:
1. ? Single file only
2. ? Small to medium file size (< 10MB)
3. ? Immediate results required
4. ? Simple document (text-only)
5. ? No image processing needed
6. ? User can wait for completion

### Use Asynchronous Translation When:
1. ? Multiple files
2. ? Large files (> 10MB)
3. ? Long-running operations
4. ? Image processing required
5. ? Complex documents
6. ? Background processing acceptable
7. ? Multiple target languages

## Error Handling

### Synchronous Errors
```csharp
try
{
    var result = await _singleDocClient.TranslateAsync(...);
}
catch (RequestFailedException ex)
{
    // Azure API error - immediate feedback
    response.Status = "Failed";
    response.ErrorMessage = $"Translation failed: {ex.Message}";
}
```

**Characteristics:**
- Immediate error reporting
- No partial results
- Client knows immediately if translation failed

### Asynchronous Errors
```csharp
// During job start
try
{
    var operation = await _batchClient.StartTranslationAsync(...);
}
catch (RequestFailedException ex)
{
    // Job creation failed
}

// During status polling
var status = await GetTranslationStatusAsync(jobId);
if (status.Status == "Failed")
{
    // Translation failed - check DocumentsFailed
}
```

**Characteristics:**
- Errors discovered during polling
- Partial results possible (some documents succeed)
- Retry logic with exponential backoff

## Performance Considerations

### Synchronous Translation
- **Latency**: Low (direct API call)
- **Throughput**: Limited to one file at a time
- **Memory**: File loaded into memory
- **Network**: Single round trip
- **Best for**: Quick, simple translations

### Asynchronous Translation
- **Latency**: Higher (blob upload + processing)
- **Throughput**: High (parallel processing)
- **Memory**: Streaming to blob storage
- **Network**: Multiple operations (upload, translate, download)
- **Best for**: Bulk operations, large files

## Configuration

Both clients share the same endpoint and credentials:

```csharp
var credential = credentialService.GetTranslationServiceCredential();

_batchClient = new DocumentTranslationClient(
    new Uri(_settings.Endpoint), 
    credential);

_singleDocClient = new SingleDocumentTranslationClient(
    new Uri(_settings.Endpoint),
    credential);
```

No additional configuration required.

## Migration Notes

### Breaking Changes
None - the public API remains the same:
```csharp
Task<TranslationResponse> TranslateDocumentsAsync(
    TranslationRequest request, 
    CancellationToken cancellationToken)
```

### Behavioral Changes
1. **Single file + sync mode**: Now uses SingleDocumentTranslationClient
   - Faster for small files
   - Direct translation without blob storage intermediary
   - Immediate results

2. **Multiple files**: Always forces async mode
   - No change in behavior
   - Ensures correct client is used

3. **Image processing**: Only available in async mode
   - Sync mode doesn't support image extraction
   - This is by design (would require blob storage)

## Testing

### Test Synchronous Translation
```csharp
var request = new TranslationRequest
{
    Files = new List<IFormFile> { singleFile },
    TargetLanguages = new List<string> { "es" },
    UseAsyncProcessing = false,  // Force sync
    AutoDetectLanguage = true
};

var response = await service.TranslateDocumentsAsync(request);

Assert.Equal("Completed", response.Status);
Assert.Single(response.TranslatedFiles);
```

### Test Asynchronous Translation
```csharp
var request = new TranslationRequest
{
    Files = new List<IFormFile> { file1, file2 },
    TargetLanguages = new List<string> { "es", "fr" },
    UseAsyncProcessing = true
};

var response = await service.TranslateDocumentsAsync(request);

Assert.Equal("InProgress", response.Status);
Assert.NotNull(response.JobId);

// Poll for completion
while (true)
{
    var status = await service.GetTranslationStatusAsync(response.JobId);
    if (status.Status == "Succeeded") break;
    await Task.Delay(5000);
}
```

## Logging

### Synchronous Translation Logs
```
[INFO] Starting SYNCHRONOUS translation using SingleDocumentTranslationClient for file: document.pdf
[INFO] Translating document.pdf to es
[INFO] Successfully translated document.pdf to es
[INFO] Synchronous translation completed for all 1 target languages
```

### Asynchronous Translation Logs
```
[INFO] Starting BATCH translation for 3 files using DocumentTranslationClient
[INFO] Processed 3 files for batch translation
[INFO] Starting batch translation with 2 input(s) using DocumentTranslationClient
[INFO] Batch translation started with operation ID: abc123...
```

## Summary

| Feature | Synchronous | Asynchronous |
|---------|------------|--------------|
| **Client** | SingleDocumentTranslationClient | DocumentTranslationClient |
| **File Count** | 1 only | 1 or more |
| **Blob Storage** | Final download only | Source + Target |
| **Returns** | Completed results | Job ID |
| **Polling** | Not needed | Required |
| **Image Processing** | No | Yes |
| **Best For** | Quick, simple | Bulk, complex |
| **Speed** | Fast (< 1 min) | Variable (minutes) |

## Conclusion

The refactored service now properly uses:
1. **SingleDocumentTranslationClient** for synchronous single-file translations
2. **DocumentTranslationClient** for asynchronous batch translations

This architecture provides:
- ? Correct SDK usage for each scenario
- ? Better performance for single files
- ? Clear separation of concerns
- ? Appropriate error handling for each mode
- ? Optimized resource usage

The implementation follows Azure SDK best practices and provides a seamless experience for both quick single-file translations and complex bulk operations.
