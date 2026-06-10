# Arhitectura aplicației StabilizatorHubVRG

## 1. Straturi (Clean Architecture)

Dependențele curg **dinspre exterior spre interior** — niciodată invers:

```
┌────────────────────────────────────────────────────────────┐
│  Web (composition root)                                    │
│  controllere REST · SignalR hub · Identity · middleware    │
│  frontend static (wwwroot)                                 │
│        │ depinde de                                        │
│  ┌─────▼──────────────────────────────────────────┐        │
│  │  Infrastructure                                │        │
│  │  EF Core + SQLite · MQTTnet · loguri criptate  │        │
│  │  GitHub update checker · servicii de fundal    │        │
│  │        │ implementează porturile din           │        │
│  │  ┌─────▼──────────────────────────────┐        │        │
│  │  │  Application                       │        │        │
│  │  │  use case-uri · porturi · DTO-uri  │        │        │
│  │  │        │ depinde doar de           │        │        │
│  │  │  ┌─────▼─────────────────┐         │        │        │
│  │  │  │  Domain               │         │        │        │
│  │  │  │  entități + logică    │         │        │        │
│  │  │  │  pură (zero deps)     │         │        │        │
│  │  │  └───────────────────────┘         │        │        │
│  │  └────────────────────────────────────┘        │        │
│  └────────────────────────────────────────────────┘        │
└────────────────────────────────────────────────────────────┘
```

| Proiect | Conține | Nu are voie să cunoască |
|---|---|---|
| `Domain` | `Device`, `TelemetryReading`, `VoltageEvent`, `AuditEntry`; `VoltageEventTracker` (mașina de stări), `EnergyCalculator`; `IClock` | EF, ASP.NET, MQTT — nimic |
| `Application` | use case-uri (`TelemetryIngestionService`, `DeviceClaimService`, `DeviceControlService`, `ConsumptionService`...), porturi (`IDeviceRepository`, `IDeviceCommandPublisher`, `ITelemetryBroadcaster`, `ITelemetryLogWriter`...), DTO-uri | implementările concrete |
| `Infrastructure` | `AppDbContext` + repository-uri, `MqttConnectionService`, `EncryptedTelemetryLog`, `GitHubUpdateChecker`, `MaintenanceService` | controllerele, UI |
| `Web` | `Program.cs` (DI), controllere, `LiveHub`, `SignalRTelemetryBroadcaster`, middleware de securitate, frontend | — (composition root) |

## 2. Maparea pe principiile SOLID

- **S — Single Responsibility.** Fiecare clasă are un singur motiv de schimbare:
  `VoltageEventTracker` doar decide tranzițiile de episod; `EncryptedTelemetryLog` doar scrie/rotește fișiere; `DeviceClaimService` doar gestionează proprietatea; `ValidateAntiforgeryTokenFilter` doar CSRF. Agregarea SQL stă în `TelemetryRepository`, umplerea golurilor de timeline în `ConsumptionTimeline` (pură, testabilă).
- **O — Open/Closed.** Comportamentele variabile sunt strategii injectate: `ILineCipher` (AES-GCM azi, altă schemă mâine, fără a atinge logul), `IPairingCodeHasher`, `IUpdateChecker`. Pragurile de tensiune vin din configurare (`VoltageMonitorOptions`), nu din cod.
- **L — Liskov.** Orice implementare a unui port respectă contractul interfeței (ex. `FakeDeviceRepository` din teste e substituibilă cu `DeviceRepository` EF fără ca serviciile să observe). Returnările `Task<bool>` la publish definesc explicit comportamentul la eșec.
- **I — Interface Segregation.** Porturi mici și focalizate: `ITelemetryLogWriter` (doar append) separat de `IEncryptedLogReader` (doar citire admin); `IDeviceCommandPublisher` separat de `ITelemetryBroadcaster`; repository per agregat, nu un „IRepository" generic.
- **D — Dependency Inversion.** Stratul Application definește interfețele; Infrastructure le implementează; `Program.cs` le leagă. Nici măcar timpul nu e o dependență ascunsă (`IClock`), ceea ce face logica de lockout/retenție testabilă determinist.

OOP fundamental: încapsulare (starea tracker-ului e privată, expune doar tranziții), abstractizare (porturi), moștenire folosită doar unde aduce valoare (`record`-uri de tranziție derivate din `VoltageTransition`, `IdentityDbContext`), polimorfism prin interfețe.

## 3. Fluxul telemetriei (60 s)

