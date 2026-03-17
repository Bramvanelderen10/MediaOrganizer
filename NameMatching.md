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

**Season folder traversal**: if the immediate parent folder matches `Season\s*\d{1,2}` (case-insensitive), e.g. `Season 02`, the parser goes up one more level and uses the grandparent folder name instead.

Examples:

```
Path:   /media/Jujutsu Kaisen/Jujustu Kaisen 01.mkv
Parent: Jujutsu Kaisen

Path:   /media/MyShow/Season 02/S02E06-Pilot.mkv
Parent: MyShow   (grandparent, because immediate parent is "Season 02")
```

### 2.4 Empty title fallback

After SxxExx or SxxDashExx pattern matching, if the extracted title is empty (i.e. the pattern appears at the very start of the cleaned filename), the title is replaced with the `ParentFolderCleanName`.

Example:

```
Path:   /media/ThatTimeIGotReincarnatedAsASlime/S02E06-The Beauty Makes Her Move [5F8B3E3E].mkv
Clean:  S02E06 The Beauty Makes Her Move
Title:  "" → falls back to "ThatTimeIGotReincarnatedAsASlime" (parent folder)
```

---

## 3) Pre-grouping: folder-based title override

Before fuzzy grouping, a heuristic detects files where filenames contain episode descriptions rather than show names.

### 3.1 Logic

1. Group parsed files by `(parent directory path, season number)` — only files with a parsed season.
2. For each group with ≥ 2 files: check if **any** title is similar (≥ 0.80) to at least 50% of the titles in that group.
3. If **no coherent title cluster** exists → override all `Title` values with `ParentFolderCleanName`.

This fires regardless of where `SxxExx` appears in the filename.

Example:

```
Path: /media/CoolShow/The Beginning S01E01.mkv   → Title: "The Beginning"
Path: /media/CoolShow/Darkness Falls S01E02.mkv   → Title: "Darkness Falls"
Path: /media/CoolShow/New Dawn S01E03.mkv          → Title: "New Dawn"
```

No pair of titles meets the 80% similarity threshold, and no title matches ≥ 50% of the group. All titles are overridden to `"CoolShow"` (parent folder).

### 3.2 When it does NOT fire

- Fewer than 2 files in a folder+season group → no override.
- Titles already form a coherent cluster (e.g. `"Dark Matter"` appears in all filenames) → no override.
- Files without a parsed season → not considered.

---

## 4) Grouping: fuzzy title similarity

Grouping operates on `ParsedVideoFile.Title` (not the full `CleanedFileName`).

### 4.1 Similarity metric

The similarity score is computed as:

$$\text{similarity}(a, b) = 1 - \frac{\text{LevenshteinDistance}(a, b)}{\max(|a|, |b|)}$$

- Inputs are compared case-insensitively.
- Exact match (case-insensitive) is always considered similar.
- Otherwise, a match is considered similar when `similarity >= 0.80`.

### 4.2 Group formation (greedy, seed-based)

The implementation is intentionally simple and fast:

1. Iterate files in input order.
2. The first unassigned file becomes a new group “seed”.
3. Add any later unassigned file whose title is similar **to the seed’s title**.

This is a **greedy** strategy, not a full clustering algorithm.

Implication: similarity is not transitive in the implementation.

- If `A` is similar to `B`, and `B` is similar to `C`, but `A` is *not* similar to `C`, then `C` will not be added to `A`’s group (even though it might have matched if `B` was the seed).

In practice, this usually behaves well for clean releases, but it’s worth knowing when debugging unexpected grouping.

---

## 5) Movie vs Show classification

Once a group is formed, `MediaGrouper.BuildMediaObject` classifies it.

The key checks are:

- `isMultiFile = group.Count > 1`
- `hasSeasonEpisode = group.Any(f => f.Season.HasValue)`  (note: **Season**, not Episode)

### 5.1 Movie

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

### 5.2 Show

A group becomes a show when either:

- It contains **multiple files**, OR
- Any file has a parsed `Season` (meaning `SxxExx` was detected)

Note the nuance:

- A single file like `Jujutsu Kaisen 56.mkv` (trailing number only) is treated as a **Movie** by the current implementation.
- The same naming style becomes a **Show** once there are multiple related files, because `isMultiFile` becomes true.

---

## 6) Canonical show name selection

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

## 7) Season bucketing and episode assignment

For shows, `BuildSeasons` builds `Season` objects.

### 7.1 Season bucketing

- If `Season` is present (from `SxxExx`), use it.
- Otherwise, default the season to **1**.

### 7.2 Episode assignment

Within each season:

1. Files with an explicit episode number (`Episode` present) keep that number.
2. Files without an episode number are sorted by their **original filename** (case-insensitive) and assigned episode numbers sequentially after the highest existing episode in that season.

This ensures every show file gets a stable `(Season, Episode)` identity, even if some releases don’t contain episode numbers.

---

## 8) Destination paths

