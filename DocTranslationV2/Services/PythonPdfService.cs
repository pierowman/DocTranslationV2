using DocTranslationV2.Models;
using System.Text.Json;

namespace DocTranslationV2.Services;

/// <summary>
/// Client for Python-based PDF image replacement microservice
/// Uses PyMuPDF (fitz) for accurate PDF image manipulation
/// </summary>
public class PythonPdfService : IPythonPdfService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonPdfService> _logger;
    private readonly string _serviceUrl;
    private readonly bool _isEnabled;

    public PythonPdfService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PythonPdfService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("PythonPdfService");
        _logger = logger;
        _serviceUrl = configuration["PythonPdfService:Url"] ?? "http://localhost:5000";
        _isEnabled = configuration.GetValue<bool>("PythonPdfService:Enabled", false);
    }

    public async Task<Stream> ReplaceImagesInPdfAsync(
        Stream translatedPdfStream,
        List<ExtractedImage> imageMappings,
        CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Python PDF service is disabled. Returning PDF without image replacement.");
            var fallbackStream = new MemoryStream();
            await translatedPdfStream.CopyToAsync(fallbackStream, cancellationToken);
            fallbackStream.Position = 0;
            return fallbackStream;
        }

        try
        {
            _logger.LogInformation("Calling Python PDF service to replace {ImageCount} images", 
                imageMappings.Count);

            using var content = new MultipartFormDataContent();

            // Add translated PDF
            var pdfBytes = await ReadStreamAsync(translatedPdfStream, cancellationToken);
            var pdfContent = new ByteArrayContent(pdfBytes);
            content.Add(pdfContent, "translated_pdf", "document.pdf");

            // Add image mappings as JSON
            var mappingsData = imageMappings.Select(img => new
            {
                page_number = img.PageNumber - 1, // Python is 0-indexed
                x = img.Position?.X ?? 0,
                y = img.Position?.Y ?? 0,
                width = img.Position?.Width ?? img.Width,
                height = img.Position?.Height ?? img.Height,
                image_id = img.ImageId,
                image_index = img.ImageIndex
            }).ToList();

            var mappingsJson = JsonSerializer.Serialize(mappingsData);
            content.Add(new StringContent(mappingsJson), "image_mappings");

            // Add translated images
            for (int i = 0; i < imageMappings.Count; i++)
            {
                var imageContent = new ByteArrayContent(imageMappings[i].ImageData);
                content.Add(imageContent, "translated_images", $"image_{i}.png");
            }

            // Call Python service
            var response = await _httpClient.PostAsync(
                $"{_serviceUrl}/replace-images",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Python PDF service returned error: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new HttpRequestException(
                    $"Python PDF service failed with status {response.StatusCode}: {errorContent}");
            }

            var resultStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var outputStream = new MemoryStream();
            await resultStream.CopyToAsync(outputStream, cancellationToken);
            outputStream.Position = 0;

            _logger.LogInformation("Successfully replaced {ImageCount} images in PDF using Python service",
                imageMappings.Count);

            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Python PDF service. Returning PDF without image replacement.");
            
            // Fallback: return original PDF
            translatedPdfStream.Position = 0;
            var fallbackStream = new MemoryStream();
            await translatedPdfStream.CopyToAsync(fallbackStream, cancellationToken);
            fallbackStream.Position = 0;
            return fallbackStream;
        }
    }

    private async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        stream.Position = 0;
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }
}
