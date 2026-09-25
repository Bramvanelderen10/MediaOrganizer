# MediaOrganizer App

Flutter companion app for the MediaOrganizer API.

It lets you:
- Configure and persist the API URL
- Check API health continuously
- Trigger an organize run
- Transcode individual shows, seasons, movies or episodes from the Library screen
- Watch transcode progress on the Transcode job screen, with a hardware self-test
- Open or share a `.torrent` file or magnet link with the app and confirm it before downloading
- View all torrents with live download progress
- Browse, rename, move, and delete files in the source folder
- View and manage the organized media library
- Forget move history (movies, shows, seasons, episodes, or batch)
- View disk storage usage
- View live server logs through SSE (`/logs/stream`)

## Requirements

- Flutter SDK (stable)
- Dart SDK compatible with this project (`^3.7.0`)
- A running MediaOrganizer backend

## Run locally

From [src/MediaOrganizer.App](src/MediaOrganizer.App):

1. Install dependencies
	- `flutter pub get`
2. Run the app
	- `flutter run`

## Opening torrents

Android registers the app for `.torrent` files (`application/x-bittorrent` and
`application/octet-stream`), for **magnet links** (`magnet:` scheme) and for shared links
(`text/plain`). So you can:

- open a `.torrent` from a file manager or the browser download notification,
- tap a magnet link in a browser and pick **Media Organizer**,
- or share a `.torrent` file / magnet link into the app.

Either way an in-app confirmation dialog shows the name with a **Cancel** / **Download**
choice. Nothing is sent until you confirm; on confirm the file or link is uploaded to
`POST /torrents/add` or `POST /torrents/add-magnet` and qBittorrent starts downloading it
immediately. The organize job is not triggered by a torrent download.

Magnet links have no file name until metadata is fetched, so the dialog shows the `dn`
(the display name inside the link) or the info hash instead.

Because `application/octet-stream` and `text/plain` are generic types, Android may also
offer Media Organizer for other files and link shares. The app filters these out and only
acts on `.torrent` files and `magnet:` links.

## Viewing torrent progress

**Torrents** in the top-right menu lists every torrent with a progress bar, percentage,
status, transferred size, speed and ETA. The list refreshes every few seconds while the
screen is open; pull down or tap the refresh icon to refresh manually.

## Codec transcoding

Open the **Library** screen and use the transcode button next to any show, season, movie or
episode. It asks the server to re-encode just that item (e.g. HEVC to H.264 so an older GPU can
play it) and sends that item's file paths (`{"paths": [...]}`), so you can convert one title
without scanning the whole library. Because files already in the target codec are skipped, it is
safe to run repeatedly.

A confirmation dialog warns that the original files are replaced (unless the server is configured
to keep originals). The server then runs the work **in the background**, so the app opens the
**Transcode job** screen straight away, where you can watch progress.

## Transcode job screen

Open it from the top-right menu on the home screen. It polls `GET /transcode/job` every two
seconds and shows the job state (`idle`, `running`, `completed`, `failed`), a progress bar, the
counters and the file currently being encoded, plus any error.

It also has a **Run self-test** button (calls `GET /transcode/selftest`) that performs a 2 second
encode to prove hardware acceleration is really working, reporting the encoder, the VA-API driver
(e.g. `iHD` / `i965`) and the ffmpeg output. This is the reliable way to check — the status
endpoint only reports whether the encoder is compiled into ffmpeg.

The server must have transcoding enabled (`MediaOrganizer:Transcoding:Enabled=true`); if it is
not, the app shows the server's message.

## Older servers

If the server runs an older MediaOrganizer build that predates these features, the app
detects the missing endpoint (HTTP 404) and tells you to update the server instead of
showing a raw error. The same applies when qBittorrent is not configured on the server
(HTTP 503).

## First-time setup

On first launch, enter your MediaOrganizer API address, for example:
- `192.168.50.200:45263`
- `http://192.168.50.200:45263`

The app stores this value in local preferences. You can clear it via **Reset API URL** in the top-right menu.

## Backend endpoints used

| Method | Path | Description |
|---|---|---|
| GET | `/health` | Connectivity check |
| GET | `/storage-info` | Disk storage info |
| GET | `/logs/stream?tail=...` | Live log streaming via SSE |
| GET | `/library` | Organized media library structure |
| GET | `/browse` | Source folder directory listing |
| POST | `/trigger-job` | Trigger organize job |
| POST | `/transcode` | Start a background transcode job (optional `paths` + `label` body) |
| GET | `/transcode/job` | Transcode job state, progress and current file |
| GET | `/transcode/selftest` | Run a real encode to verify hardware acceleration |
| GET | `/transcode/status` | Transcoding config + hardware availability |
| GET | `/torrents` | List torrents with progress |
| POST | `/torrents/add` | Upload a `.torrent` file and start the download |
| POST | `/torrents/add-magnet` | Add a magnet link and start the download |
| POST | `/rename` | Rename file or directory |
| POST | `/move` | Move file or directory |
| POST | `/delete` | Delete files or directories |
| POST | `/forget-movie` | Forget movie history |
| POST | `/forget-show` | Forget all history for a show |
| POST | `/forget-show-season` | Forget history for a show season |
| POST | `/forget-episode` | Forget history for a specific episode |
| POST | `/forget-batch` | Forget history for multiple items |

## Notes

- If no scheme is provided, the app assumes `http://`.
- The app polls health every second while active.
- Log streaming reconnect attempts are throttled to avoid rapid retries.
