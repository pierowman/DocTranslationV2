# Service Decomposition: Migration Strategy

## ?? Goal
Safely migrate from a monolithic `DocumentTranslationService` (1,500 lines) to 6 focused services without breaking existing functionality.

---

## ?? Timeline: 3-Week Migration

### Week 1: Create New Services (No Breaking Changes)
### Week 2: Refactor Orchestrator (Gradual Migration)
### Week 3: Test, Optimize & Deploy

---

## ?? Step-by-Step Migration

### ? Phase 1: Create Services (Days 1-5)

**Goal:** Create all new service interfaces and implementations WITHOUT touching existing code.

#### Day 1: JobManagementService
```bash
# Files to create:
- Services/IJobManagementService.cs        (interface)
- Services/JobManagementService.cs         (implementation)
```

**Register in Program.cs:**
```csharp
builder.Services.AddScoped<IJobManagementService, JobManagementService>();
```

**Test independently:**
```csharp
[Fact]
public void CreateJob_ShouldGenerateUniqueJobId()
{
    var service = new JobManagementService(logger);
    var jobId = service.CreateJob(new TranslationJobRequest { ... });
    Assert.NotNull(jobId);
}
```

---

#### Day 2: TranslationOperationService
```bash
# Files to create:
- Services/ITranslationOperationService.cs
- Services/TranslationOperationService.cs
```

**Implementation extracts Azure API logic from DocumentTranslationService:**
```csharp
public class TranslationOperationService : ITranslationOperationService
{
    private readonly DocumentTranslationClient _client;
    private readonly ConcurrentDictionary<string, DocumentTranslationOperation> _operations = new();
    
    public async Task<string> StartBatchTranslationAsync(...)
    {
        // COPY logic from DocumentTranslationService.StartBatchTranslationAsync
        // But make it focused on JUST starting the operation
    }
}
```

---

#### Day 3: StatusTrackingService
```bash
# Files to create:
- Services/IStatusTrackingService.cs
- Services/StatusTrackingService.cs
```

**Extract status logic:**
```csharp
public class StatusTrackingService : IStatusTrackingService
{
    public int CalculateProgress(JobStatus jobStatus, bool hasImageProcessing)
    {
        // COPY from DocumentTranslationService.CalculateOverallProgress
    }
    
    public string BuildDetailedStatusMessage(JobStatus jobStatus)
    {
        // COPY from DocumentTranslationService.BuildDetailedStatusMessage
    }
}
```

---

#### Day 4: ContainerManagementService
```bash
# Files to create:
- Services/IContainerManagementService.cs
- Services/ContainerManagementService.cs
```

**Extract container logic:**
```csharp
public class ContainerManagementService : IContainerManagementService
{
    public async Task<string> CreateJobContainerAsync(...)
    {
        // COPY from DocumentTranslationService.CreateContainerWithRetryAsync
        // But make it reusable
    }
}
```

---

#### Day 5: ImageProcessingOrchestrator
```bash
# Files to create:
- Services/IImageProcessingOrchestrator.cs
- Services/ImageProcessingOrchestrator.cs
```

**Extract image pipeline:**
```csharp
public class ImageProcessingOrchestrator : IImageProcessingOrchestrator
{
    public async Task MonitorAndProcessImagesAsync(...)
    {
        // COPY from DocumentTranslationService.MonitorAllTranslationsAndProcessImagesAsync
    }
}
```

---

### ? Phase 2: Refactor Orchestrator (Days 6-10)

**Goal:** Gradually replace logic in `DocumentTranslationService` with calls to new services.

#### Day 6: Update Constructor

**Before:**
```csharp
public DocumentTranslationService(
    IOptions<TranslationConfiguration> config,
    IBlobStorageService blobStorageService,
    IImageExtractionService imageExtractionService,
    IImageReplacementService imageReplacementService,
    ILanguageService languageService,
    ILogger<DocumentTranslationService> logger,
    ICredentialService credentialService)
{
    // Many dependencies...
}
```

