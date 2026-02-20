using DocTranslationV2.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocTranslationV2.Services;

/// <summary>
/// Service for fetching and caching supported languages from Azure Translator API
/// </summary>
public interface ILanguageService
{
    Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default);
}

public class LanguageService : ILanguageService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LanguageService> _logger;
    private readonly AzureTranslationSettings _settings;
    private const string CacheKey = "SupportedLanguages";

    // Fallback languages in case API call fails
    private static readonly Lazy<List<SupportedLanguage>> _fallbackLanguages = 
        new Lazy<List<SupportedLanguage>>(() => GetFallbackLanguages());

    public LanguageService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<TranslationConfiguration> config,
        ILogger<LanguageService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("LanguageApi");
        _cache = cache;
        _logger = logger;
        _settings = config.Value.AzureTranslation;
    }

    public async Task<List<SupportedLanguage>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        if (_cache.TryGetValue(CacheKey, out List<SupportedLanguage>? cachedLanguages))
        {
            _logger.LogDebug("Returning cached supported languages ({Count} languages)", cachedLanguages!.Count);
            return cachedLanguages;
        }

        try
        {
            _logger.LogInformation("Fetching supported languages from Azure Translator API");

            // Call Azure Translator API
            var response = await _httpClient.GetAsync(_settings.LanguageApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<TranslatorLanguagesApiResponse>(json);

            if (apiResponse?.Translation == null || !apiResponse.Translation.Any())
            {
                _logger.LogWarning("API returned empty language list, using fallback");
                return _fallbackLanguages.Value;
            }

            // Convert API response to our model
            var languages = apiResponse.Translation
                .Select(kvp => new SupportedLanguage
                {
                    Code = kvp.Key,
                    Name = kvp.Value.Name,
                    NativeName = kvp.Value.NativeName
                })
                .OrderBy(l => l.Name)
                .ToList();

            // Cache the results
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(_settings.LanguageCacheExpirationMinutes))
                .SetPriority(CacheItemPriority.High);

            _cache.Set(CacheKey, languages, cacheOptions);

            _logger.LogInformation("Successfully fetched and cached {Count} supported languages", languages.Count);
            return languages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching supported languages from API, using fallback list");
            return _fallbackLanguages.Value;
        }
    }

    private static List<SupportedLanguage> GetFallbackLanguages()
    {
        // Common languages as fallback
        return new List<SupportedLanguage>
        {
            new() { Code = "en", Name = "English", NativeName = "English" },
            new() { Code = "es", Name = "Spanish", NativeName = "Español" },
            new() { Code = "fr", Name = "French", NativeName = "Français" },
            new() { Code = "de", Name = "German", NativeName = "Deutsch" },
            new() { Code = "it", Name = "Italian", NativeName = "Italiano" },
            new() { Code = "pt", Name = "Portuguese", NativeName = "Português" },
            new() { Code = "pt-BR", Name = "Portuguese (Brazil)", NativeName = "Português (Brasil)" },
            new() { Code = "ru", Name = "Russian", NativeName = "???????" },
            new() { Code = "ja", Name = "Japanese", NativeName = "???" },
            new() { Code = "zh-Hans", Name = "Chinese Simplified", NativeName = "??(??)" },
            new() { Code = "zh-Hant", Name = "Chinese Traditional", NativeName = "??(??)" },
            new() { Code = "ko", Name = "Korean", NativeName = "???" },
            new() { Code = "ar", Name = "Arabic", NativeName = "???????" },
            new() { Code = "hi", Name = "Hindi", NativeName = "??????" },
            new() { Code = "nl", Name = "Dutch", NativeName = "Nederlands" },
            new() { Code = "pl", Name = "Polish", NativeName = "Polski" },
            new() { Code = "sv", Name = "Swedish", NativeName = "Svenska" },
            new() { Code = "tr", Name = "Turkish", NativeName = "Türkçe" },
            new() { Code = "da", Name = "Danish", NativeName = "Dansk" },
            new() { Code = "no", Name = "Norwegian", NativeName = "Norsk" },
            new() { Code = "fi", Name = "Finnish", NativeName = "Suomi" },
            new() { Code = "el", Name = "Greek", NativeName = "????????" },
            new() { Code = "he", Name = "Hebrew", NativeName = "?????" },
            new() { Code = "th", Name = "Thai", NativeName = "???" },
            new() { Code = "vi", Name = "Vietnamese", NativeName = "Ti?ng Vi?t" },
            new() { Code = "id", Name = "Indonesian", NativeName = "Indonesia" },
            new() { Code = "ms", Name = "Malay", NativeName = "Melayu" },
            new() { Code = "uk", Name = "Ukrainian", NativeName = "??????????" },
            new() { Code = "cs", Name = "Czech", NativeName = "?eština" },
            new() { Code = "ro", Name = "Romanian", NativeName = "Român?" },
        };
    }

    // API Response Models
    private class TranslatorLanguagesApiResponse
    {
        [JsonPropertyName("translation")]
        public Dictionary<string, LanguageInfo>? Translation { get; set; }
    }

    private class LanguageInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("nativeName")]
        public string NativeName { get; set; } = string.Empty;

        [JsonPropertyName("dir")]
        public string? Dir { get; set; }
    }
}
