# StabilizatorHubVRG

Aplicatie web de monitorizare si control pentru un stabilizator de tensiune
inteligent construit pe ESP32 — proiect de licenta (UPT, Automatica si
Calculatoare).

**Live:** https://app.licenta-stabilizator-vrg.org

```
ESP32 ──MQTT──> Mosquitto (Raspberry Pi) ──> Backend ASP.NET Core (.NET 10)
                                                  │  EF Core + SQLite
                                                  │  SignalR (live)
                                      Cloudflare Tunnel (HTTPS)
                                                  │
                                              Browser
```

Functionalitati: telemetrie la 60 s cu actualizare live, conturi legate de
dispozitiv prin cod de imperechere (multi-utilizator: owner + membri prin coduri
de invitatie), grafice de consum pe ore/zile/luni, evenimente de sub/supratensiune
(<=215 V / >=240 V), control releu SSR de la distanta, loguri de telemetrie
criptate AES-256-GCM cu rotatie zilnica, audit trail, sistem de self-update prin
GitHub Releases si mod demo read-only cu date simulate.

## Structura

```
src/
├── StabilizatorHub.Domain/          # entitati + logica pura (zero dependente)
├── StabilizatorHub.Application/     # use case-uri, porturi (interfete), DTO-uri
├── StabilizatorHub.Infrastructure/  # EF Core+SQLite, MQTTnet, loguri criptate, updater
└── StabilizatorHub.Web/             # API, SignalR, Identity, frontend static (wwwroot)
tests/StabilizatorHub.Tests/         # 78 teste xUnit
firmware/stabilizator_esp32/         # firmware ESP32 (Arduino)
deploy/                              # systemd + scripturi de instalare/update
.github/workflows/                   # CI + Release
```

## Dezvoltare locala

```bash
cd src/StabilizatorHub.Web
dotnet run        # http://localhost:5080 (MQTT dezactivat in Development)
dotnet test       # ruleaza suita de teste
```

## Deploy pe Raspberry Pi

```bash
ssh -t <user>@<pi> sudo ./deploy/setup-all.sh
```

Documentatia tehnica detaliata (arhitectura, securitate, integrare ESP32,
deploy, testare) se afla in dosarul lucrarii de licenta.

---
*Autor: Rusu Andrei-Ioan · Coordonator: s.l. dr. ing. Camil Jichici*
