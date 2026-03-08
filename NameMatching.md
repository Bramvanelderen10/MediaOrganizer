# Name Matching & Media Organization Algorithm

## Overview

The MediaGrouper takes a flat list of video file paths and produces structured `MediaObject` results, classifying files as **Movies** or **Shows** with season/episode information. The results are then used by the `MovePlanBuilder` to determine destination paths, and finally the `MediaFileOrganizer` executes the moves.

---

## Step 1 — File Discovery

`VideoFileFinder` scans the source folder recursively and keeps only files with an allowed video extension (case-insensitive).

**Default extensions:** `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, `.ts`, `.mpg`, `.mpeg`

Extensions are configurable via `MediaOrganizer:VideoExtensions` in appsettings.

---

## Step 2 — Filename Cleaning

Each filename is cleaned by stripping noise in this order:

1. **Bracketed content** — anything inside `[…]` or `(…)` is removed
2. **Resolution tags** — `2160p`, `1080p`, `720p`, `480p`, `360p` (case-insensitive)
3. **Codec tags** — `x264`, `x265`, `h.264`, `h.265`, `HEVC`, `AVC`, `AAC`, `DTS`, `FLAC`, `10bit`, `8bit`
4. **Release group / source tags** — `ELiTE`, `YIFY`, `RARBG`, `FGT`, `LOL`, `ETTV`, `EZTVx`, `SubsPlease`, `BluRay`, `BRRip`, `WEBRip`, `WEB-DL`, `HDRip`, `DVDRip`, `Dual Audio`, `PROPER`, `REPACK`, `INTERNAL`
5. **Separator normalization** — dots (`.`), underscores (`_`), and dashes (`-`) are replaced with spaces
6. **Whitespace collapse** — multiple spaces are collapsed to a single space, then trimmed

The parent folder name is also cleaned using the same pipeline for later use in canonical name selection.

---

## Step 3 — Episode & Season Detection

After cleaning, the algorithm tries to extract season/episode information in priority order:

### 3a. SxxExx pattern (highest priority)
Regex: `\bS(?<season>\d{1,2})\s*E(?<episode>\d{1,4})\b` (case-insensitive)

- **Title** = cleaned text before the `SxxExx` token
- **Season** = captured season number
- **Episode** = captured episode number

Example: `Dark Matter 1080p x265 ELiTE[EZTVx to] S01E07.mkv`
→ cleaned: `Dark Matter S01E07` → title: `Dark Matter`, season: `1`, episode: `7`

### 3b. Trailing episode number (fallback)
Regex: `\s+(?<episode>\d{1,4})\s*$`

- **Title** = cleaned text before the trailing number
- **Season** = none (defaults to `1` during grouping)
- **Episode** = the trailing number

Example: `[Ember] Jujutsu Kaisen 58.mkv`
→ cleaned: `Jujutsu Kaisen 58` → title: `Jujutsu Kaisen`, episode: `58`

### 3c. No episode info
If neither pattern matches, the full cleaned filename becomes the title with no season/episode data.

Example: `Taxi Driver.mkv` → title: `Taxi Driver`

---

## Step 4 — Fuzzy Grouping by Title Similarity

Parsed files are grouped using **Levenshtein distance** similarity with an **80% threshold**.

- Files are iterated; each unassigned file starts a new group
- All remaining unassigned files with a title similarity ≥ 80% are added to that group
- Exact matches (case-insensitive) always pass
- This handles typos, minor naming differences, and inconsistencies across files

Example: `Jujustu Kaisen` (typo) and `Jujutsu Kaisen` (correct) are ≈ 93% similar → grouped together.

---

## Step 5 — MediaObject Construction

Each group is classified:

| Condition | Classification |
|---|---|
| Single file **and** no `SxxExx` detected | **Movie** |
| Multiple files **or** any file has `SxxExx` | **Show** |

### Movie
- **Name** = full cleaned filename (including trailing numbers like year)
- **MoviePath** = original file path

### Show
- **Canonical name** is determined by:
  1. **Parent folder name preferred** — if a cleaned parent folder name matches ≥ 50% of the group's titles (by similarity), it wins. This picks up well-named folders even when filenames contain typos.
  2. **Most common title fallback** — the title appearing most often in the group, with longest name as tiebreaker.

#### Season bucketing
- Files with an explicit season number are placed in that season
- Files without a season number default to **Season 1**

#### Episode assignment
- Files that already have an episode number keep it
- Files without an episode number are sorted alphabetically by filename and assigned sequential episode numbers starting after the highest existing episode number in that season

---

## Step 6 — Move Plan & Destination Paths

The `MovePlanBuilder` resolves destination paths for each file:

### Movie destination
```
{root}/{Name}/{Name}.ext
```
Example: `Taxi Driver.mkv` → `{root}/Taxi Driver/Taxi Driver.mkv`

### Show destination
```
{root}/{ShowName}/Season {season:00}/{original-cleaned-filename}.ext
```
Example: `Dark Matter S01E07.mkv` → `{root}/Dark Matter/Season 01/Dark Matter S01E07.mkv`

### Move history database
Before executing moves, each file is checked against an SQLite move history database:

| History state | Action |
|---|---|
| No record exists | Create new entry (`IsMoved = false`) |
| Same destination, `IsMoved = true` | Skip (already moved) |
| Same destination, `IsMoved = false` | Keep existing record (pending move) |
| Different destination | Create new record with updated destination |

---

## Step 7 — Move Execution

The `MediaFileOrganizer` processes all entries with `IsMoved = false`:

1. **Skip missing source** — if the original file no longer exists, skip it
2. **Create destination directory** — `Directory.CreateDirectory` on the target folder
3. **Handle duplicates** — if the destination already exists, append a numeric suffix: `name (1).ext`, `name (2).ext`, etc.
4. **Move file** and mark as `IsMoved = true` in the database

### Restore support
A restore endpoint reverses all tracked moves (`IsMoved = true`), moving files back to their original paths.

---

## Full Example

### Input files
```
./[Ember] Jujutsu Kaisen 58.mkv
./Jujutsu Kaisen 59 [SubsPlease].mkv
./Jujutsu Kaisen/Jujutsu Kaisen 60.mkv
./Taxi Driver.mkv
./Dark Matter 1080p x265 ELiTE[EZTVx to] S01E07.mkv
./Dark Matter/Dark Matter 1080p x265 ELiTE[EZTVx to] S01E08.mkv
./Dark Matter/Season 01/Dark Matter S01E06.mkv
```

### Grouping result
| Group | Type | Name | Seasons |
|---|---|---|---|
| Jujutsu Kaisen | Show | `Jujutsu Kaisen` (from folder) | S01: Ep 58, 59, 60 |
| Taxi Driver | Movie | `Taxi Driver` | — |
| Dark Matter | Show | `Dark Matter` | S01: Ep 6, 7, 8 |

### Destination output
```
{root}/Jujutsu Kaisen/Season 01/Jujutsu Kaisen 58.mkv
{root}/Jujutsu Kaisen/Season 01/Jujutsu Kaisen 59.mkv
{root}/Jujutsu Kaisen/Season 01/Jujutsu Kaisen 60.mkv
{root}/Taxi Driver/Taxi Driver.mkv
{root}/Dark Matter/Season 01/Dark Matter S01E06.mkv
{root}/Dark Matter/Season 01/Dark Matter S01E07.mkv
{root}/Dark Matter/Season 01/Dark Matter S01E08.mkv
```

---

## Data Model

```
MediaObject
├── Name: string
├── Type: Movie | Show
├── MoviePath: string?          (set when Movie)
└── Seasons: List<Season>       (set when Show)
    ├── SeasonNumber: int
    └── Episodes: List<Episode>
        ├── Path: string
        └── EpisodeNumber: int
```