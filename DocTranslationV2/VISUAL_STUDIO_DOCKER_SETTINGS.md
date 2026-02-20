# Visual Studio Docker Settings Configuration

To prevent containers from staying running after debugging stops, configure these Visual Studio settings:

## Automatic Configuration Steps

1. **Open Visual Studio Options**
   - Go to: `Tools` ? `Options`

2. **Configure Container Tools**
   - Navigate to: `Container Tools` ? `General`
   - ? Check: **"Remove containers after debugging session"**
   - ? Check: **"Pull required Docker images on project open"**
   - ? Check: **"Automatically pull Docker images on project open"**

3. **Configure Docker Compose**
   - Navigate to: `Container Tools` ? `Docker Compose`
   - ? Check: **"Remove containers on close"**
   - ? Check: **"Recreate containers on run"**

4. **Configure Container Debugging**
   - Navigate to: `Debugging` ? `General`
   - ? Check: **"Automatically close the console when debugging stops"**

## Project-Specific Settings

Right-click on `DocTranslationV2` project ? `Properties`:

1. **Debug Tab**
   - Select "Docker" from the dropdown
   - Options should show:
     - Launch: Docker
     - Container name: (auto-generated)
     - Remove container: ? Yes
     - Publish ports: ? Yes

## Verify Settings Applied

After configuring, verify by checking:

```powershell
# Start debugging (F5)
# Stop debugging (Shift+F5)
# Check if containers are still running
docker ps
```

If no containers are listed, the settings are working correctly! ?

## If Settings Don't Persist

Some versions of Visual Studio have issues persisting these settings. In that case:

1. **Use the Docker Compose profile** (recommended)
2. **Use the cleanup script** after each debugging session
3. **Manually add the `--rm` flag** (already done in `launchSettings.json`)

## Recommended Debug Profile

For best experience, use this priority:

1. **?? Docker Compose** - Best for multi-container debugging, automatic cleanup
2. **?? Container (Dockerfile)** - Single container with `--rm` flag
3. **?? http/https** - Direct .NET debugging (fastest, no containers)

## Screenshot Locations (for reference)

If you need visual guidance, the settings are at:

- Tools ? Options ? Container Tools ? General
- Tools ? Options ? Container Tools ? Docker Compose  
- Tools ? Options ? Debugging ? General
- Project ? Properties ? Debug

## Troubleshooting

### Settings Not Available?

Make sure you have:
- Visual Studio 2022 (or later)
- Docker Desktop installed and running
- "Container development tools" workload installed

To install the workload:
1. Open Visual Studio Installer
2. Click "Modify"
3. Check "Container development tools"
4. Click "Modify" to install

### Settings Not Taking Effect?

Try:
1. Restart Visual Studio
2. Restart Docker Desktop
3. Clean and rebuild the solution
4. Delete `.vs` folder in solution directory (close VS first)

## Additional Tips

- **Performance**: First debug session is slow (building images), subsequent runs are faster
- **Cleanup**: Regularly run `docker system prune -a` to free disk space
- **Monitoring**: Keep Docker Desktop open to see containers starting/stopping
- **Logs**: Use "Container" output window in Visual Studio to see container logs