```
ESP32 ──publish──> stabilizator/{id}/telemetrie
                         │
              MqttConnectionService (scope DI per mesaj, procesare secvențială)
                         │ TelemetryPayloadParser (validare + plauzibilitate)
                         ▼
              TelemetryIngestionService
                ├─ înregistrează device-ul necunoscut (unclaimed)
                ├─ EnergyCalculator: E += P × Δt (Δt plafonat la 5 min)
                ├─ persistă TelemetryReading (EF, index pe DeviceId+Timestamp)
                ├─ VoltageEventTracker: started/progressed/ended (histerezis 2 V)
                ├─ SaveChanges (tranzacție unică per mesaj)
                ├─ EncryptedTelemetryLog.Append (AES-GCM, fișier zilnic)
                └─ ITelemetryBroadcaster → SignalR → doar grupul device-ului
```

Detalii de proiectare:
- **Energia se integrează pe server** (`P × Δt`), nu se ia contorul dispozitivului — robust la reboot-uri ESP32; golurile de offline nu sunt creditate (plafon 5 min).
- **Detecția evenimentelor** e o mașină de stări per dispozitiv cu **histerezis** (un episod sub 215 V se închide abia la >217 V), altfel zgomotul de la limită ar genera zeci de episoade. Starea se rehidratează din DB la restart; la offline episodul deschis se închide la momentul ultimei mostre.
- **Agregarea pe ore/zile/luni** se face în SQLite cu `strftime` peste timpul mutat în fusul orar al clientului (`tz` = minute față de UTC trimis de browser), deci „ziua" din grafic e ziua reală a utilizatorului; `ConsumptionTimeline` completează bucket-urile lipsă cu 0.

## 4. Claiming-ul dispozitivelor (asocierea cont ↔ stabilizator)

1. La prima pornire ESP32 își generează: `deviceId` (MAC), un **secret MQTT** și un **cod de împerechere** (6 caractere, fără 0/O/1/I) — toate în NVS.
2. Cât e nerevendicat, codul apare pe **OLED** și e publicat (retained) pe `info`; backend-ul stochează doar **hash-ul PBKDF2** al codului.
3. **Crearea contului cere codul**: register = creare utilizator + claim atomic (utilizatorul se șterge dacă codul e greșit). Un cont nu poate exista fără stabilizator — exact cerința „doar dacă a cumpărat sistemul".
4. La claim: `OwnerUserId` setat, hash-ul șters (cod single-use), mesaj retained `claimed=true` către dispozitiv (OLED trece pe ecranul live).
5. La release: backend-ul publică `claimed=false` → firmware-ul **generează un cod nou** → vechiul proprietar/cod nu mai pot revendica. Anti brute-force: rate limiting per IP + lockout per utilizator (5 încercări / 15 min) + audit.

## 5. Modelul de date

```
AspNet* (Identity)            Devices                     Readings
─ Users (PasswordHash…)       ─ Id (MAC, PK)              ─ Id (PK)
─ Roles ("Admin")             ─ Name                      ─ DeviceId (FK, idx+Timestamp)
                              ─ OwnerUserId (idx)         ─ TimestampUtc
VoltageEvents                 ─ PairingCodeHash           ─ VoltageIn/Out
─ Id (PK)                     ─ FirmwareVersion           ─ CurrentAmps, PowerWatts
─ DeviceId (FK, idx)          ─ IsOnline, OutputOn        ─ EnergyWh (interval)
─ Type (under/over)           ─ LastSeen/LastTelemetryUtc ─ OutputOn
─ StartedAt/EndedAtUtc        AuditEntries
─ ExtremeVoltage, Samples     ─ Timestamp, Action, UserId, DeviceId, Details, Ip
```

## 6. Decizii și compromisuri

- **SQLite** e suficient pentru un singur Pi (1 mostră/min ≈ 525k rânduri/an/dispozitiv) și simplifică backup-ul (un fișier). Retenția raw e configurabilă (`Telemetry:RawRetentionDays`).
- **Cookie auth + CSRF** în loc de JWT: aplicația e same-origin (frontend servit de backend), cookie-urile HttpOnly elimină riscul de furt de token din JS.
- **Procesare MQTT secvențială**: păstrează ordinea per dispozitiv și face tracker-ul de evenimente lipsit de curse de date; la 1 mesaj/min throughput-ul nu e o problemă.
- **Frontend vanilla JS** (cerință): fără build step, servit din `wwwroot`, cu Chart.js și clientul SignalR vendorizate local ca CSP-ul să rămână `'self'`.
