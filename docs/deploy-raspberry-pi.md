# Deploy pe Raspberry Pi

Pornește de la serverul deja configurat (OS, Mosquitto, Cloudflare Tunnel,
Tailscale, .NET runtime — vezi
[server-raspberry-pi-documentatie-completa.md](server-raspberry-pi-documentatie-completa.md)).

## 1. Layout pe Pi

```
/opt/stabilizatorhub/
├── bin/update.sh            # updater-ul (rulat de systemd ca root)
├── releases/v1.0.0/         # fiecare versiune instalată, separat
└── current -> releases/v1.0.0    # simlink atomic la versiunea activă
/var/lib/stabilizatorhub/    # date scriibile: SQLite, loguri criptate, chei DP
/etc/stabilizatorhub/secrets.env  # secrete (0600)
```

## 2. Instalarea inițială

```bash
# pe Pi (prin Tailscale): copiază directorul deploy/ din repo, apoi:
cd deploy
sudo ./install.sh
sudo nano /etc/stabilizatorhub/secrets.env    # parola MQTT + admin + URL
sudo systemctl restart stabilizatorhub
systemctl status stabilizatorhub
curl http://127.0.0.1:5000/healthz            # {"status":"ok","version":"..."}
```

`install.sh`: creează utilizatorul `stabhub`, directoarele, instalează
unit-urile systemd (aplicația hardenată + updater-ul), descarcă **ultimul
release** de pe GitHub și activează totul.

## 3. Comutarea tunelului Cloudflare pe aplicație

În `/etc/cloudflared/config.yml` schimbă serviciul:

```yaml
ingress:
  - hostname: app.licenta-stabilizator-vrg.org
    service: http://localhost:5000        # era 8080 (pagina de test)
  - service: http_status:404
```

```bash
sudo systemctl disable --now webtest
sudo systemctl restart cloudflared
```

Aplicația e live la https://app.licenta-stabilizator-vrg.org.

## 4. Fluxul de update (CI/CD)

```
modifici codul → git push (CI: build + 64 teste)
              → git tag v1.0.1 && git push --tags
              → GitHub Actions "Release": publish linux-arm64 + SHA-256
                                        → GitHub Release v1.0.1
Pe Pi: Admin → "Check for updates" → "Install update"
  aplicația scrie /var/lib/stabilizatorhub/update.requested
  systemd path unit → update.sh (root): download → verify SHA-256
  → extract în releases/v1.0.1 → simlink current → restart → healthz
  → la eșec: rollback automat la versiunea anterioară
```

Manual (fără UI): `sudo /opt/stabilizatorhub/bin/update.sh`.

> Versiunea aplicației vine din tag (`-p:Version=1.0.1` în workflow); tag-ul
> trebuie să fie mai mare decât versiunea curentă ca butonul de update să apară.

## 5. Operare

```bash
journalctl -u stabilizatorhub -f          # logurile aplicației
journalctl -u stabilizatorhub-update      # logurile updater-ului
sqlite3 /var/lib/stabilizatorhub/stabilizatorhub.db '.tables'   # inspecție DB
```

**Backup** (recomandat, cron zilnic): copiază
`/var/lib/stabilizatorhub/` (DB + loguri criptate + cheia logurilor + cheile
Data Protection) pe alt host, de exemplu prin `rsync` peste Tailscale.

## 6. Dezvoltare locală pe Mac

```bash
cd src/StabilizatorHub.Web && dotnet run
# http://localhost:5080, admin dev: admin@local.dev / DevAdmin123!
# MQTT e dezactivat în Development; pentru test cu broker real:
#   brew install mosquitto, pornește-l local și setează Mqtt:Enabled=true
#   + Mqtt:Password în dotnet user-secrets
```

Migrări EF (după schimbări de model):

```bash
dotnet tool install --tool-path .tools dotnet-ef --version "10.0.*"
./.tools/dotnet-ef migrations add NumeMigrare \
  --project src/StabilizatorHub.Infrastructure --startup-project src/StabilizatorHub.Web
# migrarea se aplică automat la pornirea aplicației
```
