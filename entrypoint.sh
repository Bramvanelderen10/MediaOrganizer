#!/bin/bash
set -e

PUID=${PUID:-1000}
PGID=${PGID:-1000}
VIDEO_GID=${VIDEO_GID:-44}
RENDER_GID=${RENDER_GID:-992}

# Create a group with the requested GID if one does not already exist
if ! getent group "$PGID" > /dev/null 2>&1; then
    groupadd -g "$PGID" mediaorganizer
fi

# Create a user with the requested UID (joined to the group above) if one does not already exist
if ! getent passwd "$PUID" > /dev/null 2>&1; then
    useradd -u "$PUID" -g "$PGID" -s /bin/false -d /app mediaorganizer
fi

# Resolve the runtime user's name for group membership changes below.
USER_NAME=$(getent passwd "$PUID" | cut -d: -f1)

# Make sure the primary group matches PGID. `gosu <uid>` (unlike `gosu <uid>:<gid>`)
# preserves supplementary groups, which is what grants access to /dev/dri.
usermod -g "$PGID" "$USER_NAME" >/dev/null 2>&1 || true

# Hardware transcoding: /dev/dri is owned by the host's video/render groups. gosu resets
# supplementary groups from /etc/group, so create the groups with the GIDs passed in (which
# must match the host device owners) and add the runtime user to them. Failures are ignored
# so the container still starts on hosts without a GPU (ffmpeg then falls back to libx264).
align_group() {
    local name=$1
    local gid=$2

    if getent group "$name" > /dev/null 2>&1; then
        groupmod -o -g "$gid" "$name" >/dev/null 2>&1 || true
    else
        groupadd -o -g "$gid" "$name" >/dev/null 2>&1 || true
    fi

    usermod -aG "$name" "$USER_NAME" >/dev/null 2>&1 || true
}

if [ -d /dev/dri ]; then
    align_group video "$VIDEO_GID"
    align_group render "$RENDER_GID"
fi

exec gosu "$PUID" "$@"
