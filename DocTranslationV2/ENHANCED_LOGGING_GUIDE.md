# Enhanced Logging for Z-Order and PDF Scaling

## What Was Changed

We've upgraded all logging to **Information level** so you can see exactly what's happening with z-order capture and PDF scaling.

---

## 1. Enabled Debug Logging

**File:** `appsettings.Development.json`

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning",
    "DocTranslationV2.Services.ImageExtractionService": "Debug",
    "DocTranslationV2.Services.ImageReplacementService": "Debug"
  }
}
```

**Why:** Enables detailed logging for image services even if we use LogDebug.

---

## 2. Enhanced Z-Order Extraction Logging

**File:** `ImageExtractionService.cs` - `ExtractImagesFromPowerPointAsync`

### New Logs You'll See:

```
[INFO] Slide 1: Found 3 image parts
[INFO]   ? Picture with relationship rId2 assigned z-order 0
[INFO]   ? Picture with relationship rId5 assigned z-order 4
[INFO]   ? Picture with relationship rId8 assigned z-order 9
[INFO] Slide 1: Captured z-order for 3 pictures
[INFO]   ? Image pptx_slide1_img0_rId2 has z-order 0 (relationship rId2)
[INFO]   ? Image pptx_slide1_img1_rId5 has z-order 4 (relationship rId5)
[INFO]   ? Image pptx_slide1_img2_rId8 has z-order 9 (relationship rId8)
[INFO] Extracted image pptx_slide1_img0_rId2 from slide 1 ... Z-Order: 0
[INFO] Extracted image pptx_slide1_img1_rId5 from slide 1 ... Z-Order: 4
[INFO] Extracted image pptx_slide1_img2_rId8 from slide 1 ... Z-Order: 9
```

**What This Tells You:**
- ? Z-order IS being captured
- ? Which relationship ID has which z-order
- ? Lower numbers are behind, higher numbers are in front

---

## 3. Enhanced PDF Scaling Logging

**File:** `ImageExtractionService.cs` - `CreatePdfFromImagesAsync`

### New Logs You'll See:

```
[INFO] Creating PDF from 3 images with DPI scaling
[INFO]   ? Image 0 (slide1_image_0.png): 1920x1080 pixels ? 1440.0x810.0 PDF points (scale factor: 0.75)
[INFO]   ? Created PDF page 0 with size 1440.0x810.0 points
[INFO]   ? Added image 0 as full-page (no margins)
[INFO]   ? Image 1 (slide1_image_1.png): 800x600 pixels ? 600.0x450.0 PDF points (scale factor: 0.75)
[INFO]   ? Created PDF page 1 with size 600.0x450.0 points
[INFO]   ? Added image 1 as full-page (no margins)
[INFO] ? Successfully created PDF with 3 images (pixels scaled to PDF points at 96 DPI)
```

**What This Tells You:**
- ? Pixel dimensions of each image
- ? Conversion to PDF points (multiply by 0.75)
- ? Actual PDF page sizes created
- ? Scaling is working correctly

---

## 4. Enhanced Z-Order Replacement Logging

**File:** `ImageExtractionService.cs` - `ReplaceImagesInPowerPointAsync`

### New Logs You'll See:

```
[INFO] Replacing 3 images in PowerPoint with position tracking
[INFO] Found 3 images with z-order metadata:
[INFO]   ? pptx_slide1_img0_rId2: Z-Order 0 (RelId: rId2)
[INFO]   ? pptx_slide1_img1_rId5: Z-Order 4 (RelId: rId5)
[INFO]   ? pptx_slide1_img2_rId8: Z-Order 9 (RelId: rId8)
[INFO] Processing slide 1 with 3 images to replace
[INFO]   ? Replacing image 0 (RelId: rId2, Z-Order: 0)
[INFO]   ? Replaced image successfully - z-order preserved via relationship ID
[INFO]   ? Replacing image 1 (RelId: rId5, Z-Order: 4)
[INFO]   ? Replaced image successfully - z-order preserved via relationship ID
[INFO]   ? Replacing image 2 (RelId: rId8, Z-Order: 9)
[INFO]   ? Replaced image successfully - z-order preserved via relationship ID
[INFO] ? Successfully replaced 3/3 images in PowerPoint (0 skipped - no text)
[INFO] Z-Order Preservation: Images replaced via relationship ID matching - original z-order maintained
```

**What This Tells You:**
- ? Z-order metadata IS being loaded from JSON
- ? Each image knows its z-order
- ? Replacement happens via relationship ID (which preserves z-order)
- ? Confirmation that z-order is maintained

---

## How Z-Order is Preserved

### The Key Insight

**PowerPoint z-order is determined by position in the shape tree, NOT by image data.**

When we replace an image:
1. ? We find the `ImagePart` by `RelationshipId`
2. ? We update the image DATA in that `ImagePart`
3. ? The `Picture` element in the shape tree **stays in the same position**
4. ? Therefore, z-order is **automatically preserved**

```
Shape Tree (determines z-order):
<p:pic>  ? Position 0 (Z-Order: 0)
  <a:blip r:embed="rId2" />  ? This RelationshipId stays the same
