# Name Matching (Grouping) Algorithm

This document describes the exact filename parsing and grouping behavior implemented by `MediaGrouper`, and how that flows into destination path planning in `MovePlanBuilder`.

The overall goal is:

- Turn a messy, flat list of video file paths into structured `MediaObject` results
- Decide whether a group represents a **Movie** or a **Show**
- For shows, assign every file a `(Season, Episode)` so the move history can be idempotent

> Important: the algorithm is intentionally “best effort”. It aims to work well for typical scene/release naming, but some ambiguous inputs can still mis-group (see “Edge cases”).

---

## High-level pipeline

1. Discover video files (`VideoFileFinder`)
2. Parse each file into a `ParsedVideoFile` (`MediaGrouper.ParseVideoFile`)
    - Clean name
    - Detect season/episode markers
    - Produce a comparison title
3. Group parsed files by fuzzy title similarity (`MediaGrouper.GroupBySimilarTitle`)
4. Convert each group into a `MediaObject` (`MediaGrouper.BuildMediaObject`)
5. For each `MediaObject`, compute destination paths (`MovePlanBuilder.ResolveDestinationPath`)
6. Persist move intent + history (`MoveHistoryStore` via EF Core)

---

## 1) File discovery

`VideoFileFinder` recursively scans the configured source folder and returns only files whose extension is in the configured list (case-insensitive).

