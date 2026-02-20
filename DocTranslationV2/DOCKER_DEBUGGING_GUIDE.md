# Docker Container Lifecycle Management for Debugging

## Problem
When debugging with Visual Studio and Docker, containers don't automatically stop when you close the browser or stop debugging.

## Solutions

### Solution 1: Use Docker Compose for Debugging (Recommended)

1. **Set the Docker Compose profile as startup project**:
   - In Visual Studio Solution Explorer, right-click on the solution
   - Select "Set Startup Projects"
   - Choose "Docker Compose" if available

2. **Or select the Docker Compose profile**:
   - In the debug dropdown at the top, select "Docker Compose" instead of "Container (Dockerfile)"
   - This will automatically stop containers when debugging stops

3. **Benefits**:
   - Containers automatically stop when debugging stops
   - Better integration with Visual Studio
   - Easier multi-container management

### Solution 2: Manual Container Cleanup Script

Run the PowerShell script when containers don't stop:

```powershell
.\cleanup-containers.ps1
```

Or manually:
```powershell
# List all containers
docker ps -a

# Stop specific container
docker stop <container_id>

# Remove container
docker rm <container_id>

# Or stop and remove all DocTranslation containers at once
docker ps -a --filter "name=doctranslation" --format "{{.ID}}" | ForEach-Object { docker stop $_; docker rm $_ }
```

### Solution 3: Configure Visual Studio Docker Settings

1. **Tools ? Options ? Container Tools**:
   - Enable "Remove containers on close"
   - Enable "Pull required Docker images on project open"

2. **Project Properties**:
   - Right-click DocTranslationV2 project ? Properties
   - Go to Debug ? Docker
   - Check "Remove container after debugging session"

### Solution 4: Add --rm Flag to Docker Run

The `launchSettings.json` has been updated to include:
```json
"DockerfileRunArguments": "--rm"
```

This tells Docker to automatically remove the container when it exits.

### Solution 5: Use Docker Desktop

In Docker Desktop:
1. Open the Containers tab
2. Find your running containers
3. Click the stop icon
4. Click the trash icon to remove

## Best Practices

### During Development

1. **Use Docker Compose profile** for debugging - containers will stop automatically
2. **Regularly clean up** with the cleanup script
3. **Monitor containers** with Docker Desktop

### Before Committing Code

```powershell
# Stop all containers
docker stop $(docker ps -aq)

# Remove all stopped containers
docker container prune -f

# Remove all unused images
docker image prune -a -f

# Remove all unused volumes
docker volume prune -f
```

## Troubleshooting

### Containers Won't Stop

If containers are locked or won't stop:

```powershell
# Force kill
docker kill <container_id>

# Force remove
docker rm -f <container_id>
```

### Port Already in Use

```powershell
# Find process using port 5055
netstat -ano | findstr :5055

# Kill the process (replace PID with actual process ID)
taskkill /PID <PID> /F
```

### Containers Keep Restarting

Check the restart policy:
```powershell
docker inspect <container_id> | Select-String "RestartPolicy"
```

The `docker-compose.override.yml` sets `restart: "no"` to prevent this during debugging.

## Quick Reference Commands

```powershell
# View running containers
docker ps

# View all containers (including stopped)
docker ps -a

# Stop all DocTranslation containers
docker ps -a --filter "name=doctranslation" -q | ForEach-Object { docker stop $_ }

# Remove all DocTranslation containers
docker ps -a --filter "name=doctranslation" -q | ForEach-Object { docker rm $_ }

# View Docker logs
docker logs <container_id>

# Follow Docker logs
docker logs -f <container_id>

# Execute command in running container
docker exec -it <container_id> powershell
```

## Integration with Visual Studio

### Debug Profiles

Three profiles are now available:

1. **http/https**: Direct .NET debugging (no Docker)
2. **Container (Dockerfile)**: Single container debugging with `--rm` flag
3. **Docker Compose**: Multi-container debugging with automatic cleanup

### Recommended Workflow

1. **Daily Development**: Use `http` or `https` profile (fastest)
2. **Testing Docker Integration**: Use `Container (Dockerfile)` profile
3. **Testing Full Stack**: Use `Docker Compose` profile
4. **Before Deploy**: Test with `docker-compose.yml` in production mode

## Automatic Cleanup on Debugging Stop

With the updated configuration:
- Containers automatically stop when you stop debugging (Docker Compose profile)
- Containers are removed automatically with `--rm` flag (Dockerfile profile)
- No manual cleanup needed in most cases

## Additional Resources

- [Visual Studio Container Tools](https://docs.microsoft.com/en-us/visualstudio/containers/)
- [Docker Compose in Visual Studio](https://docs.microsoft.com/en-us/visualstudio/containers/docker-compose)
- [Docker CLI Reference](https://docs.docker.com/engine/reference/commandline/cli/)
