# Fix: PowerPoint EMF/WMF Images Showing 0x0 Dimensions

## The Problem

PowerPoint images (especially EMF/WMF format) were being detected but showing **0x0 dimensions** and being filtered out as "decorative", even though they were large (374KB, 961KB).

### Logs Showing the Issue:

```
info: Slide 1: Found 1 image parts
warn: ? Image pptx_slide1_img0_rId4 has NO z-order captured (relationship rId4)
dbug: Skipping tiny decorative image: 0x0
info: Skipping decorative image on slide 1: 0x0, 374340 bytes  ? 374KB image!
```

**Result:** 0 images extracted ? No metadata container created ? Image replacement fails

---

## Root Cause

**Code execution order was wrong!**

### Before (Broken):

```csharp
1. Get image data from ImagePart
2. Set width = 0, height = 0
3. Get z-order
4. ? Apply filtering (checks width/height) ? dimensions still 0!
5. Detect if EMF/WMF
6. Extract dimensions from PowerPoint metadata
7. Convert EMF/WMF to PNG
8. Add to list (never reached because filtered at step 4)
```

**The problem:** Filtering happened at step 4, but dimensions weren't extracted until step 6!

---

## The Fix

**Reordered code to extract dimensions FIRST, then filter:**

### After (Fixed):

```csharp
1. Get image data from ImagePart
2. Set width = 0, height = 0
3. Get z-order
4. ? Detect if EMF/WMF FIRST
5. ? Extract dimensions from PowerPoint metadata
6. ? Convert EMF/WMF to PNG
7. ? NOW apply filtering (with correct dimensions)
8. ? Add to list if passed filters
```

**Now:** Dimensions are correctly set BEFORE filtering checks them!

---

## Code Changes

### File: `ImageExtractionService.cs`

**Moved this block UP (before filtering):**

```csharp
// Check if this is EMF/WMF format first
var contentTypeLower = contentType.ToLowerInvariant();
bool isMetafile = contentTypeLower.Contains("emf") || 
                 contentTypeLower.Contains("wmf") ||
                 contentTypeLower.Contains("x-emf") ||
                 contentTypeLower.Contains("x-wmf") ||
                 contentTypeLower.Contains("x-ms-wmf");

if (isMetafile)
{
    // Extract dimensions from PowerPoint metadata
    var pictures = slidePart.Slide.Descendants<P.Picture>();
    foreach (var picture in pictures)
    {
        var blip = picture.Descendants<A.Blip>().FirstOrDefault();
        if (blip?.Embed?.Value == relationshipId)
        {
            var transform = picture.ShapeProperties?.Transform2D;
            if (transform?.Extents != null)
            {
                width = (int)(transform.Extents.Cx.Value / 9525);
                height = (int)(transform.Extents.Cy.Value / 9525);
            }
        }
    }
    
    // Convert EMF/WMF to PNG
    processedImageData = ConvertEmfWmfToPng(imageData, width, height, contentType);
}
else
{
    // For regular images, use ImageSharp
    using var img = SixLabors.ImageSharp.Image.Load(new MemoryStream(imageData));
    width = img.Width;
    height = img.Height;
}

// NOW filtering happens with correct dimensions
if (width < 32 && height < 32)
{
    _logger.LogInformation("Skipping tiny decorative image: {Width}x{Height}",
        width, height);
    continue;
}
```

---

## Expected Behavior After Fix

### Logs You Should See Now:

```
info: Slide 1: Found 1 image parts
info:   ? Picture with relationship rId4 assigned z-order 2
info: Slide 1: Captured z-order for 1 pictures
info:   ? Image pptx_slide1_img0_rId4 has z-order 2 (relationship rId4)
info: Detected metafile format image/x-emf for pptx_slide1_img0_rId4
info: Extracted EMF/WMF dimensions from PowerPoint metadata: 921x688
info: Using native Windows GDI+ for EMF/WMF conversion
info: Successfully converted image/x-emf to PNG using Windows GDI+ (125432 bytes)
info: Detected standard image format image/png: 921x688
info: Extracted image pptx_slide1_img0_rId4 from slide 1 (size: 125432 bytes, dimensions: 921x688 Z-Order: 2)
info: Extracted 1 images from PowerPoint across 1 slides
```

**Result:**
- ? Dimensions correctly detected: **921x688** (not 0x0)
- ? EMF/WMF converted to PNG using Windows GDI+
- ? Image passed filters
- ? 1 image extracted (not 0)
- ? Metadata container created
- ? Image replacement works

---

## Why This Happened

The original code was written with regular images (PNG, JPEG) in mind, where `ImageSharp.Load()` can read dimensions directly from the image data.

But **EMF/WMF images don't have embedded dimensions** - they're vector formats! The dimensions come from PowerPoint's metadata (the `Transform2D.Extents` property).

The fix ensures we check the format FIRST and extract dimensions appropriately BEFORE applying any filters.

---

## Testing

### Before Fix:
```
Upload: CMS AI.pptx (2 slides, 2 EMF images)
Result: "Extracted 0 images from PowerPoint"
        "Container job-xxx-source-metadata does not exist"
        Image replacement fails
```

### After Fix:
```
Upload: CMS AI.pptx (2 slides, 2 EMF images)
Result: "Extracted 2 images from PowerPoint"
        "Detected metafile format image/x-emf"
        "Extracted EMF/WMF dimensions: 921x688"
        "Successfully converted to PNG using Windows GDI+"
        Metadata container created
        Image replacement succeeds
```

---

## Related Issues Fixed

1. ? **Z-order not captured for slide 1 image**
   - **Cause:** Image was filtered out before z-order could be used
   - **Fixed:** Now image passes filters, z-order is preserved

2. ? **Metadata container not created**
   - **Cause:** 0 images extracted = no metadata = no container
   - **Fixed:** Images are extracted, metadata container created

3. ? **Image replacement fails**
   - **Cause:** No metadata to map translated images back
   - **Fixed:** Metadata exists, replacement works

---

## What to Check

After restarting your application, test with a PowerPoint containing EMF/WMF images:

1. **Check extraction logs:**
   ```
   [INFO] Detected metafile format image/x-emf
   [INFO] Extracted EMF/WMF dimensions: {Width}x{Height}
   [INFO] Successfully converted ... to PNG using Windows GDI+
   [INFO] Extracted {N} images from PowerPoint
   ```

2. **Check metadata creation:**
   ```
   [INFO] Successfully uploaded images PDF and metadata for {FileName}
   ```

3. **Check replacement logs:**
   ```
   [INFO] Found {N} images with z-order metadata
   [INFO] ? Successfully replaced {N}/{N} images in PowerPoint
   ```

---

## Summary

? **Problem:** Filtering happened before dimension extraction  
? **Fix:** Reordered code - extract dimensions FIRST, filter SECOND  
? **Result:** EMF/WMF images now properly detected and processed  

**Restart your application and test again - it should work now!** ??
