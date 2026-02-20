# Troubleshooting: Changes Not Taking Effect

## Why aren't my changes working?

If you've made code changes but aren't seeing results, here are the most common reasons:

---

## 1. ? **Application Not Restarted**

**Most common issue!**

### Problem
Code changes require the application to be **completely stopped and restarted**, not just rebuilt.

### Solution
```powershell
# STOP the app completely (Ctrl+C or close the terminal)
# Then rebuild and restart
dotnet build
dotnet run

# OR in Visual Studio:
# 1. Stop debugging (Shift+F5)
# 2. Wait 5 seconds
# 3. Start debugging again (F5)
```

**Why:** .NET caches assemblies in memory. A rebuild updates the DLL files, but the running process uses the old assemblies until restarted.

---

## 2. ? **Docker Container Not Rebuilt**

**If running in Docker:**

### Problem
Docker uses cached layers. Changes to code require rebuilding the image.

### Solution
```powershell
# Stop and remove old container
docker stop <container-name>
docker rm <container-name>

# Rebuild WITHOUT cache
docker build --no-cache -t doctranslationv2:latest .

# Run new container
docker run -p 8080:8080 doctranslationv2:latest
```

**Why:** Docker layers are cached for speed. Use `--no-cache` to force a complete rebuild.

---

## 3. ? **Configuration Changes Not Applied**

**If you changed appsettings.json:**

### Problem
Configuration changes need app restart to take effect.

### Solution
```powershell
# After editing appsettings.json:
# 1. Stop the application
# 2. Restart it
# 3. Check logs to verify new config is loaded
```

---

## 4. ? **Still Using Old Binary**

### Problem
You're running an old published version instead of the new code.

### Solution
```powershell
# Clean all build artifacts
dotnet clean

# Rebuild
dotnet build

# Run from project directory
cd DocTranslationV2
dotnet run
```

---

## 5. ? **Browser Cache**

**For UI changes:**

### Problem
Browser is showing cached HTML/JavaScript.

### Solution
```
# Hard refresh
- Windows/Linux: Ctrl + F5
- Mac: Cmd + Shift + R

# Or clear browser cache
- Chrome: Ctrl+Shift+Del
- Edge: Ctrl+Shift+Del
- Clear "Cached images and files"
```

---

## How to Verify Changes Are Active

### For Z-Order Changes

1. **Check logs for z-order messages:**
```
[DEBUG] Picture with relationship rId2 has z-order 0
[INFO] Extracted image pptx_slide1_img0_rId2 ... Z-Order: 0
```

2. **If you don't see these logs:**
   - ? Changes aren't loaded yet
   - ? Restart the application
   - ? Check you're running the RIGHT version

3. **Test with a PowerPoint:**
   - Upload a PowerPoint with multiple images
   - Check Application Insights or console logs
   - Look for "Z-Order: X" in extraction logs

---

## For EMF/WMF/GDI+ Changes

### Expected Logs (Windows)
```
[INFO] Using native Windows GDI+ for EMF/WMF conversion
[INFO] Successfully converted image/x-emf to PNG using Windows GDI+ (125432 bytes)
```

### If you see old logs:
```
?? ImageMagick failed to convert image/x-emf
?? Created white placeholder PNG
```

**This means:** Old code is still running!

### Fix
1. **Stop application completely** (not just rebuild)
2. **Delete bin/ and obj/ folders**
   ```powershell
   Remove-Item -Recurse -Force .\DocTranslationV2\bin
   Remove-Item -Recurse -Force .\DocTranslationV2\obj
   ```
3. **Rebuild and run**
   ```powershell
   dotnet build
   dotnet run --project DocTranslationV2
   ```

---

## For PDF Scaling Changes

### Expected Logs
```
[DEBUG] Adding image 0: pixels 1920x1080 ? PDF points 1440.0x810.0
```

### If you see old logs:
```
? [DEBUG] Adding image 0 with dimensions 1920x1080 (no conversion)
```

**Fix:** Same as above - clean and rebuild.

---

## Verification Checklist

Run these checks to verify your changes are active:

