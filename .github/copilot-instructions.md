# MediaOrganizer

.NET 10 minimal API service that organizes video and subtitle files from a flat/messy source folder into a structured media library (movies and TV shows). The organize job runs **on demand only** (`POST /trigger-job`) — there is no scheduler. Exposes HTTP endpoints for on-demand triggering, file management, history management, torrent uploading, and health checks. Uses SQLite to track move history for idempotency. Includes a Flutter companion app.

## Project Type
- ASP.NET Core Minimal API with a hosted Kestrel listener
- On-demand organize job (no scheduler/cron)
- SQLite move-history database for idempotent moves
- Docker deployment ready
- Flutter companion app (mobile/desktop/web)

## Technology Stack
- .NET 10
- ASP.NET Core Minimal API (port 45263)
- Entity Framework Core with SQLite (move history)
- Microsoft.AspNetCore.OpenApi (built-in OpenAPI document generation)
- Scalar.AspNetCore (interactive API documentation UI)
- IHttpClientFactory / HttpClient (qBittorrent WebUI integration)
- Docker (multi-stage build with gosu for privilege de-escalation)

## Configuration (`MediaOrganizer` section)

| Key | Type | Default | Description |
|---|---|---|---|
| `SourceFolder` | `string?` | `null` | Root folder to scan for unorganized media |
| `DestinationFolder` | `string?` | `null` | Root folder for organized output (falls back to `SourceFolder` if null) |
| `MoveHistoryDatabasePath` | `string` | `"data/move-history.db"` | SQLite database file path |
| `VideoExtensions` | `string[]` | `.mp4,.mkv,.avi,.mov,.wmv,.m4v,.webm,.ts,.mpg,.mpeg` | Allowed video file extensions |
| `SubtitleExtensions` | `string[]` | `.srt,.sub,.ass,.ssa,.vtt,.idx` | Allowed subtitle file extensions |
| `Qbittorrent:Url` | `string?` | `null` | qBittorrent WebUI base URL; torrent endpoints return 503 when unset |
| `Qbittorrent:Username` | `string?` | `null` | qBittorrent WebUI username |
| `Qbittorrent:Password` | `string?` | `null` | qBittorrent WebUI password |
| `Qbittorrent:DownloadFolder` | `string?` | `null` | Save path sent to qBittorrent (falls back to `SourceFolder`) |
| `Qbittorrent:Category` | `string?` | `null` | Optional category for added torrents |
| `Qbittorrent:Tags` | `string?` | `null` | Optional comma-separated tags for added torrents |
| `Qbittorrent:RequestTimeoutSeconds` | `int` | `60` | Timeout for qBittorrent HTTP calls |

## API Endpoints

### System

| Method | Path | Description |
|---|---|---|
| `GET` | `/` | API overview with available endpoints |
| `GET` | `/health` | Health check with timestamp |
| `GET` | `/storage-info` | Disk storage info (total, used, free bytes) for the destination folder |
| `GET` | `/logs/stream` | Live log streaming via Server-Sent Events (query: `?tail=200`) |
| `GET` | `/openapi/v1.json` | OpenAPI spec |
| `GET` | `/scalar/v1` | Scalar interactive API documentation |

### Job execution

| Method | Path | Description |
|---|---|---|
| `POST` | `/trigger-job` | Trigger organize pipeline immediately (optional `folderPath` body) |

### Torrents

| Method | Path | Description |
|---|---|---|
| `POST` | `/torrents/add` | Upload a `.torrent` file (multipart field `file`) and start downloading it via qBittorrent (optional `folderPath` form field). Does not trigger the organize job |

### File management

| Method | Path | Description |
|---|---|---|
| `GET` | `/browse` | List directory contents under the source folder (query: `?path=sub/dir`) |
| `POST` | `/rename` | Rename a file or directory under the source folder |
| `POST` | `/move` | Move a file or directory to a different folder under the source root |
| `POST` | `/delete` | Delete one or more files or directories under the source folder |

### History management

| Method | Path | Description |
|---|---|---|
| `GET` | `/library` | Organized media library structure built from move history |
| `POST` | `/forget-movie` | Delete move history entries for a specific movie |
| `POST` | `/forget-show` | Delete all move history entries for a show (all seasons) |
| `POST` | `/forget-show-season` | Delete move history entries for a specific show season |
| `POST` | `/forget-episode` | Delete the move history entry for a specific episode |
| `POST` | `/forget-batch` | Delete move history entries for multiple items at once |

## Architecture

All services are registered as **singletons** via DI. Key components:

| Namespace | Class | Role |
|---|---|---|
| Orchestration | `JobExecutor` | Wraps `MediaFileOrganizer` with logging |
| Orchestration | `MediaFileOrganizer` | Main orchestrator for the organize flow |
| Discovery | `VideoFileFinder` | Recursive file discovery, filters by extension |
| Parsing | `MediaGrouper` | Parses filenames, groups by title similarity (Levenshtein ≥ 0.80) |
| Planning | `MovePlanBuilder` | Builds move plan against history DB, computes destinations |
| Execution | `VideoMover` | Executes file moves with unique-path de-duplication |
| Execution | `SubtitleMover` | Moves companion subtitles alongside their video |
| Cleanup | `DirectoryCleaner` | Removes empty/leftover directories after moves |
| History | `MoveHistoryStore` | CRUD on SQLite move history (via `IDbContextFactory`) |
| Logging | `LogBroadcaster` | Pub/sub broker for live log streaming via SSE |
| Logging | `BroadcastLoggerProvider` | Custom `ILoggerProvider` that publishes logs to `LogBroadcaster` |
| Helpers | `IFileSystem` / `PhysicalFileSystem` | File system abstraction for testability |
| Torrents | `TorrentService` | Validates uploaded `.torrent` files and resolves the download folder |
| Torrents | `QbittorrentClient` | qBittorrent WebUI API v2 client (login + `torrents/add`) |
| Helpers | `PathHelpers` | Unique path generation (`name (1).ext`, `name (2).ext`, ...) |

## Organize Flow (`MediaFileOrganizer`)

1. **Resolve source folder** — use HTTP request override or `SourceFolder` from config; fail if missing or non-existent.
2. **Resolve extensions** — video + subtitle extensions from config or defaults.
3. **Discover video files** — `VideoFileFinder` scans recursively, filters by extension, auto-deletes hidden trash files (`.`-prefixed names containing "trash").
4. **Group & parse** — `MediaGrouper` parses filenames, extracts season/episode info, groups by title similarity.
5. **Build move plan** — `MovePlanBuilder` compares against history DB, creates new `MoveHistoryEntry` records for files needing moves.
6. **Get pending entries** — `MoveHistoryStore` returns all entries with `IsMoved = false`.
7. **Execute moves** — `VideoMover` moves files, ensures unique paths, updates history records.
8. **Move subtitles** — `SubtitleMover` finds and moves subtitle files alongside their video.
9. **Cleanup directories** — `DirectoryCleaner` removes directory subtrees that no longer contain media.
10. **Return summary** — total files, moved count, skipped count, subtitles moved, leftover files removed.

## Filename Parsing Algorithm (`MediaGrouper`)

### Cleaning
- Strip bracket tags `[...]` and `(...)`, resolution tags (1080p, etc.), codec tags (x265, etc.), release group tags (YIFY, BluRay, etc.).
- Replace `.`, `_`, `-` with spaces; collapse whitespace.

### Episode Detection (in priority order)
1. **SxxExx regex** — if matched, extract season + episode, title = text before the match. If title is empty, fall back to parent folder name.
2. **SxxDashExx / Sxx space xx** — season–episode pairs separated by a dash or whitespace without the `E` prefix on the episode number.
3. **Trailing episode number** (`\s+(?<episode>\d{1,4})\s*$`) — episode number from end, title = text before it.
4. **No match** — no episode info, title = full cleaned name.

Parent folder name is also parsed as a fallback source for the pattern. If the immediate parent is a `Season XX` folder, the grandparent folder name is used instead.

### Pre-grouping: Folder-based Title Override
- For files sharing a parent folder + season (≥2 files): if no title is similar (≥0.80) to ≥50% of the group, all titles are overridden with the parent folder name.
- Handles filenames with episode descriptions instead of show names (e.g. `ShowName/Episode One S01E01.mkv`).

### Grouping
- Pairwise Levenshtein distance similarity with threshold **≥ 0.80** (80%).
- Files with similar titles are grouped together.

### MediaObject Construction
- Single file, no SxxExx → **Movie** (uses cleaned name, preserves trailing years).
- Multiple files OR any file with SxxExx → **Show**.

### Season Building
- Files bucketed by season number (default to season 1 if no explicit season).
- Files without episode numbers are assigned sequential episode numbers after the max existing episode, sorted alphabetically.

### Canonical Name Selection
- Prefer clean parent folder name if it matches ≥ 50% of the group's titles.
- Otherwise: most common title in the group, tie-break by longest name.

## Destination Path Rules

### Movie
- Path: `{root}/{Name}/{Name}.ext`
- Example: `Interstellar.2014.mp4` → `{root}/Interstellar 2014/Interstellar 2014.mp4`