</p:pic>
<p:sp>  ? Position 1
  ...
</p:sp>
<p:pic>  ? Position 2 (Z-Order: 2)
  <a:blip r:embed="rId5" />  ? This RelationshipId stays the same
</p:pic>

When we replace image via RelationshipId:
- The Picture element stays in the same position
- Only the ImagePart data changes
- Z-order is preserved!
```

---

## What To Look For In Logs

### ? Good - Z-Order Working

**During Extraction:**
```
[INFO]   ? Picture with relationship rId2 assigned z-order 0
[INFO]   ? Image pptx_slide1_img0_rId2 has z-order 0
```

**During Replacement:**
```
[INFO] Found 3 images with z-order metadata:
[INFO]   ? pptx_slide1_img0_rId2: Z-Order 0 (RelId: rId2)
[INFO]   ? Replaced image successfully - z-order preserved via relationship ID
```

### ?? Warning - Z-Order May Not Be Captured

```
[WARN] Slide 1: No shape tree found - z-order cannot be captured
[WARN]   ? Image pptx_slide1_img0_rId2 has NO z-order captured
```

### ?? Warning - Z-Order Metadata Missing

```
[WARN] No images have z-order metadata - layering may not be preserved
```

---

## What To Look For - PDF Scaling

### ? Good - Scaling Working

```
[INFO]   ? Image 0: 1920x1080 pixels ? 1440.0x810.0 PDF points (scale factor: 0.75)
```

**Verify:**
- ? Points = Pixels × 0.75
- ? 1920 × 0.75 = 1440 ?
- ? 1080 × 0.75 = 810 ?

### ? Bad - No Scaling (old code)

```
[INFO] Adding image 0 with dimensions 1920x1080
```

**No mention of:**
- ? "pixels ?  PDF points"
- ? "scale factor"
- ? "DPI"

---

## Testing Steps

### 1. Restart Application

```powershell
# Stop the application (Ctrl+C)
# Then start it again
dotnet run --project DocTranslationV2
```

**Why:** New logging changes need fresh process.

### 2. Upload PowerPoint with Multiple Images

Create a test PowerPoint:
- Slide 1: Add 3 images
- Overlap them (drag one on top of another)
- Note which image is in front

### 3. Check Extraction Logs

Look for:
```
[INFO] Slide 1: Captured z-order for 3 pictures
[INFO]   ? Picture with relationship rId2 assigned z-order 0  ? BACK
[INFO]   ? Picture with relationship rId5 assigned z-order 4  ? MIDDLE
[INFO]   ? Picture with relationship rId8 assigned z-order 9  ? FRONT
```

### 4. Check PDF Creation Logs

Look for:
```
[INFO] Creating PDF from 3 images with DPI scaling
[INFO]   ? Image 0: 1920x1080 pixels ? 1440.0x810.0 PDF points (scale factor: 0.75)
```

### 5. Translate the PowerPoint

Upload, translate, download.

### 6. Check Replacement Logs

Look for:
```
[INFO] Found 3 images with z-order metadata:
[INFO]   ? pptx_slide1_img0_rId2: Z-Order 0 (RelId: rId2)
[INFO] Z-Order Preservation: Images replaced via relationship ID matching
```

### 7. Verify Result

Open translated PowerPoint:
- ? Same image should be in front
- ? Same image should be in back
- ? Overlapping preserved

---

## Troubleshooting

### "Not seeing the new logs"

**Problem:** Old code still running

**Solution:**
1. Stop application completely
2. Clean build: `dotnet clean`
3. Rebuild: `dotnet build`
4. Run: `dotnet run --project DocTranslationV2`

### "Seeing z-order logs during extraction but not replacement"

**Problem:** Metadata JSON not being loaded

**Check logs for:**
```
[INFO] Loaded metadata for 3 images
```

**If missing:**
- ? Check blob storage for metadata file
- ? Verify container name matches appsettings.json
- ? Check for error messages about metadata loading

### "Z-order logs say 'unknown'"

```
[INFO]   ? Replacing image 0 (RelId: rId2, Z-Order: unknown)
```

**Problem:** Z-order not captured during extraction

**Possible causes:**
- ?? No shape tree in slide
- ?? Picture not in shape tree
- ?? Slide structure unusual

**Check extraction logs for:**
```
[WARN] Slide 1: No shape tree found
```

### "PDF scaling logs not showing"

**Problem:** Old code still running

**Verify logs show:**
```
[INFO] Creating PDF from X images with DPI scaling
```

**If not:** Restart application (see "Not seeing the new logs")

---

## Summary

? **All logging upgraded to Information level**  
? **Comprehensive z-order tracking throughout pipeline**  
? **PDF scaling verification at every step**  
? **Clear success/failure indicators**  
? **Detailed troubleshooting information**  

**Now restart your application and test with a PowerPoint!**

You should see:
1. ? Z-order captured during extraction
2. ? PDF pages scaled correctly
3. ? Z-order preserved during replacement
4. ? Final confirmation of success

**The logs will tell you exactly what's happening!** ??
