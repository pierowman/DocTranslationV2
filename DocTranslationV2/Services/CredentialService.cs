using Azure.Core;
using Azure.Identity;
using Azure;
using DocTranslationV2.Models;
using Microsoft.Extensions.Options;

namespace DocTranslationV2.Services;

/// <summary>
/// Provides cached Azure credentials to avoid re-authentication overhead
/// </summary>
public interface ICredentialService
{
    TokenCredential GetBlobStorageCredential();
    AzureKeyCredential GetTranslationServiceCredential();
}

public class CredentialService : ICredentialService
{
    private readonly Lazy<ClientSecretCredential> _blobCredential;
    private readonly Lazy<AzureKeyCredential> _translationCredential;
    private readonly ILogger<CredentialService> _logger;
    private readonly AzureBlobStorageSettings _blobSettings;
    private readonly AzureTranslationSettings _translationSettings;

    public CredentialService(
        IOptions<TranslationConfiguration> config,
        ILogger<CredentialService> logger)
    {
        _logger = logger;
        _blobSettings = config.Value.AzureBlobStorage;
        _translationSettings = config.Value.AzureTranslation;

        // Lazy initialization - only created when first accessed
        _blobCredential = new Lazy<ClientSecretCredential>(() =>
        {
            _logger.LogInformation("Initializing blob storage credential");
            
            // Validate configuration
            if (string.IsNullOrWhiteSpace(_blobSettings.TenantId))
            {
                _logger.LogError("TenantId is not configured for blob storage");
                throw new InvalidOperationException("AzureBlobStorage:TenantId is required but not configured. Please set it in user secrets or appsettings.json");
            }
            
            if (string.IsNullOrWhiteSpace(_blobSettings.ClientId))
            {
                _logger.LogError("ClientId is not configured for blob storage");
                throw new InvalidOperationException("AzureBlobStorage:ClientId is required but not configured. Please set it in user secrets or appsettings.json");
            }
            
            if (string.IsNullOrWhiteSpace(_blobSettings.ClientSecret))
            {
                _logger.LogError("ClientSecret is not configured for blob storage");
                throw new InvalidOperationException("AzureBlobStorage:ClientSecret is required but not configured. Please set it in user secrets or appsettings.json");
            }

            _logger.LogInformation("Creating ClientSecretCredential for Blob Storage with TenantId: {TenantId}, ClientId: {ClientId}", 
                _blobSettings.TenantId, 
                _blobSettings.ClientId);
                
            return new ClientSecretCredential(
                _blobSettings.TenantId,
                _blobSettings.ClientId,
                _blobSettings.ClientSecret);
        });

        // Use API Key for Translation Service
        _translationCredential = new Lazy<AzureKeyCredential>(() =>
        {
            _logger.LogInformation("Initializing translation service credential");
            
            // Validate configuration
            if (string.IsNullOrWhiteSpace(_translationSettings.SubscriptionKey))
            {
                _logger.LogError("SubscriptionKey is not configured for translation service");
                throw new InvalidOperationException("AzureTranslation:SubscriptionKey is required but not configured. Please set it in user secrets or appsettings.json");
            }

            _logger.LogInformation("Creating AzureKeyCredential for Translation Service");
                
            return new AzureKeyCredential(_translationSettings.SubscriptionKey);
        });
    }

    public TokenCredential GetBlobStorageCredential()
    {
        return _blobCredential.Value;
    }

    public AzureKeyCredential GetTranslationServiceCredential()
    {
        return _translationCredential.Value;
    }
}
