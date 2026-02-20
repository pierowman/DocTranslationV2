# Multiple Languages Support with Status Polling Fix

## Changes Made

### 1. Support for Multiple Target Languages
Previously, the batch translation only supported the first target language. Now it properly handles all target languages in a single translation operation.

**Before:**
```csharp
// Only used first language
var targetLang = targetLanguages.First();
var input = new DocumentTranslationInput(sourceUri, targetUri, targetLang);
```

**After:**
```csharp
// Create translation source
var translationSource = autoDetect 
    ? new TranslationSource(sourceUri)
    : new TranslationSource(sourceUri) { LanguageCode = sourceLanguage };

// Create translation targets for ALL languages
var targets = new List<TranslationTarget>();
foreach (var targetLang in targetLanguages)
{
    var target = new TranslationTarget(targetUri, targetLang);
    targets.Add(target);
}

// Single input with multiple targets
var input = new DocumentTranslationInput(translationSource, targets);
```

### 2. Restored Polling for Job Completion
Previously, the code was calling `WaitForCompletionAsync()` which blocked until translation finished. This was changed to support asynchronous polling.

**Before:**
```csharp
var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
await operation.WaitForCompletionAsync(cancellationToken); // ? Blocks!
response.Status = "Completed";
```

**After:**
```csharp
var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
response.Status = "InProgress"; // ? Return immediately
// Client polls via GetTranslationStatusAsync
```

### 3. Added TranslatedFiles to JobStatus
The `JobStatus` model now includes a list of translated files, which is populated when the job completes.

**Added to JobStatus model:**
```csharp
public List<TranslatedFile> TranslatedFiles { get; set; } = new();
```

### 4. Populate Translated Files on Completion
When `GetTranslationStatusAsync` detects a job has succeeded, it now populates the translated files list.

**New method added:**
```csharp
private async Task PopulateTranslatedFilesAsync(string jobId, JobStatus jobStatus, CancellationToken cancellationToken)
{
    // Get the cached operation
    DocumentTranslationOperation? operation = null;
    lock (_operationsLock)
    {
        _activeOperations.TryGetValue(jobId, out operation);
    }

    if (operation == null) return;

    var translatedFiles = new List<TranslatedFile>();
    
    // Get document statuses from the operation
    await foreach (var document in operation.GetDocumentStatusesAsync())
    {
        if (document.Status == DocumentTranslationStatus.Succeeded)
        {
            translatedFiles.Add(new TranslatedFile
            {
                OriginalFileName = Path.GetFileName(document.SourceDocumentUri?.AbsolutePath ?? "unknown"),
                TargetLanguage = document.TranslatedToLanguageCode ?? "unknown",
                TranslatedBlobUrl = document.TranslatedDocumentUri?.ToString() ?? ""
            });
        }
    }

    jobStatus.TranslatedFiles = translatedFiles;
}
```

## How Azure Organizes Multiple Languages

When you submit a translation with multiple target languages to the same target container:

**Input:**
- Source Container: `job-123-source` (contains: `document.pdf`)
- Target Container: `job-123-target`
- Target Languages: `es`, `fr`, `de`

**Azure's Output Organization:**
Azure automatically organizes the translated files in the target container. The exact structure depends on Azure's implementation, but typically:

```
job-123-target/
  ??? document.pdf (translated to es)
  ??? document.pdf (translated to fr)
  ??? document.pdf (translated to de)
```

Or with language prefixes/suffixes:
```
job-123-target/
  ??? document_es.pdf
  ??? document_fr.pdf
  ??? document_de.pdf
```

The `DocumentTranslationOperation.GetDocumentStatusesAsync()` method provides the exact URIs for each translated document, including the language code.

## Benefits of This Approach

? **Single Operation** - One API call translates to all languages  
? **Parallel Processing** - All languages processed simultaneously  
? **Cost Efficient** - Reduced API calls  
? **Non-Blocking** - Client can continue working while translation happens  
? **Real-time Status** - Client polls for updates via GetTranslationStatusAsync  
? **Complete Information** - Status includes all translated files with download URLs  

