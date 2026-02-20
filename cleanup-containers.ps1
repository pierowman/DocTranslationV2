# Stop and remove all DocTranslation containers
Write-Host "Stopping DocTranslation containers..." -ForegroundColor Yellow

# Stop all running containers for this project
$containers = docker ps -a --filter "name=doctranslation" --format "{{.ID}}"
if ($containers) {
    docker stop $containers
    docker rm $containers
    Write-Host "Containers stopped and removed successfully." -ForegroundColor Green
} else {
    Write-Host "No DocTranslation containers found." -ForegroundColor Cyan
}

# Optional: Clean up dangling images
$danglingImages = docker images -f "dangling=true" -q
if ($danglingImages) {
    Write-Host "Cleaning up dangling images..." -ForegroundColor Yellow
    docker rmi $danglingImages
    Write-Host "Dangling images removed." -ForegroundColor Green
}

Write-Host "Cleanup complete!" -ForegroundColor Green