**After:**
```csharp
public DocumentTranslationService(
    IJobManagementService jobManagement,
    ITranslationOperationService translationOps,
    IStatusTrackingService statusTracking,
    IContainerManagementService containerManagement,
    IImageProcessingOrchestrator imageProcessing,
    IBlobStorageService blobStorage,
    ILanguageService languageService,
    ILogger<DocumentTranslationService> logger)
{
    _jobManagement = jobManagement;
    _translationOps = translationOps;
    _statusTracking = statusTracking;
    _containerManagement = containerManagement;
    _imageProcessing = imageProcessing;
    _blobStorage = blobStorage;
    _languageService = languageService;
    _logger = logger;
}
```

---

#### Day 7-8: Refactor TranslateDocumentsAsync

**Strategy:** Replace sections incrementally, test after each change.

**Step 1: Job Creation**
```csharp
// BEFORE:
var jobId = Guid.NewGuid().ToString();
lock (_operationsLock)
{
    _jobMetadata[jobId] = new JobMetadata { ... };
}

// AFTER:
var jobId = _jobManagement.CreateJob(new TranslationJobRequest
{
    Files = request.Files,
    TargetLanguages = request.TargetLanguages,
    ProcessImages = request.ProcessImages,
    SourceLanguage = request.SourceLanguage,
    AutoDetectLanguage = request.AutoDetectLanguage,
    ImageFiltering = request.ImageFiltering
});
```

**Step 2: Container Creation**
```csharp
// BEFORE:
var sourceContainerName = $"job-{jobId}-source";
// ... complex container creation with retry logic

// AFTER:
var sourceContainerName = await _containerManagement.CreateJobContainerAsync(
    ContainerNamePatterns.GetSourceContainerName(jobId),
    cancellationToken);
```

**Step 3: Translation Operations**
```csharp
// BEFORE:
var operation = await _batchClient.StartTranslationAsync(input, cancellationToken);
lock (_operationsLock)
{
    _activeOperations[operation.Id] = operation;
    // complex metadata management
}

// AFTER:
var operationId = await _translationOps.StartBatchTranslationAsync(
    sourceUri, targetUri, targetLang,
    request.SourceLanguage, request.AutoDetectLanguage,
    cancellationToken);

_jobManagement.RegisterOperation(jobId, operationId, targetLang, targetContainer);
```

---

#### Day 9: Refactor GetTranslationStatusAsync

**Strategy:** Replace status logic with StatusTrackingService calls.

```csharp
public async Task<JobStatus> GetTranslationStatusAsync(
    string jobId,
    CancellationToken cancellationToken = default)
{
    // Check cache first
    var cachedStatus = _statusTracking.GetCachedTerminalStatus(jobId);
    if (cachedStatus != null)
        return cachedStatus;

    // Get job metadata
    var metadata = _jobManagement.GetJobMetadata(jobId);
    if (metadata == null)
        return NotFoundStatus(jobId);

    // Get operation statuses from Azure
    var operationIds = _jobManagement.GetOperationIds(jobId);
    var statuses = await GetOperationStatusesAsync(operationIds, cancellationToken);

    // Aggregate and compute
    var aggregated = _statusTracking.AggregateOperationStatuses(statuses);
    var jobStatus = BuildJobStatus(jobId, metadata, aggregated);
    jobStatus.PercentComplete = _statusTracking.CalculateProgress(
        jobStatus, metadata.HasImageProcessing);
    jobStatus.DetailedStatus = _statusTracking.BuildDetailedStatusMessage(jobStatus);

    // Cache if terminal
    if (IsTerminal(jobStatus.Status))
        _statusTracking.CacheTerminalStatus(jobId, jobStatus);

    return jobStatus;
}
```

---

#### Day 10: Refactor Image Processing

```csharp
// BEFORE:
await ProcessImageExtractionAsync(file, fileName, ...);
_ = Task.Run(() => MonitorAllTranslationsAndProcessImagesAsync(jobId));

// AFTER:
await _imageProcessing.ProcessImageExtractionAsync(
    request.Files, sourceContainer, jobId, 
    request.ImageFiltering, cancellationToken);

_ = Task.Run(() => _imageProcessing.MonitorAndProcessImagesAsync(jobId));
```

