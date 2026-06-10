#!/usr/bin/env bash
# ============================================================================
# One-command full setup on the Raspberry Pi (run with sudo, interactively):
#   ssh -t deiuvrg@<pi> sudo ./deploy/setup-all.sh
#
# Does everything in order:
#   1. Asks for the app secrets and writes /etc/stabilizatorhub/secrets.env
#   2. Runs install.sh (service user, systemd units, downloads latest release)
#   3. Switches the Cloudflare tunnel from the test page (8080) to the app (5000)
#   4. Verifies the service health and prints the result
# Safe to re-run: every step is idempotent.
# ============================================================================
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run with sudo: sudo $0" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> [1/4] Application secrets"
if [ -f /etc/stabilizatorhub/secrets.env ] && ! grep -q CHANGE_ME /etc/stabilizatorhub/secrets.env; then
  echo "    secrets.env already configured - keeping it."
else
  read -rp  "    Admin email for the web app: " ADMIN_EMAIL
  read -rsp "    Admin password (min 10 chars, upper+lower+digit): " ADMIN_PASS; echo
  read -rsp "    MQTT password of the 'backend' broker user: " MQTT_PASS; echo

  mkdir -p /etc/stabilizatorhub
  printf 'Mqtt__Password=%s\nAdmin__Email=%s\nAdmin__Password=%s\nUrls=http://127.0.0.1:5000\n' \
    "$MQTT_PASS" "$ADMIN_EMAIL" "$ADMIN_PASS" > /etc/stabilizatorhub/secrets.env
  chmod 600 /etc/stabilizatorhub/secrets.env
  echo "    secrets.env written."
fi

echo "==> [2/4] Installing the application (latest GitHub release)"
bash "$SCRIPT_DIR/install.sh"

echo "==> [3/4] Pointing the Cloudflare tunnel at the application"
if grep -q 'http://localhost:8080' /etc/cloudflared/config.yml 2>/dev/null; then
  sed -i 's|http://localhost:8080|http://localhost:5000|' /etc/cloudflared/config.yml
  echo "    ingress switched 8080 -> 5000."
else
  echo "    ingress already points at 5000."
fi
systemctl disable --now webtest 2>/dev/null || true
systemctl restart cloudflared

echo "==> [4/4] Verifying"
sleep 3

if ! systemctl is-active --quiet stabilizatorhub; then
  echo "    FAILED: service is not running. Inspect with:"
  echo "      journalctl -u stabilizatorhub -n 50 --no-pager"
  exit 1
fi

HEALTH=$(curl -fsS http://127.0.0.1:5000/healthz || true)

if [ -z "$HEALTH" ]; then
  echo "    FAILED: no answer on /healthz. Inspect with:"
  echo "      journalctl -u stabilizatorhub -n 50 --no-pager"
  exit 1
fi

echo "    local healthz: $HEALTH"
echo
echo "ALL DONE. The app is live at https://app.licenta-stabilizator-vrg.org"
echo "Sign in with the admin account you just configured."
