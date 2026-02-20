# FINAL FIX: Dependency Injection Scoping Issue

## The Root Cause

The translation status polling was failing with "Job not found" because `DocumentTranslationService` was registered with the **wrong lifetime** in the dependency injection container.

### What Was Wrong

In `Program.cs`:
```csharp
builder.Services.AddScoped<IDocumentTranslationService, DocumentTranslationService>();
```

**Scoped services create a NEW instance for EACH HTTP request!**

### The Problem Flow

```
1. Client calls /api/translation/start
   ? ASP.NET creates Instance A of DocumentTranslationService
   ? Instance A caches operation in its _activeOperations dictionary
   ? Response sent: { jobId: "abc-123", status: "InProgress" }
   ? Request ends, Instance A is disposed ?

2. Client polls /api/translation/status?jobId=abc-123
   ? ASP.NET creates Instance B of DocumentTranslationService (NEW instance!)
   ? Instance B checks its _activeOperations dictionary
   ? Dictionary is EMPTY (it's a new instance!) ?
   ? Returns: "Job not found"
```

### Why This Happened

From the logs:
```
[INFO] CACHED operation for jobId abc-123... Dictionary now contains 1 entries
[INFO] Translation job abc-123 started successfully

[INFO] Checking status for translation job abc-123
[INFO] Attempting to retrieve... Cache contains 0 entries  ? DIFFERENT INSTANCE!
[WARN] Failed to find operation in cache
```

The cache went from **1 entry** to **0 entries** because they were **different service instances**.

## The Fix

Change the service registration from **Scoped** to **Singleton**:

### Before (? Wrong):
```csharp
builder.Services.AddScoped<IDocumentTranslationService, DocumentTranslationService>();
```

### After (? Correct):
```csharp
// DocumentTranslationService MUST be Singleton to preserve the in-memory operation cache across requests
builder.Services.AddSingleton<IDocumentTranslationService, DocumentTranslationService>();
```

## Why Singleton Is Correct

### Benefits:
1. ? **Same instance across all requests** - Cache persists
2. ? **Operation tracking works** - jobId lookups succeed
3. ? **Memory efficient** - One instance for the entire application
4. ? **Thread-safe** - Already using lock statements for cache access

### The Service Is Already Thread-Safe:
```csharp
private readonly Dictionary<string, DocumentTranslationOperation> _activeOperations = new();
private readonly object _operationsLock = new();

// All cache access is protected
lock (_operationsLock)
{
    _activeOperations[jobId] = operation;
}
```

## Service Lifetimes in This Project

| Service | Lifetime | Reason |
|---------|----------|--------|
| `ICredentialService` | Singleton | Reuse credentials across requests |
| `ILanguageService` | Singleton | Cache language list globally |
| `IBlobStorageService` | Singleton | Reuse blob clients |
| `IPythonPdfService` | Singleton | HTTP client reuse |
| `IImageExtractionService` | Singleton | Stateless service |
| `IImageReplacementService` | Singleton | Stateless service |
| **`IDocumentTranslationService`** | **Singleton** | **Must cache operations across requests** |

## How It Works Now

```
1. Application starts
   ? Single DocumentTranslationService instance created
   ? Instance has empty _activeOperations dictionary

2. Client calls /start
   ? Same instance handles request
   ? Caches operation: _activeOperations["abc-123"] = operation
   ? Returns: { jobId: "abc-123", status: "InProgress" }

3. Client polls /status?jobId=abc-123
   ? Same instance handles request  ?
   ? Checks cache: _activeOperations["abc-123"]
   ? Found! Returns current status  ?

4. Client polls again
   ? Same instance handles request  ?
   ? Cache still has the operation  ?
   ? Returns updated status  ?
```

## Testing the Fix

### Test Case 1: Normal Translation Flow
```bash
# Start translation
POST /api/translation/start
Response: { jobId: "abc-123", status: "InProgress" }

# Poll for status (immediately)
GET /api/translation/status?jobId=abc-123
Response: { jobId: "abc-123", status: "InProgress", ... }  ?

# Poll again
GET /api/translation/status?jobId=abc-123
Response: { jobId: "abc-123", status: "Running", ... }  ?

# Poll until complete
GET /api/translation/status?jobId=abc-123
Response: { jobId: "abc-123", status: "Succeeded", translatedFiles: [...] }  ?
```

### Test Case 2: Multiple Concurrent Users
```bash
# User A starts translation
POST /api/translation/start (from User A)
Response: { jobId: "user-a-job" }

# User B starts translation
POST /api/translation/start (from User B)
Response: { jobId: "user-b-job" }

# User A polls
GET /api/translation/status?jobId=user-a-job
Response: User A's status  ?

# User B polls
GET /api/translation/status?jobId=user-b-job
Response: User B's status  ?

# No mixing or confusion  ?
```

