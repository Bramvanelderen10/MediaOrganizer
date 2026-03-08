# MediaOrganizer

A .NET 10 worker service that automatically organizes video files into a clean folder structure. Runs on a configurable cron schedule and can be triggered on-demand via HTTP. Companion subtitle files are moved alongside their videos and leftover directories are cleaned up automatically.

## Features

- **Scheduled Execution** — runs on a configurable cron schedule (default: daily at 05:00)
- **HTTP Trigger** — trigger organization or restore on-demand via REST API (port 45263)
- **Smart Name Matching** — groups files by title similarity (Levenshtein distance) and detects `SxxExx`, anime-style, and movie naming patterns
- **Separate Source & Destination** — reads from a source folder and writes the organized structure to a (optionally different) destination folder
- **Subtitle Companion Moves** — automatically moves `.srt`, `.sub`, `.ass`, `.ssa`, `.vtt`, `.idx` files alongside their video
- **Move History** — every move is tracked in a local SQLite database via EF Core
- **Restore Endpoint** — reverts all tracked moves back to their original paths
- **Directory Cleanup** — removes directory trees that no longer contain any media files after organization
- **Duplicate Handling** — appends numeric suffixes (`(1)`, `(2)`, …) when destination files already exist
- **Interactive API Docs** — OpenAPI spec + Scalar UI at `/scalar/v1`
- **Docker Ready** — pre-built container image published to GitHub Container Registry

## Prerequisites

### Docker Deployment
- Docker and Docker Compose
- Port 45263 accessible on your network

### Local Development
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Quick Start

### 1. Create a `docker-compose.yml`

Note: for best performance and reliability, mount a single parent media folder and keep `source/` and `destination/` as subfolders under it. This keeps moves on the same filesystem (fast rename) and avoids cross-device move errors.

```yaml
services:
  media-organizer:
    image: ghcr.io/bramvanelderen10/mediaorganizer:main
    container_name: media-organizer
    ports:
      - "45263:45263"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - TZ=Europe/Amsterdam
      - MediaOrganizer__SourceFolder=/media/source
      - MediaOrganizer__DestinationFolder=/media/destination
      - MediaOrganizer__MoveHistoryDatabasePath=/data/move-history.db
    restart: unless-stopped
    volumes:
      - /path/to/your/videos:/media
      - media-organizer-data:/data

volumes:
  media-organizer-data:
```

### 2. Start the Container

```bash
docker-compose up -d
```

### 3. Verify

```bash
curl http://localhost:45263/health
```

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | API overview with available endpoints |
| `GET` | `/health` | Health check |
| `POST` | `/trigger-job` | Trigger organization immediately |
| `POST` | `/restore-folder-structure` | Restore all tracked moves |
| `GET` | `/openapi/v1.json` | OpenAPI specification |
| `GET` | `/scalar/v1` | Interactive API documentation |

### Trigger Job

```bash
# Use configured source folder
curl -X POST http://YOUR_SERVER_IP:45263/trigger-job

# Override source folder
curl -X POST http://YOUR_SERVER_IP:45263/trigger-job \
  -H "Content-Type: application/json" \
  -d '{"folderPath":"/media/incoming"}'
```

**Example response:**

```json
{
  "message": "Job triggered successfully",
  "executedAt": "2026-03-08T10:30:00",
  "result": "Processed 42 video files. Moved 38, skipped 4. Subtitles moved: 12. Leftover files removed: 3."
}
```

### Restore Moves

```bash
curl -X POST http://YOUR_SERVER_IP:45263/restore-folder-structure
```

## How Organization Works

### 1. Discovery
All video files under the source folder are discovered recursively, filtered by the configured extensions.

### 2. Parsing & Grouping
Each filename is cleaned (brackets, resolution tags, codec info, and release group names are stripped) and then classified:

- **TV Episode** — detected by an `SxxExx` pattern (case-insensitive). The show title is extracted from the text before the token.
- **Anime-style Episode** — detected by a trailing episode number or a `Title - Episode` pattern, optionally with a leading fansub tag in brackets (e.g. `[SubsPlease]`).
- **Movie** — anything that doesn't match an episode pattern.

Files with similar titles (≥ 80% Levenshtein similarity) are grouped together. If a group has multiple files or any `SxxExx` token, it is treated as a **Show**; otherwise it is a **Movie**.

### 3. Destination Paths

**TV/Anime shows:**
```
{destination}/{Show Title}/Season {season:00}/{filename}.ext
```

**Movies:**
```
{destination}/{Movie Title}/{Movie Title}.ext
```

### 4. Examples

| Input | Output |
|-------|--------|
| `The.Office.S02E03.Health.Care.mkv` | `The Office/Season 02/Health Care S02E03.mkv` |
| `[SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv` | `Jujutsu Kaisen/Season 01/Episode 56 S01E56.mkv` |
| `Interstellar.2014.mp4` | `Interstellar 2014/Interstellar 2014.mp4` |

