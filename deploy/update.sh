#!/usr/bin/env bash
# ============================================================================
# StabilizatorHub self-update script (runs as root via the updater unit).
#  1. Reads + removes the trigger file (so a failed run does not loop).
#  2. Fetches the latest GitHub release and compares it with `current`.
#  3. Downloads the linux-arm64 artifact, verifies its SHA-256.
#  4. Extracts to releases/<tag>, atomically switches the `current` symlink.
#  5. Restarts the service and health-checks it; rolls back on failure.
# ============================================================================
set -euo pipefail

REPO="DeiuVRG/StabilizatorHubVRG"
ASSET="stabilizatorhub-linux-arm64.tar.gz"
BASE_DIR="/opt/stabilizatorhub"
TRIGGER="/var/lib/stabilizatorhub/update.requested"
HEALTH_URL="http://127.0.0.1:5000/healthz"
SERVICE="stabilizatorhub"

log() { echo "[update] $*"; }

# 1) Consume the trigger (when invoked manually it may not exist - that is fine).
rm -f "$TRIGGER"

# 2) Latest release tag.
log "Querying latest release of $REPO..."
RELEASE_JSON=$(curl -fsSL -H 'Accept: application/vnd.github+json' \
  "https://api.github.com/repos/$REPO/releases/latest")
TAG=$(echo "$RELEASE_JSON" | grep -m1 '"tag_name"' | cut -d '"' -f 4)

if [ -z "$TAG" ]; then
  log "ERROR: could not determine the latest release tag."
  exit 1
fi

CURRENT_TARGET=$(readlink -f "$BASE_DIR/current" 2>/dev/null || true)
PREVIOUS_DIR="$CURRENT_TARGET"
NEW_DIR="$BASE_DIR/releases/$TAG"

if [ "$CURRENT_TARGET" = "$NEW_DIR" ]; then
  log "Already running $TAG - nothing to do."
  exit 0
fi

# 3) Download + verify.
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

DOWNLOAD_URL="https://github.com/$REPO/releases/download/$TAG"
log "Downloading $ASSET ($TAG)..."
curl -fsSL -o "$TMP/$ASSET" "$DOWNLOAD_URL/$ASSET"
curl -fsSL -o "$TMP/$ASSET.sha256" "$DOWNLOAD_URL/$ASSET.sha256"

log "Verifying checksum..."
(cd "$TMP" && sha256sum -c "$ASSET.sha256")

# 4) Extract and switch atomically.
log "Installing to $NEW_DIR..."
rm -rf "$NEW_DIR"
mkdir -p "$NEW_DIR"
tar -xzf "$TMP/$ASSET" -C "$NEW_DIR"
chown -R stabhub:stabhub "$NEW_DIR"

ln -sfn "$NEW_DIR" "$BASE_DIR/current.new"
mv -Tf "$BASE_DIR/current.new" "$BASE_DIR/current"

# 5) Restart + health check (DB migrations run automatically at startup).
log "Restarting $SERVICE..."
systemctl restart "$SERVICE"

for i in $(seq 1 30); do
  sleep 2
  if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then
    log "Update to $TAG successful."
    # Keep the previous release plus the new one; prune anything older.
    find "$BASE_DIR/releases" -mindepth 1 -maxdepth 1 -type d \
      ! -path "$NEW_DIR" ${PREVIOUS_DIR:+! -path "$PREVIOUS_DIR"} \
      -exec rm -rf {} +
    exit 0
  fi
done

# Rollback.
log "Health check FAILED - rolling back."
if [ -n "$PREVIOUS_DIR" ] && [ -d "$PREVIOUS_DIR" ]; then
  ln -sfn "$PREVIOUS_DIR" "$BASE_DIR/current.new"
  mv -Tf "$BASE_DIR/current.new" "$BASE_DIR/current"
  systemctl restart "$SERVICE"
  log "Rolled back to $(basename "$PREVIOUS_DIR")."
fi
exit 1
