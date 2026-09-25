# MediaOrganizer App

Flutter companion app for the MediaOrganizer API.

It lets you:
- Configure and persist the API URL
- Check API health continuously
- Trigger an organize run
- Transcode media files that are not yet in the target codec (button below the organize button)
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

The home screen has a **Transcode videos** button below **Organize videos**. It calls
`POST /transcode`, which asks the server to scan the media library and re-encode every file
whose codec is not the configured target codec (e.g. HEVC to H.264 so an older GPU can play
it). Because it skips files that are already in the target codec, it is safe to run repeatedly.

The **Library** screen also has a transcode button next to every show, season, movie and
episode. Those send just that item's file paths (`{"paths": [...]}`), so you can convert one
title without scanning the whole library. A confirmation dialog warns that the originals are
replaced (unless the server is configured to keep originals). Per-item transcodes require a
server that supports the `paths` body (newer builds).

The server must have transcoding enabled (`MediaOrganizer:Transcoding:Enabled=true`); if it is
not, the app shows the server's message. While either job is running the other button is
disabled so the two do not overlap.

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
| POST | `/transcode` | Transcode files not yet in the target codec (optional `paths` body for single items) |
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