### 5. Post-Move Cleanup
- Companion subtitle files (`.srt`, `.sub`, `.ass`, `.ssa`, `.vtt`, `.idx`) in the same source directory are moved alongside their video.
- Directory trees under the source that no longer contain any video or subtitle files are removed.
- All moves are recorded in the SQLite history database so they can be reversed with the restore endpoint.

## Configuration

All settings live under the `MediaOrganizer` section. They can be set in `appsettings.json` or via environment variables (using `__` as the separator).

| Setting | Default | Description |
|---------|---------|-------------|
| `SourceFolder` | — | Root folder to scan for unorganized videos (required) |
| `DestinationFolder` | Same as `SourceFolder` | Root folder for the organized output |
| `MoveHistoryDatabasePath` | `data/move-history.db` | Path to the SQLite database (absolute or relative to app base) |
| `CronSchedule` | `0 5 * * *` | NCrontab cron expression for the background schedule |
| `VideoExtensions` | `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, `.ts`, `.mpg`, `.mpeg` | Video file extensions to include |
| `SubtitleExtensions` | `.srt`, `.sub`, `.ass`, `.ssa`, `.vtt`, `.idx` | Subtitle file extensions to move alongside videos |

### Example `appsettings.json`

```json
{
  "MediaOrganizer": {
    "SourceFolder": "/media/source",
    "DestinationFolder": "/media/destination",
    "MoveHistoryDatabasePath": "/data/move-history.db",
    "CronSchedule": "0 5 * * *",
    "VideoExtensions": [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"],
    "SubtitleExtensions": [".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"]
  }
}
```

### Environment Variable Overrides (Docker)

```yaml
environment:
  - MediaOrganizer__SourceFolder=/media/source
  - MediaOrganizer__DestinationFolder=/media/destination
  - MediaOrganizer__CronSchedule=0 3 * * *
  - TZ=Europe/Amsterdam
```

### Cron Schedule Examples

| Expression | Description |
|------------|-------------|
| `0 5 * * *` | Daily at 05:00 |
| `0 3 * * *` | Daily at 03:00 |
| `0 */6 * * *` | Every 6 hours |
| `0 9 * * 1-5` | Weekdays at 09:00 |

## Docker Management

```bash
# Start
docker-compose up -d

# Stop
docker-compose down

# Restart
docker-compose restart

# View logs
docker-compose logs -f

# Rebuild from source
docker-compose up -d --build

# Remove everything including volumes
docker-compose down -v
```

## Development

### Run Locally

```bash
dotnet run --project src/MediaOrganizer/MediaOrganizer.csproj
```

The application starts on `http://localhost:45263`.

### Run Tests

```bash
dotnet test
```

### Build Docker Image

```bash
docker build -t media-organizer .
```

## Project Structure

```
src/MediaOrganizer/
├── Program.cs                         # Entry point, Kestrel config & API endpoints
├── Configuration/
│   └── MediaOrganizerOptions.cs       # Strongly-typed settings
├── Discovery/
│   └── VideoFileFinder.cs             # Recursive video file scanner
├── Parsing/
│   ├── MediaGrouper.cs               # Title parsing, similarity grouping
│   ├── MediaObject.cs                # Movie/Show model
│   ├── Season.cs / Episode.cs        # Show hierarchy
│   └── ParsedVideoFile.cs            # Intermediate parse result
├── Planning/
│   ├── MovePlanBuilder.cs            # Destination resolution & history-aware planning
│   └── MovePlanItem.cs               # Plan entry
├── Execution/
│   ├── VideoMover.cs                 # File move execution with duplicate handling
│   ├── SubtitleMover.cs              # Companion subtitle detection & move
│   └── MovedFileInfo.cs              # Move result record
├── History/
│   ├── MoveHistoryStore.cs           # SQLite persistence via EF Core
│   ├── MoveHistoryDbContext.cs       # DbContext
│   └── MoveHistoryEntry.cs           # Database entity
├── Cleanup/
│   └── DirectoryCleaner.cs           # Post-move empty directory removal
├── Orchestration/
│   ├── ScheduledJobService.cs        # BackgroundService with cron scheduling
│   ├── JobExecutor.cs                # Job entry point
│   ├── MediaFileOrganizer.cs         # Full pipeline orchestrator
│   └── MediaFileRestorer.cs          # Move reversal logic
├── Helpers/
│   ├── IFileSystem.cs                # File system abstraction for testing
│   ├── PhysicalFileSystem.cs         # Real file system implementation
│   └── PathHelpers.cs                # Unique path generation
├── appsettings.json                  # Default configuration
└── appsettings.Production.json       # Production overrides

src/MediaOrganizer.Tests/             # Unit tests
```

## Firewall

If the service is not reachable, open port 45263:

```bash
sudo ufw allow from 192.168.1.0/24 to any port 45263
```

## Troubleshooting

| Problem | What to check |
|---------|---------------|
| Container won't start | `docker-compose logs` for errors |
| Port conflict | `sudo ss -tlnp \| grep 45263` |
| Can't reach from network | `docker-compose ps` and firewall rules |
| Job doesn't run on time | Compare `date` on host vs `docker exec media-organizer date`; verify `TZ` |
| Files not being organized | Check that `SourceFolder` exists and contains files with configured extensions |
