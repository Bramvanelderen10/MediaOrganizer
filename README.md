# Scheduled Job Application

A .NET 8 application that runs scheduled jobs every night at 5:00 AM and can be triggered on-demand via HTTP requests on your local network.

## Features

- **Scheduled Execution**: Automatically runs job at 5:00 AM daily
- **HTTP Trigger**: Trigger job manually via POST request on port 45263
- **Media Organizer**: Sorts video files into Show/Season or Movie folders
- **Move History Database**: Tracks every move in a local SQLite file
- **Restore Endpoint**: Restore all tracked moves back to original paths
- **Docker Support**: Ready to deploy on Ubuntu server
- **Health Check**: Monitor application status
- **Logging**: Comprehensive logging for debugging and monitoring

## Prerequisites

### On Your Ubuntu Server:
- Docker and Docker Compose installed
- Port 45263 accessible on your local network

### For Local Development:
- .NET 8 SDK
- Docker (optional)

## Quick Start on Ubuntu Server

### 1. Install Docker (if not already installed)
```bash
# Update package index
sudo apt update

# Install Docker
sudo apt install -y docker.io docker-compose

# Start Docker service
sudo systemctl start docker
sudo systemctl enable docker

# Add your user to docker group (optional, to run without sudo)
sudo usermod -aG docker $USER
```

### 2. Deploy the Application
```bash
# Clone or copy your project to the server
cd /path/to/formattool

# Build and start the container
docker-compose up -d

# Check if it's running
docker-compose ps
```

### 3. View Logs
```bash
# View real-time logs
docker-compose logs -f

# View specific number of lines
docker-compose logs --tail=100
```

## Usage

### Automatic Scheduled Execution
The job runs automatically every day at 5:00 AM (server time). Check logs to confirm execution:
```bash
docker-compose logs | grep "Job execution"
```

### Manual Trigger via HTTP
From any device on your local network:

```bash
# Trigger the job
curl -X POST http://YOUR_SERVER_IP:45263/trigger-job

# Restore all tracked moves back to original structure
curl -X POST http://YOUR_SERVER_IP:45263/restore-folder-structure

# Trigger with an explicit folder override
curl -X POST http://YOUR_SERVER_IP:45263/trigger-job \
  -H "Content-Type: application/json" \
  -d '{"folderPath":"/media/incoming"}'

# Check health status
curl http://YOUR_SERVER_IP:45263/health

# View application info
curl http://YOUR_SERVER_IP:45263/
```

Replace `YOUR_SERVER_IP` with your Ubuntu server's IP address.

### Example Response
```json
{
  "message": "Job triggered successfully",
  "executedAt": "2026-03-06T10:30:00",
  "result": "Job completed successfully at 3/6/2026 10:30:00 AM"
}
```

## Configuration

### Media Organizer Settings
Edit [appsettings.json](appsettings.json):

```json
"MediaOrganizer": {
  "SourceFolder": "/path/to/your/media/folder",
  "MoveHistoryDatabasePath": "data/move-history.db",
  "VideoExtensions": [".mp4", ".mkv", ".avi", ".mov"]
}
```

Notes:
- `MoveHistoryDatabasePath` can be absolute or relative.
- Relative paths are resolved from the app base directory.
- The default is `data/move-history.db`.

Organization rules:
- TV episodes are detected by patterns like `S01E01` or `S01 E01`
- Episode files are moved to: `Show Name/Season 01/Episode Name S01E01.ext`
- Non-episode videos are treated as movies and moved to: `Movie Name/Movie Name.ext`

### Change Schedule Time
Edit [ScheduledJobService.cs](ScheduledJobService.cs#L17) and modify the cron expression:
```csharp
// Current: 0 5 * * * (5:00 AM daily)
// Format: "minute hour day month dayofweek"
_schedule = CrontabSchedule.Parse("0 5 * * *");
```

Examples:
- `"0 3 * * *"` - 3:00 AM daily
- `"0 */6 * * *"` - Every 6 hours
- `"0 9 * * 1-5"` - 9:00 AM weekdays only

### Change Timezone
Edit [Dockerfile](Dockerfile#L15) or [docker-compose.yml](docker-compose.yml#L9):
```yaml
environment:
  - TZ=Europe/Amsterdam  # Change to your timezone
```

Common timezones:
- `America/New_York`
- `Europe/London`
- `Asia/Tokyo`
- `UTC`

### Change Port
Edit [docker-compose.yml](docker-compose.yml#L6):
```yaml
ports:
  - "45263:45263"  # Change first number for external port
```

## Customize Job Logic

Core organization logic is in [JobExecutor.cs](JobExecutor.cs).

## Management Commands

```bash
# Start the application
docker-compose up -d

# Stop the application
docker-compose down

# Restart the application
docker-compose restart

# View logs
docker-compose logs -f

# Rebuild after code changes
docker-compose up -d --build

# Check status
docker-compose ps

# Remove everything (including volumes)
docker-compose down -v
```

## Firewall Configuration

If you can't access port 45263, configure UFW:

```bash
# Allow port 45263 from local network
sudo ufw allow from 192.168.1.0/24 to any port 45263

# Or allow from specific IP
sudo ufw allow from 192.168.1.100 to any port 45263

# Check firewall status
sudo ufw status
```

## Troubleshooting

### Application not starting
```bash
# Check logs for errors
docker-compose logs

# Check if port is already in use
sudo netstat -tulpn | grep 45263
```

### Can't access from network
```bash
# Verify container is running
docker-compose ps

# Check if port is exposed
docker port scheduled-job-app

# Test from server itself
curl http://localhost:45263/health
```

### Job not running at scheduled time
- Check server timezone: `date`
- Check container timezone: `docker exec scheduled-job-app date`
- Review logs for schedule confirmation

### Update timezone in running container
```bash
# Stop container
docker-compose down

# Edit docker-compose.yml with correct TZ

# Rebuild and start
docker-compose up -d --build
```

## Development

### Run Locally (without Docker)
```bash
# Install .NET 8 SDK from https://dotnet.microsoft.com/download

# Restore dependencies
dotnet restore

# Run the application
dotnet run

# Application will start on http://localhost:45263
```

### Build Docker Image
```bash
docker build -t scheduled-job-app .
```

## Project Structure

```
.
├── Program.cs                    # Application entry point & API endpoints
├── ScheduledJobService.cs        # Background service for scheduling
├── JobExecutor.cs                # Job logic implementation
├── ScheduledJobApp.csproj        # Project configuration
├── Dockerfile                    # Docker configuration
├── docker-compose.yml            # Docker Compose configuration
├── appsettings.json              # Application settings
└── README.md                     # This file
```

## License

This project is open source and available for personal and commercial use.