---

### ? Phase 3: Remove Old Code (Days 11-12)

**Goal:** Delete methods that have been moved to services.

#### Methods to DELETE from DocumentTranslationService:

```csharp
// Job Management (moved to JobManagementService)
? private class JobMetadata { ... }
? private void UpdateJobPhase(string jobId, string phase)

// Translation Operations (moved to TranslationOperationService)
? private async Task<string> StartBatchTranslationAsync(...)
? private async Task<string> StartBatchTranslationWithoutWaitingAsync(...)

// Status Tracking (moved to StatusTrackingService)
? private int CalculateOverallProgress(...)
? private string BuildDetailedStatusMessage(...)
? private void CacheTerminalStatus(...)

// Container Management (moved to ContainerManagementService)
? private async Task CreateContainerWithRetryAsync(...)
? private async Task WaitForContainerDeletionAsync(...)
? private async Task CleanupExistingContainersIfNeededAsync(...)

// Image Processing (moved to ImageProcessingOrchestrator)
? private async Task ProcessImageExtractionAsync(...)
? private async Task ProcessImageReplacementAfterTranslationAsync(...)
? private async Task MonitorAllTranslationsAndProcessImagesAsync(...)
```

**Result:** DocumentTranslationService goes from 1,500 lines ? ~300 lines

---

### ? Phase 4: Testing (Days 13-15)

#### Day 13: Unit Tests

**Test each service independently:**

```csharp
// JobManagementServiceTests.cs
public class JobManagementServiceTests
{
    [Fact]
    public void CreateJob_GeneratesUniqueIds()
    {
        var service = new JobManagementService(Mock.Of<ILogger>());
        var id1 = service.CreateJob(new TranslationJobRequest { ... });
        var id2 = service.CreateJob(new TranslationJobRequest { ... });
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void UpdateJobPhase_UpdatesMetadata()
    {
        var service = new JobManagementService(Mock.Of<ILogger>());
        var jobId = service.CreateJob(...);
        service.UpdateJobPhase(jobId, "Translating");
        var metadata = service.GetJobMetadata(jobId);
        Assert.Equal("Translating", metadata.CurrentPhase);
    }
}

// TranslationOperationServiceTests.cs (with mocked Azure SDK)
public class TranslationOperationServiceTests
{
    [Fact]
    public async Task StartBatchTranslationAsync_ReturnsOperationId()
    {
        var mockClient = new Mock<DocumentTranslationClient>();
        mockClient.Setup(c => c.StartTranslationAsync(...))
            .ReturnsAsync(new MockOperation("op-123"));
        
        var service = new TranslationOperationService(mockClient.Object, ...);
        var opId = await service.StartBatchTranslationAsync(...);
        
        Assert.Equal("op-123", opId);
    }
}
```

---

#### Day 14: Integration Tests

**Test the orchestrator with real services:**

```csharp
public class DocumentTranslationServiceIntegrationTests
{
    [Fact]
    public async Task TranslateDocumentsAsync_CreatesJobAndStartsTranslation()
    {
        // Arrange: Use real services (or TestContainers for Azure Storage)
        var services = new ServiceCollection();
        services.AddScoped<IJobManagementService, JobManagementService>();
        services.AddScoped<ITranslationOperationService, TranslationOperationService>();
        // ... register all services
        
        var provider = services.BuildServiceProvider();
        var translationService = provider.GetRequiredService<IDocumentTranslationService>();

        // Act
        var response = await translationService.TranslateDocumentsAsync(new TranslationRequest
        {
            Files = CreateTestFiles(),
            TargetLanguages = new List<string> { "es", "fr" }
        });

        // Assert
        Assert.NotNull(response.JobId);
        Assert.Equal("InProgress", response.Status);
    }
}
```

---

#### Day 15: Load Testing