`MovePlanBuilder.ResolveDestinationPath` computes where each file should go.

### 8.1 Movies

Folder structure:

```
{root}/{MovieName}/{MovieName}.ext
```

Example:

```
{root}/Taxi Driver/Taxi Driver.mkv
```

### 8.2 Shows

Folder structure:

```
{root}/{ShowName}/Season {NN}/{FileName}.ext
```

The filename depends on whether the original filename already contains the show name:

- **Show name present in filename**: the original filename is preserved (after stripping trailing copy suffixes).
- **Show name missing from filename**: the file is renamed to `{ShowName} S{Season:00}E{Episode:00}.ext`.

The check normalizes the filename by replacing `.`, `_`, `-` with spaces and does a case-insensitive substring search for the show name.

Examples:

```
# Show name already in filename → preserved
Input:  The.Office.S02E03.Health.Care.mkv  (show: "The Office")
Output: {root}/The Office/Season 02/The.Office.S02E03.Health.Care.mkv

# Show name missing → reconstructed
Input:  S02E06-The Beauty Makes Her Move [5F8B3E3E].mkv  (show: "ThatTimeIGotReincarnatedAsASlime")
Output: {root}/ThatTimeIGotReincarnatedAsASlime/Season 02/ThatTimeIGotReincarnatedAsASlime S02E06.mkv
```

---

## 9) Move history (idempotency)

Before moving anything, `MovePlanBuilder` writes/updates SQLite move history entries keyed by a `UniqueKey`.

- Movies: `UniqueKey = media.Name`
- Shows: `UniqueKey = {ShowName}_Season{NN}_Episode{EE}`

For a given `UniqueKey`, the builder looks up the latest record and:

- Same destination + `IsMoved = true` → skip (already moved)
- Same destination + `IsMoved = false` → leave as pending (no new record)
- Different destination → add a new record with `IsMoved = false`
- No record → add a new record with `IsMoved = false`

---

## 10) Worked examples

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

## 11) Edge cases to be aware of

- Single-file trailing-number names (e.g. `Show Name 01.mkv`) are treated as movies until there are multiple matching files.
- Movies with years can group strangely if multiple years exist (e.g. `Dune 1984` and `Dune 2021` both produce the grouping title `Dune`). With both present, the group becomes a show and the “episode numbers” become 1984 and 2021.
- Greedy grouping can split what humans think is one cluster when similarity is “chain-like” (A~B, B~C, but A!~C).
---

### Example D — Folder-only show name (no show name in filename)

Input files:

```
./ThatTimeIGotReincarnatedAsASlime/S02E06-The Beauty Makes Her Move [5F8B3E3E].mkv
./ThatTimeIGotReincarnatedAsASlime/S02E07-bladiebla[5F8B3E3E].mkv
```

Cleaning + parsing:

```
File 1:  Clean: "S02E06 The Beauty Makes Her Move"  → Title: "" → fallback to parent: "ThatTimeIGotReincarnatedAsASlime"
File 2:  Clean: "S02E07 bladiebla"                   → Title: "" → fallback to parent: "ThatTimeIGotReincarnatedAsASlime"
```

Grouping: titles are identical → one group → **Show** named `ThatTimeIGotReincarnatedAsASlime`.

Filename reconstruction: original filenames don't contain `ThatTimeIGotReincarnatedAsASlime` → reconstructed.

Destination:

```
{root}/ThatTimeIGotReincarnatedAsASlime/Season 02/ThatTimeIGotReincarnatedAsASlime S02E06.mkv
{root}/ThatTimeIGotReincarnatedAsASlime/Season 02/ThatTimeIGotReincarnatedAsASlime S02E07.mkv
```

### Example E — Files in existing Season subfolder

Input files:

```
./MyShow/Season 02/S02E01-Pilot.mkv
./MyShow/Season 02/S02E02-SecondEp.mkv
```

Season folder detection: immediate parent is `Season 02` → uses grandparent `MyShow`.
Parsed titles are empty (SxxExx at start) → both fall back to `MyShow`.

Destination:

```
{root}/MyShow/Season 02/MyShow S02E01.mkv
{root}/MyShow/Season 02/MyShow S02E02.mkv
```

### Example F — Episode descriptions without show name (SxxExx not at start)

Input files:

```
./CoolShow/The Beginning S01E01.mkv
./CoolShow/Darkness Falls S01E02.mkv
./CoolShow/New Dawn S01E03.mkv
```

Parsed titles: `The Beginning`, `Darkness Falls`, `New Dawn` — all different, no coherent cluster.
Pre-grouping override: all titles replaced with `CoolShow` (parent folder).
Grouping: one group → **Show** named `CoolShow`.

Destination:

```
{root}/CoolShow/Season 01/CoolShow S01E01.mkv
{root}/CoolShow/Season 01/CoolShow S01E02.mkv
{root}/CoolShow/Season 01/CoolShow S01E03.mkv
```
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