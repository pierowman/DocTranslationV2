# Image Filtering Configuration

The document translation service includes intelligent image filtering to avoid translating decorative or background images. This feature can be customized or disabled through configuration.

## Configuration Settings

Add the following section to your `appsettings.json` or `appsettings.Development.json`:

```json
{
  "ImageFiltering": {
    "FilterImagesWithContainedText": true,
    "FilterDecorativeImages": true,
    "MinimumImageSizeBytes": 100,
    "MinimumImageWidthPixels": 32,
    "MinimumImageHeightPixels": 32
  }
}
```

## Configuration Options

### FilterImagesWithContainedText
**Type:** `bool`  
**Default:** `true`  
**Description:** Filters images that have text fully contained within their boundaries. This typically indicates styled titles or text with colored backgrounds.

**Example:** A chapter heading "CHAPTER 1" with a blue background box would be filtered.

**When to disable:**
- You want to translate ALL images, including styled text backgrounds
- You're experiencing false positives where legitimate images are being skipped
- You're debugging why certain images aren't being processed

### FilterDecorativeImages
**Type:** `bool`  
**Default:** `true`  
**Description:** Filters images that are likely decorative elements such as:
- Borders and dividers (extreme aspect ratios)
- Solid color backgrounds
- Very small icons or bullets

**When to disable:**
- Your documents use decorative images that contain translatable text
- You want to preserve ALL images in the translated document
- You're experiencing false positives

### MinimumImageSizeBytes
**Type:** `int`  
**Default:** `100` bytes  
**Description:** Images smaller than this size (in bytes) are filtered as they're likely tiny decorative elements.

**Recommended values:**
- `0` - Disable size filtering
- `100` - Default (filters very small images)
- `500` - More aggressive filtering
- `1000` - Very aggressive (may filter small but legitimate images)

### MinimumImageWidthPixels
**Type:** `int`  
**Default:** `32` pixels  
**Description:** Images narrower than this are filtered.

**Recommended values:**
- `0` - Disable width filtering
- `32` - Default (filters tiny images)
- `50` - More aggressive
- `100` - Very aggressive (may filter small but legitimate images)

### MinimumImageHeightPixels
**Type:** `int`  
**Default:** `32` pixels  
**Description:** Images shorter than this are filtered.

**Recommended values:**
- `0` - Disable height filtering
- `32` - Default (filters tiny images)
- `50` - More aggressive
- `100` - Very aggressive (may filter small but legitimate images)

## Common Configuration Scenarios

### Scenario 1: Disable All Filtering
Process ALL images without any filtering:

```json
{
  "ImageFiltering": {
    "FilterImagesWithContainedText": false,
    "FilterDecorativeImages": false,
    "MinimumImageSizeBytes": 0,
    "MinimumImageWidthPixels": 0,
    "MinimumImageHeightPixels": 0
  }
}
```

### Scenario 2: Only Filter Decorative Images
Keep text-on-image filtering but allow decorative elements:

```json
{
  "ImageFiltering": {
    "FilterImagesWithContainedText": true,
    "FilterDecorativeImages": false,
    "MinimumImageSizeBytes": 100,
    "MinimumImageWidthPixels": 32,
    "MinimumImageHeightPixels": 32
  }
}
```

### Scenario 3: Aggressive Filtering
Filter more aggressively to reduce translation costs:

```json
{
  "ImageFiltering": {
    "FilterImagesWithContainedText": true,
    "FilterDecorativeImages": true,
    "MinimumImageSizeBytes": 1000,
    "MinimumImageWidthPixels": 100,
    "MinimumImageHeightPixels": 100
  }
}
```

### Scenario 4: Debugging Configuration
Minimal filtering to see all images in logs:

```json
{
  "ImageFiltering": {
    "FilterImagesWithContainedText": false,
    "FilterDecorativeImages": false,
    "MinimumImageSizeBytes": 50,
    "MinimumImageWidthPixels": 10,
    "MinimumImageHeightPixels": 10
  }
}
```

## Logging

When image filtering is enabled, you'll see detailed logs explaining filtering decisions:

### Images that are filtered:
```
WARN: SKIPPING image /Image7 - Text found WITHIN image boundary (styled title/background)
INFO:   Image bounds: X=100.0, Y=200.0, W=400.0, H=50.0
INFO:   Text CONTAINED in image (2 chunks): 'CHAPTER' | '1'
```

### Images that are processed:
```
INFO: Extracted image pdf_page3_img5 from page 3 (size: 45231 bytes, dimensions: 800x600)
```

### Configuration at startup:
```
INFO: Image filtering settings - TextFilter: True, DecorativeFilter: True, MinSize: 100 bytes
```

## Testing Your Configuration

1. **Start the application** and check the logs for the configuration message
2. **Upload a test document** with various image types
3. **Review the logs** to see which images are filtered and why
4. **Adjust the configuration** based on the results
5. **Restart the application** to apply changes

## Performance Impact

- **Enabled filtering:** Faster translation, lower costs (fewer images to translate)
- **Disabled filtering:** Slower translation, higher costs (all images translated), but ensures no images are missed

## Troubleshooting

### Problem: Legitimate images are being skipped

**Solution:**
1. Check the logs to see why the image was filtered
2. If filtered due to "text within boundary", set `FilterImagesWithContainedText: false`
3. If filtered as "decorative", set `FilterDecorativeImages: false`
4. If filtered due to size, reduce the minimum thresholds

### Problem: Too many decorative images are being translated

**Solution:**
1. Ensure `FilterDecorativeImages: true`
2. Increase minimum size thresholds
3. Increase minimum dimension thresholds

### Problem: Configuration changes not taking effect

**Solution:**
1. Verify the JSON syntax is correct
2. Restart the application (configuration is loaded at startup)
3. Check the startup logs to verify settings were loaded
4. Ensure you're editing the correct `appsettings.json` file

## User Secrets for Development

For local development, you can also set these in user secrets:

```bash
dotnet user-secrets set "ImageFiltering:FilterImagesWithContainedText" "false"
dotnet user-secrets set "ImageFiltering:FilterDecorativeImages" "false"
```

This allows you to override settings without modifying `appsettings.json`.
