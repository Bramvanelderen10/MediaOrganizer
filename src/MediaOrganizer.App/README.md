# MediaOrganizer App

Flutter companion app for the MediaOrganizer API.

It lets you:
- Configure and persist the API URL
- Check API health continuously
- Trigger an organize run
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
