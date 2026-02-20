# Quick Fix: Stopping Docker Containers After Debugging

## Immediate Solutions

### Option 1: Run Cleanup Script (Fastest)
```powershell
.\cleanup-containers.ps1
```

### Option 2: One-Line PowerShell Command
```powershell
docker ps -a --filter "name=doctranslation" -q | ForEach-Object { docker stop $_; docker rm $_ }
```

### Option 3: Docker Desktop
1. Open Docker Desktop
2. Go to Containers tab
3. Stop and remove the containers

## Prevent This Issue

### ? Best Solution: Change Debug Profile
In Visual Studio:
1. Click the debug dropdown (currently shows "Container (Dockerfile)")
2. Select **"Docker Compose"** instead
3. Press F5 to debug

**Result**: Containers will automatically stop when you stop debugging! ?

### Alternative: Use the Updated Dockerfile Profile
The `Container (Dockerfile)` profile now includes the `--rm` flag which automatically removes containers when they exit.

## Why This Happens

Visual Studio's Docker debugging creates containers that keep running even after you:
- Close the browser
- Stop debugging (Shift+F5)
- Close Visual Studio

This is by design for performance (container reuse), but can be frustrating.

## Files Updated

? `launchSettings.json` - Added `--rm` flag and Docker Compose profile
? `docker-compose.override.yml` - Created for debugging configuration
? `docker-compose.dcproj` - Created for Visual Studio integration
? `cleanup-containers.ps1` - Created for manual cleanup

## Need More Help?

See `DOCKER_DEBUGGING_GUIDE.md` for complete documentation.