### TV Show
- If original filename contains the show name: `{root}/{Name}/Season {NN}/{originalFileName}.ext`
- If show name is missing from filename: `{root}/{Name}/Season {NN}/{Name} S{NN}E{EE}.ext`
- Example (name present): `The.Office.S02E03.Health.Care.mkv` → `{root}/The Office/Season 02/The.Office.S02E03.Health.Care.mkv`
- Example (name missing): `S02E06-The Beauty Makes Her Move.mkv` (show: ThatTimeIGotReincarnatedAsASlime) → `{root}/ThatTimeIGotReincarnatedAsASlime/Season 02/ThatTimeIGotReincarnatedAsASlime S02E06.mkv`

### Anime-style (trailing episode number, no SxxExx)
- Input examples:
	- `[SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv`
	- `[Cleo]91 Days 01 (Dual Audio 10bit BD1080p x265).mkv`
- Grouped as a show with season 1, assigned episode numbers.
- Path: `{root}/{Show Title}/Season 01/{originalFileName}.ext`

## Move History & Idempotency (`MoveHistoryStore`)

- Each move is tracked as a `MoveHistoryEntry` with `UniqueKey`, `OriginalFilePath`, `TargetFilePath`, `MoveDateTime`, `IsMoved`.
- **UniqueKey**: for movies = media name; for shows = `{Name}_Season{NN}_Episode{EE}`.
- Before creating a plan entry, the builder checks the latest history record by `UniqueKey`:
	- Same destination + `IsMoved = true` → skip (already done).
	- Same destination + `IsMoved = false` → already pending, no action.
	- Different destination → create new record.
	- No record → create new record.
- Database indexes on `UniqueKey`, `IsMoved`, and composite `(UniqueKey, Id)` (descending).

## Subtitle Handling (`SubtitleMover`)

1. Group moved videos by original parent directory.
2. For each directory, find all subtitle files (recursive for subdirs, top-only for source root).
3. Match each subtitle to the best video:
	- If only one video in the group → use it.
	- Otherwise try **SxxExx matching** (checks subtitle filename, then walks parent directory names).
	- Fallback: **longest common prefix** of normalized names.
4. Prefix subtitle filename with the video stem unless it already contains it.
5. Ensure unique path, create directory, move file.

## Directory Cleanup (`DirectoryCleaner`)

### `CleanupDirectoriesWithoutMedia` (used by the organize flow)
- Enumerate all directories under the source root with a non-recursive stack, skipping symlinks.
- Compute bottom-up whether a directory contains media (video/subtitle) directly or in any descendant.
- Delete directories (deepest first) that have no media in their subtree; on failure it falls back to a
  recursive delete, so files inside a removed directory are deleted as well.

> **Important:** a directory containing only in-progress download files is treated as having no media and
> is deleted. qBittorrent appends a `.!qB` suffix to incomplete files, which is not a recognized media
> extension — so never run the organize job while a torrent is downloading.

**Safety**: symlinks are never deleted; enumeration errors default to "keep" behavior.

> `CleanupSourceDirectories(IEnumerable<string> movedSourcePaths, string sourceRoot)` also exists on the
> class but is currently unreferenced (dead code).

## Key Patterns

| Pattern | Usage |
|---|---|
| Options pattern | `MediaOrganizerOptions` bound from `MediaOrganizer` config section |
| DI / Singleton | All services registered as singletons |
| `IDbContextFactory` | Thread-safe EF Core usage from singletons |
| `IFileSystem` abstraction | All file operations go through `IFileSystem` for testability |
| Record types | `ParsedVideoFile`, `Episode`, `MovePlanItem`, `MovedFileInfo`, summaries |
| Levenshtein similarity | Title grouping with 80% threshold |
| Idempotent moves | History DB tracks `UniqueKey` + `TargetPath` to skip already-moved files |
| SSE log streaming | `LogBroadcaster` + `BroadcastLoggerProvider` for live log delivery |

## Testing

Tests in `src/MediaOrganizer.Tests/` mock `IFileSystem` and `ITorrentClient` for isolated unit testing:
- `DirectoryCleanerTests`, `MediaGrouperTests`, `MovePlanBuilderTests`
- `PathHelpersTests`, `SubtitleMoverTests`, `TorrentServiceTests`
- `VideoFileFinderTests`, `VideoMoverTests`

> Note: `MediaFileRestorerTests` is stale — it targets a `MediaFileRestorer` class that no longer exists,
> so the test project currently fails to compile until it is deleted or rewritten.
