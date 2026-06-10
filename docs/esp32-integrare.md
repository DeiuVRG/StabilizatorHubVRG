# Integrarea ESP32 — contractul MQTT

Firmware-ul de referință: [firmware/stabilizator_esp32/](../firmware/stabilizator_esp32/).
Orice firmware care respectă contractul de mai jos funcționează cu backend-ul.

## 1. Identitate și topicuri

- `deviceId` = adresa MAC fără `:` (ex. `A1B2C3D4E5F6`) — unică, gravată în chip.
- Toate topicurile au forma `stabilizator/{deviceId}/{leaf}`:

| Topic (leaf) | Direcție | Payload | Retained |
|---|---|---|---|
| `telemetrie` | ESP → server | `{"vin":228.4,"vout":230.1,"i":3.10,"p":713.0,"e":12.40,"out":1,"fw":"1.1.0"}` | nu |
| `status` | ESP → server | `online` / `offline` (LWT) | **da** |
| `info` | ESP → server | `{"pair":"7F3K9Q","fw":"1.1.0"}` — `pair` doar cât e nerevendicat | **da** |
| `comanda` | server → ESP | `{"output":"on"}` / `{"output":"off"}` | nu |
| `claimed` | server → ESP | `true` / `false` | **da** |

Câmpuri telemetrie: `vin`/`vout` [V], `i` [A], `p` [W], `e` [kWh cumulativ de la
boot — informativ; serverul își integrează singur energia], `out` (0/1 starea
SSR), `fw` (versiunea firmware). `i`, `p`, `e`, `out`, `fw` sunt opționale —
backend-ul acceptă și payload-uri minimale `{"vin":...,"vout":...}`.

**Cadența telemetriei: 60 de secunde** (cerința proiectului). După o comandă de
releu firmware-ul publică imediat o telemetrie suplimentară, ca dashboard-ul să
confirme instant.

## 2. Ciclul de viață

```
boot → WiFiManager (portal "Stabilizator-Setup" la prima pornire)
     → MQTT connect (user=deviceId, pass=secret din NVS, LWT="offline" retained)
     → publish "online" (retained) + info {pair,fw} (retained)
     → la 60 s: telemetrie
     → la comanda {"output":...}: comută SSR + telemetrie imediată
     → la claimed=true: salvează flag, OLED trece pe ecranul live, info fără pair
     → la claimed=false: GENEREAZĂ COD NOU, OLED revine la ecranul de împerechere
```

Bucla de reglare a variacului rulează **independent de rețea** (fail-safe local);
limitatoarele hardware protejează capetele de cursă indiferent de software.

## 3. Detecția online/offline

1. **LWT**: la deconectare bruscă brokerul publică `offline` (retained) în
   numele dispozitivului → backend-ul îl marchează imediat.
2. **Plasă de siguranță**: `MaintenanceService` marchează offline orice
   dispozitiv tăcut mai mult de `Telemetry:OfflineAfterSeconds` (implicit 180 s
   = 3 mostre ratate) — acoperă cazul în care pică tot LAN-ul.
   La offline, episodul de tensiune rămas deschis se închide la momentul
   ultimei mostre.

## 4. Provisioning-ul unui dispozitiv nou (o singură dată)

1. Flashează firmware-ul; deschide Serial Monitor (115200). La boot se afișează:
   `deviceId`, `secret` (parola MQTT), `pairing code`.
2. Pe Raspberry Pi, creează contul de broker al dispozitivului:
   ```bash
   sudo mosquitto_passwd /etc/mosquitto/passwd <deviceId>   # parola = secretul afișat
   sudo systemctl restart mosquitto
   ```
   ACL-ul existent (`pattern readwrite stabilizator/%u/#`) îl izolează automat
   la propriile topicuri.
3. Configurează WiFi prin portalul captiv `Stabilizator-Setup` (telefon).
4. Dispozitivul apare ca *unclaimed* (vizibil în pagina de admin); pe OLED stă
   codul de împerechere → utilizatorul își creează cont cu el (sau „+ Add
   device" dacă are deja cont).

## 5. Calibrare senzori (obligatoriu pe hardware-ul real)

În firmware: `CAL_VIN`, `CAL_VOUT` (raport V_rețea / V_rms_la_pin — măsoară cu
multimetrul și ajustează), `DIVIDER_RATIO` (divizorul de pe ieșirea ACS712),
`ACS_SENS` (0.100 V/A la ACS712-20A). Atenție la potențiometrul modulelor ZMPT:
fără clipping la ~250 V.

## 6. Pinout (conform schemei proiectului)

| Funcție | GPIO |
|---|---|
| ZMPT101B #1 — tensiune intrare | 34 |
| ZMPT101B #2 — tensiune ieșire | 35 |
| ACS712 — curent (prin divizor) | 36 |
| BTS7960 RPWM / LPWM / EN | 25 / 26 / 27 |
| SSR (RELAY_IN) | 14 |
| OLED SDA / SCL | 21 / 22 |
