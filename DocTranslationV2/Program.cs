using DocTranslationV2.Models;
using DocTranslationV2.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure Application Insights
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});

// Configure Translation Settings with proper binding
builder.Services.Configure<TranslationConfiguration>(config =>
{
    // Bind the entire section - this will automatically pick up from user secrets
    builder.Configuration.GetSection("AzureTranslation").Bind(config.AzureTranslation);
    builder.Configuration.GetSection("AzureBlobStorage").Bind(config.AzureBlobStorage);
    builder.Configuration.GetSection("ImageFiltering").Bind(config.ImageFiltering);
    builder.Configuration.GetSection("Diagnostics").Bind(config.Diagnostics);
});

// Add diagnostic logging for configuration during development
if (builder.Environment.IsDevelopment())
{
    // Log configuration sources to help debug
    var configRoot = (IConfigurationRoot)builder.Configuration;
    Console.WriteLine("Configuration Sources:");
    foreach (var provider in configRoot.Providers)
    {
        Console.WriteLine($"  - {provider.GetType().Name}");
    }
    
    // Log blob storage settings (without secrets)
    var blobSettings = builder.Configuration.GetSection("AzureBlobStorage");
    Console.WriteLine("\nAzureBlobStorage Configuration:");
    Console.WriteLine($"  AccountName: {blobSettings["AccountName"]}");
    Console.WriteLine($"  TenantId: {(string.IsNullOrEmpty(blobSettings["TenantId"]) ? "<not set>" : "<configured>")}");
    Console.WriteLine($"  ClientId: {(string.IsNullOrEmpty(blobSettings["ClientId"]) ? "<not set>" : "<configured>")}");
    Console.WriteLine($"  ClientSecret: {(string.IsNullOrEmpty(blobSettings["ClientSecret"]) ? "<not set>" : "<configured>")}");
    Console.WriteLine($"  ContainerName: {blobSettings["ContainerName"]}\n");
}

// Register services
builder.Services.AddHttpClient();

// Add memory cache for language caching
builder.Services.AddMemoryCache();

// Credential caching service (Singleton for credential reuse)
builder.Services.AddSingleton<ICredentialService, CredentialService>();

// Language service with caching (Singleton for caching across requests)
builder.Services.AddSingleton<ILanguageService, LanguageService>();

// Core storage and image services (Singleton for performance)
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<IPythonPdfService, PythonPdfService>();
builder.Services.AddSingleton<IImageExtractionService, ImageExtractionService>();
builder.Services.AddSingleton<IImageReplacementService, ImageReplacementService>();

// NEW: Decomposed services for better maintainability
// Job Management Service (Singleton - manages shared job state)
builder.Services.AddSingleton<IJobManagementService, JobManagementService>();

// Translation Operation Service (Singleton - manages Azure SDK clients and operation cache)
builder.Services.AddSingleton<ITranslationOperationService, TranslationOperationService>();

// Status Tracking Service (Singleton - manages status cache)
builder.Services.AddSingleton<IStatusTrackingService, StatusTrackingService>();

// Container Management Service (Singleton - manages blob containers)
builder.Services.AddSingleton<IContainerManagementService, ContainerManagementService>();

// Image Processing Orchestrator (Singleton - orchestrates image pipeline)
builder.Services.AddSingleton<IImageProcessingOrchestrator, ImageProcessingOrchestrator>();

// DocumentTranslationServiceV2 (Singleton - thin orchestration layer)
// This replaces the old monolithic DocumentTranslationService with a clean,
// maintainable orchestrator that delegates to specialized services
builder.Services.AddSingleton<IDocumentTranslationService, DocumentTranslationServiceV2>();

// Configure HttpClient for Python PDF Service with resilience policies
builder.Services.AddHttpClient("PythonPdfService", client =>
{
    var timeout = builder.Configuration.GetValue<int>("PythonPdfService:TimeoutSeconds", 120);
    client.Timeout = TimeSpan.FromSeconds(timeout);
});

// Configure HttpClient for Language API
builder.Services.AddHttpClient("LanguageApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Configure file upload limits
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500 MB
    options.ValueLengthLimit = 524288000;
    options.MultipartHeadersLengthLimit = 524288000;
});

// Configure Kestrel server limits
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000; // 500 MB
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Translation}/{action=Index}/{id?}");

app.Run();
