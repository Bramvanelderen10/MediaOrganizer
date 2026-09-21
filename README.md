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
- File management API (browse, rename, move, delete)
- Move history management (forget movies, shows, seasons, episodes)
- Organized media library view
- Live log streaming via Server-Sent Events (SSE)
- OpenAPI document + Scalar docs UI
- Docker-ready deployment
- Flutter companion app (mobile/desktop/web)

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

### System

| Method | Path | Description |
|---|---|---|
| GET | `/` | API overview |
| GET | `/health` | Health check with timestamp |
| GET | `/storage-info` | Disk storage info (total, used, free bytes) for the destination folder |
| GET | `/logs/stream` | Live log streaming via SSE (query: `?tail=200`) |
| GET | `/openapi/v1.json` | OpenAPI spec |
| GET | `/scalar/v1` | Scalar API docs UI |

### Job execution

| Method | Path | Description |
|---|---|---|
| POST | `/trigger-job` | Trigger organize job immediately (optional `folderPath` body) |

### Torrents

| Method | Path | Description |
|---|---|---|
| GET | `/torrents` | List all torrents with progress, speed, ETA and state |
| POST | `/torrents/add` | Upload a `.torrent` file and start the download |
| POST | `/torrents/add-magnet` | Add a magnet link and start the download |

### File management

| Method | Path | Description |
|---|---|---|
| GET | `/browse` | List directory contents under the source folder (query: `?path=sub/dir`) |
| POST | `/rename` | Rename a file or directory under the source folder |
| POST | `/move` | Move a file or directory to a different folder under the source root |
| POST | `/delete` | Delete one or more files or directories under the source folder |

### History management

| Method | Path | Description |
|---|---|---|
| GET | `/library` | Organized media library structure built from move history |
| POST | `/forget-movie` | Delete move history entries for a specific movie |
| POST | `/forget-show` | Delete all move history entries for a show (all seasons) |
| POST | `/forget-show-season` | Delete move history entries for a specific show season |
| POST | `/forget-episode` | Delete the move history entry for a specific episode |
| POST | `/forget-batch` | Delete move history entries for multiple items at once |

### Examples

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

Browse source folder:

```bash
curl http://localhost:45263/browse
curl "http://localhost:45263/browse?path=subfolder"
```

Library overview:

```bash
curl http://localhost:45263/library
```

## Adding torrents

There are two ways to add a download: a `.torrent` file upload or a magnet link. Both hand
the torrent to qBittorrent, which starts downloading it immediately. The organize job is
**not** triggered; downloaded files are organized the next time the job is run
(`POST /trigger-job`).

### From a .torrent file

```bash
curl -X POST http://localhost:45263/torrents/add \
  -F "file=@/path/to/movie.torrent"
```

### From a magnet link

```bash
curl -X POST http://localhost:45263/torrents/add-magnet \
  -H "Content-Type: application/json" \
  -d '{"magnetLink":"magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=Big+Buck+Bunny"}'
```

http(s) URLs pointing at a `.torrent` file are accepted as well. A magnet link must contain a
BitTorrent info hash (`xt=urn:btih:`).

Optionally override the download folder for a single request (must be an absolute path that
the qBittorrent container can see):

```bash
curl -X POST http://localhost:45263/torrents/add \
  -F "file=@/path/to/movie.torrent" \
  -F "folderPath=/media"
```

### Listing torrents and progress

```bash
curl http://localhost:45263/torrents
```

```json
{
  "count": 1,
  "torrents": [
    {
      "hash": "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c",
      "name": "Big Buck Bunny",
      "state": "downloading",
      "status": "Downloading",
      "progress": 0.42,
      "sizeBytes": 276445467,
      "downloadedBytes": 116107096,
      "amountLeftBytes": 160338371,
      "downloadSpeed": 1048576,
      "uploadSpeed": 2048,
      "etaSeconds": 153,
      "savePath": "/media",
      "addedOnUnixSeconds": 1790020976
    }
  ]
}
```

`progress` is a ratio between `0` and `1`. `etaSeconds` is `null` when the ETA is unknown
(for example while seeding or stalled). `state` is qBittorrent's raw state code, while
`status` is a human readable label derived from it.

Responses:

| Status | Meaning |
|---|---|
| `200` | Torrent accepted and download started, or list returned |
| `400` | Missing/invalid `.torrent`, invalid magnet link, or invalid `folderPath` |
| `502` | qBittorrent rejected the request (invalid torrent, duplicate torrent, bad credentials, or unreachable) |
| `503` | qBittorrent is not configured |

### Download folder and the shared mount

qBittorrent resolves `savepath` in **its own** filesystem namespace, so for downloads to land
in the folder MediaOrganizer scans, mount the **same host folder at the same container path**
in both services (as `docker-compose.yml` does with `/path/to/your/videos:/media`).

`MediaOrganizer__Qbittorrent__DownloadFolder` is the save path sent to qBittorrent. When it is
left empty, MediaOrganizer falls back to `MediaOrganizer:SourceFolder`, so out of the box
downloads land directly in the mounted volume.

> **Note:** because downloads go straight into the source folder, a file that is still
> downloading is visible to the organize job. The organizer deletes directory trees that
> contain no recognized video/subtitle file, and qBittorrent names in-progress files with a
> `.!qB` suffix — so triggering the job mid-download could remove an unfinished download.
> **Only run `POST /trigger-job` when no torrent is actively downloading.**

### First-time qBittorrent setup

On first start the LinuxServer image prints a temporary `admin` password to its container log:

```bash
docker logs qbittorrent
```

Log in at `http://<host>:8488` (the compose file binds the WebUI to localhost, so use an
SSH tunnel from another machine: `ssh -L 8488:localhost:8488 user@server`), change the
password in **Tools → Options → WebUI → Authentication**, and put that permanent password in
`MediaOrganizer__Qbittorrent__Password`. If you do not change it, a new password is generated
on every container start.
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
| `Qbittorrent:Url` | `null` | qBittorrent WebUI base URL (e.g. `http://qbittorrent:8488`). Torrent endpoints return `503` when empty |
| `Qbittorrent:Username` | `null` | qBittorrent WebUI username |
| `Qbittorrent:Password` | `null` | qBittorrent WebUI password |
| `Qbittorrent:DownloadFolder` | `null` (falls back to `SourceFolder`) | Save path sent to qBittorrent |
| `Qbittorrent:Category` | `null` | Optional category applied to added torrents |
| `Qbittorrent:Tags` | `null` | Optional comma-separated tags applied to added torrents |
| `Qbittorrent:RequestTimeoutSeconds` | `60` | Timeout for qBittorrent login/add HTTP calls |

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
    "SubtitleExtensions": [".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx"],
    "Qbittorrent": {
      "Url": "http://qbittorrent:8488",
      "Username": "admin",
      "Password": "your-webui-password",
      "DownloadFolder": "/media",
      "Category": "",
      "Tags": "",
      "RequestTimeoutSeconds": 60
    }
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
  MediaOrganizer/          # Backend service (minimal API)
  MediaOrganizer.Tests/    # Unit tests
  MediaOrganizer.App/      # Flutter companion app (mobile/desktop/web client)
tools/
  mcreate/                 # CLI tool to recreate folder structures with empty files
```

## Troubleshooting

| Problem | Check |
|---|---|
| Service not reachable | Port mapping/firewall for `45263` |
| Files skipped | Source path exists and extension lists are correct |
| Duplicate names | Expected behavior; unique suffix is applied |
| Moved files locked / can't delete via SMB | Container is running as root; set `PUID`/`PGID` env vars to match the host user that owns your media files (run `id` on the host) |
