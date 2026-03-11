#!/bin/bash
set -e

PUID=${PUID:-1000}
PGID=${PGID:-1000}

# Create a group with the requested GID if one does not already exist
if ! getent group "$PGID" > /dev/null 2>&1; then
    groupadd -g "$PGID" mediaorganizer
fi

# Create a user with the requested UID (joined to the group above) if one does not already exist
if ! getent passwd "$PUID" > /dev/null 2>&1; then
    useradd -u "$PUID" -g "$PGID" -s /bin/false -d /app mediaorganizer
fi

exec gosu "$PUID:$PGID" "$@"
