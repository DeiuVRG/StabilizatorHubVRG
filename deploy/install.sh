#!/usr/bin/env bash
# ============================================================================
# First-time installation on the Raspberry Pi (run with sudo from the repo's
# deploy/ directory, or after copying these files to the Pi).
# Prerequisites already on the Pi (see docs/server-raspberry-pi-documentatie-completa.md):
#   - ASP.NET Core 10 runtime at /opt/dotnet (+ /usr/bin/dotnet symlink)
#   - Mosquitto with the `backend` account
#   - cloudflared tunnel
# ============================================================================
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run with sudo." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BASE_DIR="/opt/stabilizatorhub"

echo "[install] Creating service user and directories..."
id -u stabhub >/dev/null 2>&1 || useradd -r -s /usr/sbin/nologin stabhub
mkdir -p "$BASE_DIR/releases" "$BASE_DIR/bin" /var/lib/stabilizatorhub
chown -R stabhub:stabhub /var/lib/stabilizatorhub

echo "[install] Installing the updater script..."
install -m 755 "$SCRIPT_DIR/update.sh" "$BASE_DIR/bin/update.sh"

echo "[install] Writing the secrets file template (edit it!)..."
mkdir -p /etc/stabilizatorhub
if [ ! -f /etc/stabilizatorhub/secrets.env ]; then
  cat > /etc/stabilizatorhub/secrets.env <<'EOF'
# --- StabilizatorHub secrets (never commit this file) ---
# MQTT password of the `backend` broker account:
Mqtt__Password=CHANGE_ME

# Administrator account (created/updated at startup):
Admin__Email=admin@example.com
Admin__Password=CHANGE_ME_Strong1

# Listen address behind the Cloudflare tunnel:
Urls=http://127.0.0.1:5000
EOF
  chmod 600 /etc/stabilizatorhub/secrets.env
  chown stabhub:stabhub /etc/stabilizatorhub/secrets.env
fi

echo "[install] Installing systemd units..."
install -m 644 "$SCRIPT_DIR/stabilizatorhub.service" /etc/systemd/system/
install -m 644 "$SCRIPT_DIR/stabilizatorhub-update.service" /etc/systemd/system/
install -m 644 "$SCRIPT_DIR/stabilizatorhub-update.path" /etc/systemd/system/
systemctl daemon-reload

echo "[install] Downloading the latest release..."
"$BASE_DIR/bin/update.sh" || {
  echo "[install] No release available yet - publish one (git tag vX.Y.Z) and re-run:"
  echo "          sudo $BASE_DIR/bin/update.sh"
}

systemctl enable --now stabilizatorhub-update.path
systemctl enable stabilizatorhub

echo
echo "[install] Done. Next steps:"
echo "  1. Edit /etc/stabilizatorhub/secrets.env (MQTT password, admin account)."
echo "  2. sudo systemctl restart stabilizatorhub"
echo "  3. Point the Cloudflare tunnel at http://localhost:5000 in /etc/cloudflared/config.yml,"
echo "     disable the test page (sudo systemctl disable --now webtest) and"
echo "     sudo systemctl restart cloudflared"