### ? Check Build Output
```powershell
dotnet build | Select-String "succeeded"
```
Should show: `Build succeeded`

### ? Check Running Process
```powershell
# Windows
Get-Process -Name dotnet | Select-Object StartTime

# The StartTime should be AFTER your code changes
```

### ? Check Assembly Version (optional)
Add this to your code temporarily:
```csharp
_logger.LogInformation("Assembly Last Modified: {Time}", 
    System.IO.File.GetLastWriteTime(typeof(Program).Assembly.Location));
```

This shows when the DLL was built. Should be recent!

### ? Check Logs for New Features
Look for log messages you added:
- "Z-Order:" for z-order support
- "Windows GDI+" for GDI+ conversion
- "PDF points" for PDF scaling

**If you don't see them:** Old code is running!

---

## Nuclear Option: Complete Clean

If nothing works, do a complete clean:

```powershell
# Stop all running instances
Get-Process -Name dotnet | Stop-Process -Force

# Delete all build artifacts
Remove-Item -Recurse -Force .\DocTranslationV2\bin
Remove-Item -Recurse -Force .\DocTranslationV2\obj

# Clear NuGet cache (if needed)
dotnet nuget locals all --clear

# Rebuild from scratch
dotnet restore
dotnet build
dotnet run --project DocTranslationV2
```

---

## Docker-Specific Issues

### Problem: Image not rebuilding
```powershell
# List images
docker images

# Force rebuild
docker build --no-cache -t doctranslationv2:latest -f Dockerfile .
```

### Problem: Old container still running
```powershell
# List running containers
docker ps

# Stop and remove
docker stop <container-id>
docker rm <container-id>

# Remove old images
docker image prune -a
```

---

## How to Test Z-Order Support

1. **Create test PowerPoint:**
   - Add 3 images on one slide
   - Overlap them (drag one on top of another)
   - Note which image is in front

2. **Upload to app**

3. **Check logs:**
```
[INFO] Slide 1: Found 3 image parts
[DEBUG] Picture with relationship rId2 has z-order 0  ? Back
[DEBUG] Picture with relationship rId5 has z-order 3  ? Middle
[DEBUG] Picture with relationship rId8 has z-order 6  ? Front
[INFO] Extracted image pptx_slide1_img0_rId2 ... Z-Order: 0
[INFO] Extracted image pptx_slide1_img1_rId5 ... Z-Order: 3
[INFO] Extracted image pptx_slide1_img2_rId8 ... Z-Order: 6
```

4. **If you DON'T see "Z-Order:":**
   - ? Old code is running
   - ? Follow "Nuclear Option" above

---

## Common Mistakes

### ? Mistake 1: Rebuilding but not restarting
```powershell
# WRONG
dotnet build  # ? Builds new DLL
# ... but old process is still running with old DLL!
```

```powershell
# RIGHT
dotnet build  # ? Builds new DLL
# Stop running process (Ctrl+C)
dotnet run    # ? Starts new process with new DLL
```

### ? Mistake 2: Running from wrong directory
```powershell
# Check where you are
pwd

# Should be in solution root
# C:\Users\cbo\source\repos\DocTranslationV2

# Run from project directory
cd DocTranslationV2
dotnet run
```

### ? Mistake 3: Multiple instances running
```powershell
# Check for multiple dotnet processes
Get-Process -Name dotnet

# Kill all
Get-Process -Name dotnet | Stop-Process -Force

# Start fresh
dotnet run --project DocTranslationV2
```

---

## Summary

**Most likely issue:** ? **You didn't restart the application**

**Quick fix:**
1. Stop the app (Ctrl+C or Shift+F5)
2. Wait 5 seconds
3. Start it again (F5 or dotnet run)
4. Test again

**If that doesn't work:**
1. Clean build artifacts (delete bin/ and obj/)
2. Rebuild (`dotnet build`)
3. Restart (`dotnet run`)

**If STILL doesn't work:**
1. Nuclear option (clear everything and rebuild)
2. Check you're running the right version
3. Check logs to verify new code is executing

---

**The code changes are correct - they just need to be loaded into a fresh process!** ??
