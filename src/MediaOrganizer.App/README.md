# MediaOrganizer App

Flutter companion app for the MediaOrganizer API.

It lets you:
- Configure and persist the API URL
- Check API health continuously
- Trigger an organize run
- Forget move history for a specific show season
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

- `GET /health`
- `POST /trigger-job`
- `POST /forget-show-season`
- `GET /logs/stream?tail=...`

## Notes

- If no scheme is provided, the app assumes `http://`.
- The app polls health every second while active.
- Log streaming reconnect attempts are throttled to avoid rapid retries.