Default extensions include: `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, `.ts`, `.mpg`, `.mpeg`.

---

## 2) Parsing a single file

Each file path becomes a `ParsedVideoFile` with:

- `FilePath`: original file path
- `CleanedFileName`: cleaned version of the filename (without extension)
- `Title`: the string used for grouping (often a subset of `CleanedFileName`)
- `Season` / `Episode`: parsed numbers when detected
- `ParentFolderCleanName`: cleaned immediate parent folder name (used for canonical naming)

### 2.1 CleanName (normalization)

The cleaner runs on:

- The **filename without extension**
- The **immediate parent folder name**

It applies these transformations, in this order:

1. Remove bracketed content: any `[...]` or `(...)`
2. Remove resolution tags matching `2160p/i`, `1080p/i`, `720p/i`, `480p/i`, `360p/i`
3. Remove codec-ish tags like `x264`, `x265`, `h264`, `h.265`, `HEVC`, `AVC`, `AAC`, `DTS`, `FLAC`, `10bit`, `8bit`
4. Remove known release/source tags (examples): `ELiTE`, `YIFY`, `BONE`, `RARBG`, `SubsPlease`, `BluRay`, `BRRip`, `WEBRip`, `WEB-DL`, `HDRip`, `DVDRip`, `Dual Audio`, `PROPER`, `REPACK`, `INTERNAL`
5. Replace separators `.` `_` `-` with spaces
6. Collapse whitespace and trim

Example:

```
Raw:    Dark.Matter.1080p.x265.ELiTE[EZTVx].S01E07.mkv
Clean:  Dark Matter S01E07
```

### 2.2 Season/episode detection (priority order)

After cleaning, parsing tries these patterns in order.

#### A) SxxExx (highest priority)

Pattern (conceptually): `\bS(?<season>\d{1,2})\s*E(?<episode>\d{1,4})\b` (case-insensitive)

- `Season` and `Episode` come from the match
- `Title` becomes the cleaned text *before* the match

Example:

```
Clean:  Dark Matter S01E07
Title:  Dark Matter
Season: 1
Episode: 7
```

#### B) Trailing number (fallback)

Pattern (conceptually): `\s+(?<episode>\d{1,4})\s*$`

- `Episode` is the trailing number
- `Season` is **not** set here (it will later default to 1 during season bucketing)
- `Title` becomes the cleaned text *before* the trailing number

Example:

```
Raw:    [SubsPlease] Jujutsu Kaisen - 56 (1080p) [0F106B43].mkv
Clean:  Jujutsu Kaisen 56
Title:  Jujutsu Kaisen
Season: null
Episode: 56
```

#### C) No episode info

If neither pattern matches:

- `Title` becomes the full cleaned name
- `Season` and `Episode` are null

Example:

```
Raw:   Taxi Driver.mkv
Clean: Taxi Driver
Title: Taxi Driver
```

### 2.3 Parent folder name (for canonical naming)

The parser also cleans the *immediate* parent folder name of each file using the same cleaning pipeline.

This value can be used later to pick a nicer canonical show name, e.g.:

```
Folder: Jujutsu Kaisen/
Files:  Jujustu Kaisen 01.mkv   (typo)
```

---

## 3) Grouping: fuzzy title similarity

Grouping operates on `ParsedVideoFile.Title` (not the full `CleanedFileName`).

### 3.1 Similarity metric

The similarity score is computed as:

$$\text{similarity}(a, b) = 1 - \frac{\text{LevenshteinDistance}(a, b)}{\max(|a|, |b|)}$$

- Inputs are compared case-insensitively.
- Exact match (case-insensitive) is always considered similar.
- Otherwise, a match is considered similar when `similarity >= 0.80`.

### 3.2 Group formation (greedy, seed-based)

The implementation is intentionally simple and fast:

1. Iterate files in input order.
2. The first unassigned file becomes a new group “seed”.
3. Add any later unassigned file whose title is similar **to the seed’s title**.

This is a **greedy** strategy, not a full clustering algorithm.

Implication: similarity is not transitive in the implementation.

- If `A` is similar to `B`, and `B` is similar to `C`, but `A` is *not* similar to `C`, then `C` will not be added to `A`’s group (even though it might have matched if `B` was the seed).

In practice, this usually behaves well for clean releases, but it’s worth knowing when debugging unexpected grouping.

---

## 4) Movie vs Show classification

Once a group is formed, `MediaGrouper.BuildMediaObject` classifies it.

The key checks are:

- `isMultiFile = group.Count > 1`
- `hasSeasonEpisode = group.Any(f => f.Season.HasValue)`  (note: **Season**, not Episode)

### 4.1 Movie

A group becomes a movie only when:

- It is a **single file**, AND
- No file in the group has a parsed `Season` (i.e., no `SxxExx` was detected)

Movie naming:

- `MediaObject.Name` is set to the file’s **full cleaned filename** (`CleanedFileName`), so trailing numbers like years remain part of the movie name.

Example:

```
Raw:   Interstellar.2014.1080p.BluRay.x265.mkv
Clean: Interstellar 2014
Movie name: Interstellar 2014
```

### 4.2 Show

A group becomes a show when either:

- It contains **multiple files**, OR
- Any file has a parsed `Season` (meaning `SxxExx` was detected)

Note the nuance:

- A single file like `Jujutsu Kaisen 56.mkv` (trailing number only) is treated as a **Movie** by the current implementation.
- The same naming style becomes a **Show** once there are multiple related files, because `isMultiFile` becomes true.

---

## 5) Canonical show name selection

For shows, the final `MediaObject.Name` (used as the show folder name) is chosen by `DetermineCanonicalName`:

1. Collect distinct `ParentFolderCleanName` values from the group.
2. For each distinct folder name:
    - Count how many files in the group have `Title` similar to that folder name.
    - If at least half match (`matchCount * 2 >= group.Count`), return that folder name.
3. Otherwise, fall back to the **most common `Title`** in the group.
    - Ties are broken by picking the **longest** title.

This is designed to:

- Prefer “nice” existing folders when users already partially organized content
- Fix typos in filenames by taking the folder name as canonical

---

## 6) Season bucketing and episode assignment

For shows, `BuildSeasons` builds `Season` objects.

### 6.1 Season bucketing

- If `Season` is present (from `SxxExx`), use it.
- Otherwise, default the season to **1**.

### 6.2 Episode assignment

Within each season:

1. Files with an explicit episode number (`Episode` present) keep that number.
2. Files without an episode number are sorted by their **original filename** (case-insensitive) and assigned episode numbers sequentially after the highest existing episode in that season.

This ensures every show file gets a stable `(Season, Episode)` identity, even if some releases don’t contain episode numbers.

---

## 7) Destination paths

`MovePlanBuilder.ResolveDestinationPath` computes where each file should go.

### 7.1 Movies

Folder structure:

```
{root}/{MovieName}/{MovieName}.ext
```

Example:

```
{root}/Taxi Driver/Taxi Driver.mkv
```

### 7.2 Shows

Folder structure:

```
{root}/{ShowName}/Season {NN}/{OriginalFileNameWithoutExtension}.ext
```

Important detail: for shows, the moved filename is based on the **original filename**, not the cleaned title.

Example:

```
Input: The.Office.S02E03.Health.Care.mkv
Output: {root}/The Office/Season 02/The.Office.S02E03.Health.Care.mkv
```

---

## 8) Move history (idempotency)

Before moving anything, `MovePlanBuilder` writes/updates SQLite move history entries keyed by a `UniqueKey`.

- Movies: `UniqueKey = media.Name`
- Shows: `UniqueKey = {ShowName}_Season{NN}_Episode{EE}`

For a given `UniqueKey`, the builder looks up the latest record and:

- Same destination + `IsMoved = true` → skip (already moved)
- Same destination + `IsMoved = false` → leave as pending (no new record)
- Different destination → add a new record with `IsMoved = false`
- No record → add a new record with `IsMoved = false`

---

## 9) Worked examples

### Example A — Typical show with SxxExx

Input files:

```
./Dark Matter 1080p x265 ELiTE[EZTVx] S01E07.mkv
./Dark Matter/Dark Matter 1080p x265 ELiTE[EZTVx] S01E08.mkv
./Dark Matter/Season 01/Dark Matter S01E06.mkv
```

Cleaning + parsing produces titles like `Dark Matter` and seasons/episodes 1x06, 1x07, 1x08.

Grouping:

- Titles are identical → one group → **Show**

Destination:

```
{root}/Dark Matter/Season 01/Dark Matter S01E06.mkv
{root}/Dark Matter/Season 01/Dark Matter 1080p x265 ELiTE[EZTVx] S01E07.mkv
{root}/Dark Matter/Season 01/Dark Matter 1080p x265 ELiTE[EZTVx] S01E08.mkv
```

### Example B — Movie with year in name

Input file:

```
./Interstellar.2014.2160p.BluRay.DTS.x265.mkv
```

Cleaning:

```
CleanedFileName: Interstellar 2014
Title:           Interstellar   (because trailing number parsing matches 2014)
```

Classification:

- Single file and no parsed Season → **Movie**
- Movie name uses `CleanedFileName`, so the year remains in the folder name.

Destination:

```
{root}/Interstellar 2014/Interstellar 2014.mkv
```

### Example C — Anime-style (trailing episode numbers)

Input files:

```
./[SubsPlease] Jujutsu Kaisen - 55 (1080p) [AAAA].mkv
./[SubsPlease] Jujutsu Kaisen - 56 (1080p) [BBBB].mkv
./[SubsPlease] Jujutsu Kaisen - 57 (1080p) [CCCC].mkv
```

Cleaning:

- Each becomes `Jujutsu Kaisen 55/56/57` and titles become `Jujutsu Kaisen`

Classification:

- Multiple files → **Show**
- Season defaults to 1, episodes are 55/56/57

Destinations:

```
{root}/Jujutsu Kaisen/Season 01/[SubsPlease] Jujutsu Kaisen - 55 (1080p) [AAAA].mkv
{root}/Jujutsu Kaisen/Season 01/[SubsPlease] Jujutsu Kaisen - 56 (1080p) [BBBB].mkv
{root}/Jujutsu Kaisen/Season 01/[SubsPlease] Jujutsu Kaisen - 57 (1080p) [CCCC].mkv
```

---

## 10) Edge cases to be aware of

- Single-file trailing-number names (e.g. `Show Name 01.mkv`) are treated as movies until there are multiple matching files.
- Movies with years can group strangely if multiple years exist (e.g. `Dune 1984` and `Dune 2021` both produce the grouping title `Dune`). With both present, the group becomes a show and the “episode numbers” become 1984 and 2021.
- Greedy grouping can split what humans think is one cluster when similarity is “chain-like” (A~B, B~C, but A!~C).

---

## Data model (conceptual)

```
MediaObject
├── Name: string
├── Type: Movie | Show
├── MoviePath: string?          (Movie only)
└── Seasons: List<Season>       (Show only)
   ├── SeasonNumber: int
   └── Episodes: List<Episode>
      ├── Path: string
      └── EpisodeNumber: int
```