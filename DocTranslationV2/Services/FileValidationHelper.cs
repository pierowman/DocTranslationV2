namespace DocTranslationV2.Services;

public static class FileValidationHelper
{
    private static readonly Dictionary<string, string[]> SupportedFormats = new()
    {
        { "Documents", new[] { ".pdf", ".docx", ".doc", ".rtf", ".txt", ".odt" } },
        { "Presentations", new[] { ".pptx", ".ppt", ".odp" } },
        { "Spreadsheets", new[] { ".xlsx", ".xls", ".ods" } },
        { "Web", new[] { ".html", ".htm", ".xml" } }
    };

    private static readonly long MaxFileSize = 524288000; // 500 MB
    private static readonly long MaxSyncFileSize = 52428800; // 50 MB for sync processing

    public static (bool isValid, string errorMessage) ValidateFile(string fileName, long fileSize, bool isSync)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Check if extension is supported
        if (!IsExtensionSupported(extension))
        {
            return (false, $"File type '{extension}' is not supported. Supported formats: {GetSupportedExtensionsString()}");
        }

        // Check file size
        if (fileSize > MaxFileSize)
        {
            return (false, $"File size exceeds maximum allowed size of {FormatBytes(MaxFileSize)}");
        }

        // Additional check for sync processing
        if (isSync && fileSize > MaxSyncFileSize)
        {
            return (false, $"For synchronous processing, file size must not exceed {FormatBytes(MaxSyncFileSize)}. Use async processing for larger files.");
        }

        return (true, string.Empty);
    }

    public static bool IsExtensionSupported(string extension)
    {
        extension = extension.ToLowerInvariant();
        return SupportedFormats.Values.Any(formats => formats.Contains(extension));
    }

    public static string GetSupportedExtensionsString()
    {
        var allExtensions = SupportedFormats.Values.SelectMany(x => x).Distinct().OrderBy(x => x);
        return string.Join(", ", allExtensions);
    }

    public static string GetFileCategory(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        foreach (var category in SupportedFormats)
        {
            if (category.Value.Contains(extension))
            {
                return category.Key;
            }
        }

        return "Unknown";
    }

    public static bool HasImageSupport(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf" || extension == ".docx" || extension == ".doc" || extension == ".pptx" || extension == ".ppt";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 Bytes";
        
        var k = 1024;
        var sizes = new[] { "Bytes", "KB", "MB", "GB", "TB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        
        return $"{Math.Round(bytes / Math.Pow(k, i), 2)} {sizes[i]}";
    }

    public static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        
        // Limit length
        if (sanitized.Length > 200)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt.Substring(0, 200 - extension.Length) + extension;
        }

        return sanitized;
    }
}
