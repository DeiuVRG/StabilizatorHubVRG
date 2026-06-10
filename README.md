# StabilizatorHubVRG

Aplicație web de monitorizare și control pentru un **stabilizator de tensiune inteligent** construit pe ESP32 — proiect de licență (UPT, Automatică și Calculatoare).

**Live:** https://app.licenta-stabilizator-vrg.org

```
ESP32 ──MQTT──> Mosquitto (Raspberry Pi) ──> Backend ASP.NET Core (.NET 10)
                                                   │  EF Core + SQLite
                                                   │  SignalR (live)
                                       Cloudflare Tunnel (HTTPS)
                                                   │
                                               Browser
```

## Funcționalități

- **Telemetrie la 60 s** de la ESP32 (tensiune intrare/ieșire, curent, putere) prin MQTT, afișată **live** în browser prin SignalR.
- **Conturi de utilizator** (ASP.NET Core Identity) — un cont se poate crea **doar cu un cod valid**: codul de împerechere de pe OLED (dovada posesiei fizice → devii **owner**) sau un **cod de invitație** generat de owner (membrii familiei își fac propriile conturi pe același dispozitiv). Un dispozitiv revendicat nu mai poate fi atașat prin pairing până când owner-ul nu îl eliberează (moment în care firmware-ul generează un **cod nou**).
- **Acces multi-utilizator pe dispozitiv**: owner-ul invită membrii casei cu un cod valabil 48 h; membrii văd consumul și pot comanda releul; doar owner-ul redenumește, invită, scoate membri sau eliberează dispozitivul.
- **Monitorizarea consumului** pe ore / zile / săptămâni / luni (agregare SQL pe SQLite), grafice Chart.js + sumar zilnic/săptămânal/lunar.
- **Evenimente de tensiune**: episoadele de **subtensiune ≤ 215 V** și **supratensiune ≥ 240 V** la intrare sunt detectate (cu histerezis), persistate, notificate live și listate în dashboard.
- **Control releu (SSR) de la distanță** — pornește/oprește ieșirea stabilizatorului, cu confirmare în UI și starea reală raportată înapoi de dispozitiv.
- **Loguri de telemetrie criptate** (AES-256-GCM pe fiecare linie), **rotite zilnic**, cu retenție configurabilă; adminul le poate descărca decriptat (acțiune auditată).
- **Sistem de update**: tag `vX.Y.Z` → GitHub Actions publică artefactul `linux-arm64` → adminul apasă „Install update" → un serviciu systemd separat descarcă, verifică SHA-256, schimbă atomic simlink-ul și repornește aplicația (cu rollback la health-check eșuat).
- **Audit trail** pentru acțiunile sensibile (login, claim, comandă releu, update).

## Securitate (rezumat)

Parole doar hash-uite (PBKDF2, Identity) · lockout la cont + rate limiting per IP · cookie HttpOnly/SameSite + protecție CSRF (cookie-to-header) · verificare de proprietate la fiecare endpoint · CSP strict fără resurse externe · headere de securitate · MQTT cu autentificare + ACL per dispozitiv · systemd sandboxing · separare de privilegii la update. Detalii: [docs/securitate.md](docs/securitate.md).

## Structura repo

```
src/
├── StabilizatorHub.Domain/          # entități + logică pură (zero dependențe)
├── StabilizatorHub.Application/     # use case-uri, porturi (interfețe), DTO-uri
├── StabilizatorHub.Infrastructure/  # EF Core+SQLite, MQTTnet, loguri criptate, updater
└── StabilizatorHub.Web/             # API, SignalR, Identity, frontend static (wwwroot)
tests/StabilizatorHub.Tests/         # 64 teste xUnit
firmware/stabilizator_esp32/         # firmware-ul ESP32 (Arduino)
deploy/                              # systemd + scripturi install/update
.github/workflows/                   # CI + Release
docs/                                # documentație detaliată
```

Arhitectura pe straturi și maparea pe principiile **SOLID**: [docs/arhitectura.md](docs/arhitectura.md).

## Dezvoltare locală (fără broker)

```bash
cd src/StabilizatorHub.Web
dotnet run
# http://localhost:5080 — admin dev: admin@local.dev / DevAdmin123!
# (Mqtt:Enabled=false în appsettings.Development.json)
```

Teste: `dotnet test`

## Deploy pe Raspberry Pi

Pe scurt (detalii în [docs/deploy-raspberry-pi.md](docs/deploy-raspberry-pi.md)):

```bash
# pe Pi, din directorul deploy/ al repo-ului:
sudo ./install.sh        # user de serviciu, unit-uri systemd, ultima versiune publicată
sudo nano /etc/stabilizatorhub/secrets.env   # parola MQTT + contul de admin
sudo systemctl restart stabilizatorhub
```

Serverul (OS, Mosquitto, Cloudflare Tunnel, Tailscale, hardening) este documentat în
[docs/server-raspberry-pi-documentatie-completa.md](docs/server-raspberry-pi-documentatie-completa.md).

## Integrarea ESP32

Contractul MQTT (topicuri, payload-uri, claiming, provisioning broker) este specificat în
[docs/esp32-integrare.md](docs/esp32-integrare.md); firmware-ul de referință este în
[firmware/stabilizator_esp32/](firmware/stabilizator_esp32/).

---
*Autor: Rusu Andrei-Ioan · Coordonator: ș.l. dr. ing. Camil Jichici*
