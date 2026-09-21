# MediaOrganizer

MediaOrganizer is a .NET 10 minimal API service that organizes messy video folders into a clean movie/TV library layout which is supported by Jellyfin.

It supports on-demand API triggers, subtitle companion moves, source cleanup, and idempotent move tracking using a SQLite move-history database.

## What this repository contains

- `src/MediaOrganizer`: backend service (minimal API)
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
- Torrent downloads via qBittorrent (`.torrent` files and magnet links) with a progress listing
- Live log streaming via Server-Sent Events (SSE)
- OpenAPI document + Scalar docs UI
- Docker-ready deployment
- Flutter companion app (mobile/desktop/web)

## Quick start (Docker)

Runs MediaOrganizer together with qBittorrent so `.torrent` files and magnet links can be
downloaded straight into your media folder.

The important detail: **both services mount the same host folder at the same container path**
(`/media`). qBittorrent resolves its save path in its own namespace, so identical mounts are
what make downloads land where the organizer scans. Keep source and destination under one
mount as well, so moves stay fast renames on a single filesystem.

Replace `/media/bram/Expansion/Videos` with the host folder holding your videos.

```yaml
services:
  media-organizer:
    image: ghcr.io/bramvanelderen10/mediaorganizer:0.0.13
    container_name: media-organizer
    ports:
      - "45263:45263"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - TZ=Europe/Amsterdam
      - MediaOrganizer__SourceFolder=/media
      - MediaOrganizer__MoveHistoryDatabasePath=/data/move-history.db
      # Torrent integration (see "Adding torrents" below)
      - MediaOrganizer__Qbittorrent__Url=http://qbittorrent:8488
      - MediaOrganizer__Qbittorrent__Username=admin
      - MediaOrganizer__Qbittorrent__Password=your-webui-password
      - MediaOrganizer__Qbittorrent__DownloadFolder=/media
      # Match PUID/PGID to the host user that owns your media files, otherwise moved
      # files end up root-owned and get locked over SMB. Run `id` on your host.
      - PUID=1000
      - PGID=1000
    restart: unless-stopped
    volumes:
      - /media/bram/Expansion/Videos:/media
      # Binds the history DB next to the compose file, so backing up ./data is enough.
      - ./data:/data
    depends_on:
      - qbittorrent

  qbittorrent:
    image: lscr.io/linuxserver/qbittorrent:latest
    container_name: qbittorrent
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Amsterdam
      - WEBUI_PORT=8488        # must match both sides of the port mapping below
      - TORRENTING_PORT=6881
    ports:
      # Exposes the WebUI on your LAN. Use "127.0.0.1:8488:8488" to keep it local
      # (MediaOrganizer still reaches it over the compose network).
      - "8488:8488"
      - "6881:6881"
      - "6881:6881/udp"
    volumes:
      # MUST be identical to the media-organizer mount above.
      - /media/bram/Expansion/Videos:/media
      - qbittorrent-config:/config
    restart: unless-stopped

volumes:
  qbittorrent-config:
```

Start and verify:

```bash
docker compose up -d
curl http://localhost:45263/health

# First run only: read the temporary qBittorrent password, then log in at
# http://<host>:8488 and set a permanent one (Tools -> Options -> WebUI -> Authentication).
docker compose logs qbittorrent
```

Set that permanent password as `MediaOrganizer__Qbittorrent__Password`. If you skip this,
qBittorrent generates a new password on every restart and the integration breaks.

> Prefer your own scheduler? Drop the `qbittorrent` service and the `Qbittorrent` env vars.
> Torrent endpoints then return `503` and the rest of the service works unchanged.

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

Returns `{ "count": N, "torrents": [...] }`. Each entry has `hash`, `name`, `state`
(qBittorrent's raw code), `status` (human readable label derived from it), `progress` (ratio
`0..1`), `sizeBytes`, `downloadedBytes`, `amountLeftBytes`, `downloadSpeed`, `uploadSpeed`,
`etaSeconds` (`null` when unknown, e.g. while seeding or stalled), `savePath` and
`addedOnUnixSeconds`.

Responses for the add endpoints:

| Status | Meaning |
|---|---|
| `200` | Torrent accepted and download started, or list returned |
| `400` | Missing/invalid `.torrent`, invalid magnet link, or invalid `folderPath` |
| `502` | qBittorrent rejected the request (invalid torrent, duplicate torrent, bad credentials, or unreachable) |
| `503` | qBittorrent is not configured |

### Download folder

`Qbittorrent:DownloadFolder` is the save path sent to qBittorrent. When empty it falls back to
`SourceFolder`, so downloads land directly in the mounted volume by default.

> **Never run `POST /trigger-job` while a torrent is downloading.** Downloads go into the
> source folder, and the organizer deletes directory trees that contain no recognized
> video/subtitle file. qBittorrent names in-progress files with a `.!qB` suffix, which is not
> a recognized extension — so an unfinished download could be deleted.

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
| `Qbittorrent:Password` | `null` | qBittorrent WebUI password (set a permanent one; see Quick start) |
| `Qbittorrent:DownloadFolder` | `null` (falls back to `SourceFolder`) | Save path sent to qBittorrent, in qBittorrent's own namespace |
| `Qbittorrent:Category` | `null` | Optional category applied to added torrents |
| `Qbittorrent:Tags` | `null` | Optional comma-separated tags applied to added torrents |
| `Qbittorrent:RequestTimeoutSeconds` | `60` | Timeout for qBittorrent HTTP calls |

**Docker-only environment variables** (handled by `entrypoint.sh`, not part of the
`MediaOrganizer` config section):

| Variable | Default | Description |
|---|---|---|
| `PUID` | `1000` | User ID the service runs as inside the container |
| `PGID` | `1000` | Group ID the service runs as inside the container |

Set `PUID`/`PGID` to the UID/GID of the host user that owns your media files (run `id` on your
host). This keeps moved files correctly owned so they are not locked over SMB.

Example `appsettings.json` (the Docker example above uses environment variables instead):

```json
{
  "MediaOrganizer": {
    "SourceFolder": "/media",
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

> `DestinationFolder` is omitted above because it defaults to `SourceFolder`. Set it only if
> you want organized output somewhere else — for example `SourceFolder=/media/incoming` and
> `DestinationFolder=/media`.

### Repo `docker-compose.yml` vs. this guide

The committed [`docker-compose.yml`](docker-compose.yml) uses `/path/to/your/videos` as a
placeholder for your media folder, `change-me` as the qBittorrent password, the moving `:main`
image tag, and binds the qBittorrent WebUI to `127.0.0.1` only. Replace the placeholders, pin a
version tag, and switch to `"8488:8488"` if you want LAN access to the WebUI.

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
| Moved files locked / can't delete via SMB | Container is running as root; set `PUID`/`PGID` to match the host user that owns your media files (run `id` on the host) |
| Torrent endpoints return `503` | `MediaOrganizer__Qbittorrent__Url` is set and qBittorrent is reachable from the container |
| Torrent endpoints return `502` | Check `docker compose logs media-organizer`; usually bad credentials or qBittorrent rejecting the torrent |
| qBittorrent login fails after a restart | The temporary password changed. Set a permanent one in **Tools → Options → WebUI → Authentication** and update `Qbittorrent__Password` |
| Downloads land in the wrong place | `Qbittorrent__DownloadFolder` must be a path **qBittorrent** sees, and both services must mount the same host folder at the same container path |
| An unfinished download disappeared | The organize job ran mid-download; see the warning under "Download folder" |