### Test Case 3: Service Restart
```bash
# Start translation
POST /api/translation/start
Response: { jobId: "abc-123" }

# Restart application (simulate)
<app restarts>

# Poll for status
GET /api/translation/status?jobId=abc-123
Response: { status: "NotFound", errorMessage: "Job not found (service restarted)" }

# This is EXPECTED behavior - in-memory cache is lost on restart
# For production, use persistent storage (database/Redis)
```

## Important Notes

### Singleton Services and Dependency Injection

Singleton services can **only** inject other Singleton services. In this project:

```csharp
public DocumentTranslationService(
    IOptions<TranslationConfiguration> config,        // ? Singleton
    IBlobStorageService blobStorageService,           // ? Singleton
    IImageExtractionService imageExtractionService,   // ? Singleton
    ILanguageService languageService,                 // ? Singleton
    ILogger<DocumentTranslationService> logger,       // ? Singleton
    ICredentialService credentialService)             // ? Singleton
{
    // All dependencies are Singleton or effectively Singleton (IOptions, ILogger)
}
```

All dependencies are Singleton, so **this is safe**.

### Service Restart Limitations

**In-memory cache is lost on restart:**
- Application restart
- App pool recycle
- Server reboot
- Deployment

**For production, consider:**
1. **Database mapping** - Store jobId?operationId in SQL/CosmosDB
2. **Distributed cache** - Use Redis/Azure Cache
3. **Accept limitation** - Document that jobs are lost on restart

### Thread Safety

The service is thread-safe because:
```csharp
// All cache operations use locks
lock (_operationsLock)
{
    _activeOperations[jobId] = operation;
}

lock (_operationsLock)
{
    _activeOperations.TryGetValue(jobId, out operation);
}
```

Multiple concurrent requests can safely access the singleton instance.

## Files Modified

### 1. Program.cs
**Change:**
```csharp
// Before
builder.Services.AddScoped<IDocumentTranslationService, DocumentTranslationService>();

// After
builder.Services.AddSingleton<IDocumentTranslationService, DocumentTranslationService>();
```

**Location:** Line ~57

### 2. No Changes to DocumentTranslationService.cs
The service was already designed correctly with:
- Thread-safe cache access (locks)
- Immutable settings (_settings, _blobSettings)
- Thread-safe clients (_batchClient, _singleDocClient)

**It was just registered with the wrong lifetime!**

## Verification

After the fix, you should see in logs:

```
[INFO] Starting translation job abc-123...
[INFO] CACHED operation for jobId abc-123. Dictionary now contains 1 entries
[INFO] Translation job abc-123 started successfully

[INFO] Checking status for translation job abc-123
[INFO] Attempting to retrieve operation for jobId abc-123 from cache. Cache contains 1 entries  ? Still has 1!
[INFO] Cache keys: abc-123  ? Found!
[INFO] Successfully retrieved cached operation for jobId abc-123  ? Success!
[INFO] Found cached operation for job abc-123, operation ID: xyz-789
[INFO] Translation job abc-123 status: InProgress, Total: 1, Succeeded: 0, Failed: 0
```

**The cache maintains its entries across requests! ?**

## Lessons Learned

### 1. Choose Service Lifetime Carefully
- **Transient**: New instance every time (use for lightweight, stateless services)
- **Scoped**: New instance per request (use for request-specific state, like DbContext)
- **Singleton**: Single instance for application lifetime (use for shared state, caches)

### 2. In-Memory State Requires Singleton
If your service has in-memory state that must persist across requests:
```csharp
private readonly Dictionary<string, SomeData> _cache = new();
```

Then it **MUST** be registered as Singleton (or use external cache like Redis).

### 3. Diagnostic Logging Helps
The detailed logging added helped identify the issue:
```csharp
_logger.LogInformation("Cache contains {Count} entries", _activeOperations.Count);
```

Without this, the problem would have been much harder to diagnose.

## Related Documents
- `MULTIPLE_LANGUAGES_POLLING_FIX.md` - Multiple language support
- `JOBID_OPERATIONID_CACHE_FIX.md` - JobId vs OperationId caching
- `CONTAINER_BASED_TRANSLATION_FIX.md` - Container-based URIs
- `STATUS_CHECK_NOT_FOUND_EXPLANATION.md` - Why status checks can fail

## Summary

**Problem:** Service registered as Scoped ? new instance per request ? cache not shared  
**Solution:** Service registered as Singleton ? same instance always ? cache shared  
**Result:** Status polling works correctly! ??