## Workflow

### 1. Start Translation (Client ? Server)
```
POST /Translation/StartTranslation
Body: { files, sourceLanguage, targetLanguages: ["es", "fr", "de"] }

Response: { jobId: "abc-123", status: "InProgress" }
```

### 2. Poll for Status (Client ? Server, repeated)
```
GET /Translation/GetJobStatus?jobId=abc-123

Response (In Progress):
{
  jobId: "abc-123",
  status: "InProgress",
  totalDocuments: 1,
  translatedDocuments: 0
}

Response (Completed):
{
  jobId: "abc-123",
  status: "Succeeded",
  totalDocuments: 3,
  translatedDocuments: 3,
  translatedFiles: [
    { originalFileName: "doc.pdf", targetLanguage: "es", translatedBlobUrl: "..." },
    { originalFileName: "doc.pdf", targetLanguage: "fr", translatedBlobUrl: "..." },
    { originalFileName: "doc.pdf", targetLanguage: "de", translatedBlobUrl: "..." }
  ]
}
```

### 3. Download Files (Client ? Server)
```
GET /Translation/DownloadFile?blobPath=...
```

## Testing

### Test Case 1: Single File, Multiple Languages
1. Upload: `test.pdf`
2. Target Languages: Spanish (es), French (fr), German (de)
3. Click "Start Translation"
4. **Expected:** 
   - Job starts with status "InProgress"
   - Client polls every 2 seconds
   - When complete, shows 3 translated files (one per language)
   - All download buttons work

### Test Case 2: Multiple Files, Multiple Languages
1. Upload: `doc1.pdf`, `doc2.docx`
2. Target Languages: Spanish (es), French (fr)
3. Click "Start Translation"
4. **Expected:**
   - Job starts with status "InProgress"
   - Client polls every 2 seconds
   - When complete, shows 4 translated files (2 files × 2 languages)
   - All download buttons work

## Files Modified

1. **DocTranslationV2/Services/DocumentTranslationService.cs**
   - `ProcessBatchTranslationAsync()` - Changed to return immediately with "InProgress" status
   - `StartBatchTranslationAsync()` - Updated to support multiple target languages using TranslationSource and TranslationTarget
   - `GetTranslationStatusAsync()` - Added call to PopulateTranslatedFilesAsync when job succeeds
   - `PopulateTranslatedFilesAsync()` - New method to extract translated file information from completed operation

2. **DocTranslationV2/Models/TranslationModels.cs**
   - `JobStatus` - Added `TranslatedFiles` property

## Important Notes

### Container-Based URIs Only
Azure Translation Service requires **container-level URIs**, not folder paths:
- ? Correct: `https://storage.blob.core.windows.net/job-123-source`
- ? Wrong: `https://storage.blob.core.windows.net/container/jobs/123/source`

### No SAS Tokens Required
We use **managed identity authentication**, not SAS URIs:
- Azure Translation Service uses its managed identity
- Requires "Storage Blob Data Contributor" role on the storage account
- No SAS tokens needed in URIs

### Operation Caching
The translation operation is cached when started:
```csharp
lock (_operationsLock)
{
    _activeOperations[operation.Id] = operation;
}
```

This cached operation is used later to:
- Retrieve document-level status
- Get translated file URIs
- Extract error details

### Terminal Status Caching
Completed/Failed jobs are cached for 30 minutes to avoid repeated Azure API calls:
```csharp
private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30);
```

## Related Documents
- `CONTAINER_BASED_TRANSLATION_FIX.md` - Why we use separate containers per job
- `CRITICAL_JOBID_OPERATIONID_MISMATCH_FIX.md` - JobId vs OperationId distinction
- `FINAL_FIX_STATUS_POLLING.md` - Client-side polling implementation
- `STATUS_CHECK_NOT_FOUND_EXPLANATION.md` - Why status checks fail for old jobs
