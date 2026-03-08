# Scheduled Job Application

.NET 10 worker service with scheduled job execution and HTTP trigger support.

## Project Type
- Worker Service with ASP.NET Core Minimal API
- Scheduled background tasks
- Docker deployment ready

## Technology Stack
- .NET 10
- ASP.NET Core Minimal API
- BackgroundService for scheduling
- Microsoft.AspNetCore.OpenApi (built-in OpenAPI document generation)
- Scalar.AspNetCore (interactive API documentation UI)
- NCrontab (cron scheduling)
- Docker

## Completed Steps
✅ Project structure created
✅ VS Code debug configuration (.vscode/launch.json, tasks.json)
✅ OpenAPI spec generation via built-in Microsoft.AspNetCore.OpenApi
✅ Scalar interactive API reference UI at /scalar

## File Move Algorithm (Current Behavior)

1. Resolve source folder
	- Use request override when provided, otherwise `MediaOrganizer:SourceFolder`.
	- Fail if folder is empty/missing or does not exist.

2. Resolve allowed video extensions
	- Use configured `MediaOrganizer:VideoExtensions` when present.
	- Fall back to defaults:
	  - `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, `.ts`, `.mpg`, `.mpeg`
	- Normalize extension matching (case-insensitive, with leading `.`).

3. Discover input files recursively
	- Scan all files under source root (`SearchOption.AllDirectories`).
	- Keep only files with allowed extension.

4. Build move plan per file
	- Compute destination from filename using title parser:
	  - TV format detected with regex token `SxxExx` (case-insensitive).
	- Anime-style format detected when name includes `Title - <episodeNumber>` (optional leading fansub tag in `[]`).
	  - Otherwise treat as movie.

5. TV episode destination rules
	- Extract season + episode from `SxxExx`.
	- Show title = text before token, cleaned (`.`, `_`, `-` -> spaces, collapse spaces).
	- Episode title = text after token (or before token if after is empty), cleaned.
	- Destination path:
	  - `{root}/{Show Title}/Season {season:00}/{Episode Title or Show Title + SxxExx}.ext`
	- Example:
	  - `The.Office.S02E03.Health.Care.mkv`
	  - -> `{root}/The Office/Season 02/Health Care S02E03.mkv`

6. Movie destination rules
	- Movie title = cleaned filename without extension.
	- Destination path:
	  - `{root}/{Movie Title}/{Movie Title}.ext`
	- Example:
	  - `Interstellar.2014.mp4`
	  - -> `{root}/Interstellar 2014/Interstellar 2014.mp4`

7. Skip no-op moves
	- If source equals destination (case-insensitive), do not move.

### Anime-style override (no explicit season token)

- Input examples:
	- `[SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv`
	- `[Cleo]91 Days 01 (Dual Audio 10bit BD1080p x265).mkv`
- Detection:
	- Optional leading tag in brackets (e.g. `[SubsPlease]`)
	- Show title before ` - `, or before trailing episode number when no dash is used
	- Episode number immediately after ` - ` or at the end of the title block (before metadata like `(...)`/`[...]`)
	- If filename does not match, try parent folder name as fallback source for this pattern
- Destination path:
	- `{root}/{Show Title}/Season 01/Episode {episode} S01E{episode:00}.ext`
- Example output:
	- `{root}/Jujutsu Kaisen/Season 01/Episode 56 S01E56.mkv`
	- `{root}/91 Days/Season 01/Episode 1 S01E01.mkv`

8. Execute move plan
	- Ensure destination directory exists.
	- If destination file already exists, append numeric suffix:
	  - `name.ext` -> `name (1).ext`, `name (2).ext`, ...
	- Move file and log source/destination.

9. Return summary
	- `TotalFiles` = discovered video files
	- `MovedFiles` = successful moves
	- `SkippedFiles` = `TotalFiles - plannedMoves`
