# MediaOrganizer

MediaOrganizer is a .NET 10 minimal API service that organizes messy video folders into a clean movie/TV library layout which is supported by Jellyfin.

It supports on-demand API triggers, subtitle companion moves, source cleanup, and idempotent move tracking using a SQLite move-history database.

## What this repository contains

- `src/MediaOrganizer`: backend service (worker + API)
- `src/MediaOrganizer.Tests`: unit tests for organizer components
- `src/MediaOrganizer.App`: Flutter companion app (separate README)

## Features

- On-demand trigger endpoint
- Movie + TV + anime-style filename handling
- Title grouping via Levenshtein similarity (`>= 0.80`)
- Subtitle relocation next to moved video files
- Idempotent planning with SQLite move history
- Cleanup of empty/leftover source directories
- OpenAPI document + Scalar docs UI
- Docker-ready deployment

## Quick start (Docker)

Use one mounted parent media folder where source/destination are subfolders on the same filesystem. This keeps moves fast and avoids cross-device move failures.

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
      # Match PUID/PGID to the host user that owns your media files.
      # This prevents moved files from being owned by root and becoming
      # inaccessible (locked) when accessed over SMB from other devices.
      # Run `id` on your host to find the right values.
      - PUID=1000
      - PGID=1000
    restart: unless-stopped
    volumes:
      - /path/to/your/videos:/media
      - media-organizer-data:/data

volumes:
  media-organizer-data:
```

Start:

```bash
docker compose up -d
```

Health check:

```bash
curl http://localhost:45263/health
```

## API endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/` | API overview |
| GET | `/health` | Health check |
| GET | `/storage-info` | Disk storage info for the destination folder |
| POST | `/trigger-job` | Trigger organize job immediately |
| POST | `/forget-show-season` | Delete move history for a show/season |
| GET | `/openapi/v1.json` | OpenAPI spec |
| GET | `/scalar/v1` | Scalar API UI |

Trigger now:

```bash
curl -X POST http://localhost:45263/trigger-job
```

Trigger with source override:

```bash
curl -X POST http://localhost:45263/trigger-job \
  -H "Content-Type: application/json" \
  -d '{"folderPath":"/media/incoming"}'
```

Storage info:

```bash
curl http://localhost:45263/storage-info
```

## Organize behavior (summary)

1. Resolve source folder from request override or config
2. Discover allowed video files recursively
3. Parse/group titles (SxxExx, trailing episode patterns, movies)
4. Build move plan against history DB (idempotent)
5. Move videos with unique destination handling (`name (1).ext`, etc.)
6. Move matched subtitle files next to videos
7. Clean leftover source directories

### Destination rules

Movie:

```text
{root}/{Name}/{Name}.ext
```

Show:

```text
{root}/{Name}/Season {NN}/{originalFileName}.ext
```

Examples:

- `Interstellar.2014.mp4` → `Interstellar 2014/Interstellar 2014.mp4`
- `The.Office.S02E03.Health.Care.mkv` → `The Office/Season 02/The.Office.S02E03.Health.Care.mkv`
- `[SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv` → `Jujutsu Kaisen/Season 01/[SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv`

## Configuration

Settings are under `MediaOrganizer` in `appsettings.json` or environment variables (`__` separator).

| Key | Default | Description |
|---|---|---|
| `SourceFolder` | `null` | Source root to scan |
| `DestinationFolder` | `null` (falls back to source) | Organized output root |
| `MoveHistoryDatabasePath` | `data/move-history.db` | SQLite history DB path |
| `VideoExtensions` | `.mp4,.mkv,.avi,.mov,.wmv,.m4v,.webm,.ts,.mpg,.mpeg` | Allowed video extensions |
| `SubtitleExtensions` | `.srt,.sub,.ass,.ssa,.vtt,.idx` | Allowed subtitle extensions |

**Docker-only environment variables** (not part of `MediaOrganizer` config section):

| Variable | Default | Description |
|---|---|---|
| `PUID` | `1000` | User ID the service runs as inside the container |
| `PGID` | `1000` | Group ID the service runs as inside the container |

Set `PUID`/`PGID` to the UID/GID of the host user that owns your media files (run `id` on your host to find the values). This ensures all moved files keep the correct ownership so they are not locked when accessed over SMB.

Example:

```json
{
  "MediaOrganizer": {
    "SourceFolder": "/media/source",
    "DestinationFolder": "/media/destination",
    "MoveHistoryDatabasePath": "/data/move-history.db",
    "VideoExtensions": [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v", ".webm", ".ts", ".mpg", ".mpeg"],
    "SubtitleExtensions": [".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"]
  }
}
```

## Development

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download)

Run service:

```bash
dotnet run --project src/MediaOrganizer/MediaOrganizer.csproj
```

Run tests:

```bash
dotnet test
```

Build:

```bash
dotnet build src/MediaOrganizer/MediaOrganizer.csproj
```

## Repo layout

```text
src/
  MediaOrganizer/          # Worker + minimal API
  MediaOrganizer.Tests/    # Unit tests
  MediaOrganizer.App/      # Flutter app (mobile/desktop/web client)
```

## Troubleshooting

| Problem | Check |
|---|---|
| Service not reachable | Port mapping/firewall for `45263` |
| Files skipped | Source path exists and extension lists are correct |
| Duplicate names | Expected behavior; unique suffix is applied |
| Moved files locked / can't delete via SMB | Container is running as root; set `PUID`/`PGID` env vars to match the host user that owns your media files (run `id` on the host) |