**Test concurrent operations:**

```csharp
[Fact]
public async Task ConcurrentJobs_ShouldNotInterfere()
{
    var service = CreateService();
    
    // Start 10 jobs concurrently
    var tasks = Enumerable.Range(0, 10)
        .Select(i => service.TranslateDocumentsAsync(CreateRequest()))
        .ToList();
    
    var results = await Task.WhenAll(tasks);
    
    // All should succeed with unique job IDs
    Assert.Equal(10, results.Select(r => r.JobId).Distinct().Count());
}
```

---

## ?? Rollback Strategy

### If Things Go Wrong

**Option 1: Feature Flag**
```csharp
// Program.cs
var useNewServices = configuration.GetValue<bool>("UseNewServices", false);

if (useNewServices)
{
    builder.Services.AddScoped<IDocumentTranslationService, NewDocumentTranslationService>();
}
else
{
    builder.Services.AddScoped<IDocumentTranslationService, LegacyDocumentTranslationService>();
}
```

**Option 2: Keep Old Code as Fallback**
```csharp
public class DocumentTranslationService
{
    private readonly bool _useNewImplementation;
    
    public async Task<TranslationResponse> TranslateDocumentsAsync(...)
    {
        if (_useNewImplementation)
        {
            return await TranslateDocumentsAsync_NewImplementation(...);
        }
        else
        {
            return await TranslateDocumentsAsync_LegacyImplementation(...);
        }
    }
}
```

---

## ? Success Criteria

### Metrics to Track

| Metric | Before | Target | Validation |
|--------|--------|--------|------------|
| **Lines of Code** | 1,500 | 300 | ? Reduced 80% |
| **Method Count** | 40+ | <15 | ? Focused orchestration |
| **Unit Test Coverage** | 0% | 80% | ? Testable services |
| **Build Time** | Baseline | <+5% | ? No slowdown |
| **Response Time** | Baseline | <+10% | ? Minimal overhead |
| **Memory Usage** | Baseline | <+5% | ? Efficient |

### Functional Tests

- [ ] Single file translation works
- [ ] Multi-file translation works
- [ ] Multi-language translation works
- [ ] Image processing works
- [ ] Status polling works
- [ ] Job cancellation works
- [ ] Container cleanup works
- [ ] Concurrent jobs don't interfere

---

## ?? Post-Migration Benefits

### Immediate
- ? Code is readable and maintainable
- ? Services can be tested independently
- ? New developers onboard faster
- ? Bug fixes are localized

### Future
- ?? Can move services to separate microservices
- ?? Can add distributed caching easily
- ?? Can swap implementations (e.g., Google Translate)
- ?? Can scale services independently

---

## ?? Resources

### Documentation to Create
1. **Service Interface Documentation** - What each service does
2. **Migration Guide** - For other developers
3. **Testing Guide** - How to test each service
4. **Deployment Guide** - How to deploy safely

### Training
- Code review sessions for new architecture
- Pair programming for complex refactors
- Knowledge sharing on service boundaries

---

## ? Common Issues & Solutions

### Issue: Circular Dependencies
**Problem:** Service A depends on Service B which depends on Service A

**Solution:** 
- Extract shared logic to a third service
- Use events/messaging for loose coupling
- Review service boundaries

### Issue: Too Much Code Duplication
**Problem:** Multiple services do similar things

**Solution:**
- Create shared utility classes
- Use base classes for common functionality
- Don't over-DRY - some duplication is OK

### Issue: Performance Regression
**Problem:** New services add latency

**Solution:**
- Profile with BenchmarkDotNet
- Add caching at service boundaries
- Consider async/await patterns

---

## ?? Conclusion

This migration strategy:
- ? Is **incremental** - can stop at any phase
- ? Is **safe** - has rollback options
- ? Is **testable** - validates at each step
- ? Is **practical** - 3 weeks is realistic

**Ready to start?** Begin with Phase 1, Day 1: Create `IJobManagementService`!

---

Would you like me to implement any specific service to get you started?
