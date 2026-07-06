# StabilizatorHubVRG

**Stabilizator inteligent de tensiune cu monitorizare și control de la distanță**

Proiect de licență — Universitatea Politehnica Timișoara, Facultatea de Automatică
și Calculatoare, specializarea Automatică și Informatică Aplicată (AIA).

**Autor:** Rusu Andrei-Ioan · **Coordonator:** as. dr. ing. Camil-Vasile Jichici

**🔗 Aplicația live:** https://app.licenta-stabilizator-vrg.org

---

## 1. Problema și motivația

În mediul rural, tensiunea din rețeaua electrică variază semnificativ. Standardul
european **EN 50160** admite o abatere de ±10% față de 230 V, dar în practică
abaterile pot fi mai mari și mai frecvente. Un studiu de caz propriu, desfășurat
pe **52 de zile** într-o gospodărie din Șiria (jud. Arad), cu peste **1200 de
măsurători**, a înregistrat tensiuni între **192 V și 242 V**, cu subtensiuni
repetate în intervalele orare de seară — în total ~13 ore de subtensiune.

Produsele comerciale oferă fie stabilizare, fie monitorizare — rar ambele, și
aproape niciodată cu control de la distanță. Acest proiect le combină:

- **stabilizare activă** — un servo-variac menține ieșirea la 230 V ± 1 V;
- **monitorizare** — telemetrie (tensiuni, curent, putere, energie) în timp real
  și istoric, de oriunde;
- **control de la distanță** — pornirea/oprirea ieșirii din aplicația web.

## 2. Arhitectura de ansamblu

Principiul cheie: **toate componentele se conectează doar spre exterior**
(outbound). Dispozitivul și serverul se întâlnesc pe un broker MQTT în cloud,
iar accesul web trece printr-un tunel Cloudflare — **niciun port deschis**,
nicio dependență de rețeaua locală.

```
ESP32 (orice WiFi / hotspot)                     Raspberry Pi (server, acasă)
        │                                                 │
        │ MQTT peste TLS (8883)                           │ MQTT peste TLS (8883)
        ▼                                                 ▼
        └────────────►  HiveMQ Cloud (broker)  ◄──────────┘
                                                          │
                                              Backend ASP.NET Core (.NET 10)
                                                │  EF Core + SQLite
                                                │  SignalR (actualizare live)
                                                │  loguri criptate AES-256-GCM
                                              Cloudflare Tunnel (HTTPS)
                                                          │
                                                      Browser
```

Consecința practică: stabilizatorul funcționează de pe **orice** rețea WiFi sau
hotspot de telefon, indiferent unde se află serverul — fără IP-uri locale, fără
port-forwarding, fără reconfigurare.

## 3. Hardware

Punctul de plecare este un stabilizator cu **autotransformator variabil
(variac)**, din care s-a păstrat partea electromecanică; toată electronica de
comandă a fost reproiectată în jurul unui **ESP32**.

| Componentă | Rol |
|---|---|
| ESP32 (dual-core) | achiziție senzori, reglare, comandă SSR, WiFi/MQTT |
| Variac ROMA-1K + motor DC | reglarea continuă a tensiunii (perie deplasată de motor) |
| BTS7960 (punte H) | comanda motorului, PWM complementar 1 kHz (50% = stop) |
| 2 × ZMPT101B | măsurarea izolată a tensiunilor de intrare/ieșire (RMS) |
| ACS712-20A (efect Hall) | măsurarea izolată a curentului de sarcină |
| SSR-25 DA | comutarea sarcinii (releu static, izolat optic) |
| OLED SSD1306 (I²C) | afișaj local: tensiuni, stare, cod de împerechere |
| Limitatoare de capăt | protecție electromecanică a cursei variacului, independentă de software |

Detalii de proiectare importante:

- **Izolare galvanică completă** între zona de 230 V și zona de comandă (SELV):
  senzorii ZMPT/ACS712 și SSR-ul sunt izolați; partea de comandă nu atinge
  niciodată rețeaua.
- **Toți senzorii sunt pe ADC1** (GPIO32/34/35): pe ESP32, ADC2 este ocupat de
  radioul WiFi, deci ar da citiri corupte cu WiFi pornit.
- Măsurarea tensiunilor/curentului se face prin **RMS pe fereastră de 60 ms**
  (3 perioade la 50 Hz), din varianța eșantioanelor ADC — elimină natural
  offset-ul DC al senzorilor.

## 4. Firmware-ul ESP32 (`firmware/stabilizator_esp32/`)

### Reglarea (bucla de control)

Regulator cu **bandă moartă** (deadband ± 1 V în jurul țintei de 230 V) cu
viteză proporțională cu eroarea și histerezis anti-oscilație. Protecții:
- sub **120 V la intrare** → considerat „fără rețea": motor oprit + SSR tăiat
  (fail-safe);
- **protecție la blocaj (stall)**: dacă motorul rulează continuu peste 30 s fără
  să atingă banda, comanda se oprește (limitatoarele hardware rămân protecția
  reală);
- ieșirea pornește mereu **OPRITĂ** după boot (safe-by-default) și se
  energizează doar la comandă explicită și doar dacă există tensiune de rețea.

### Arhitectura pe două nuclee

Tot ce ține de rețea (portal WiFi, reconectări, TLS, MQTT) rulează într-un task
FreeRTOS pe **core 0**; reglajul, senzorii și siguranța SSR rămân pe **core 1**
(bucla Arduino). Un broker căzut sau lipsa WiFi **nu pot bloca niciodată bucla
de control de 230 V**. Starea partajată între nuclee este `volatile` (scalari,
atomici pe 32 de biți) sau protejată cu mutex (NVS, string-ul de împerechere).

### Conectivitate

- **WiFiMulti + portal captiv:** dispozitivul memorează până la 5 rețele WiFi în
  NVS și se conectează automat la cea mai puternică din rază (ex. router acasă,
  hotspot la prezentare). Prima configurare se face din telefon, prin portalul
  captiv `Stabilizator-Setup`, unde se introduc și parametrii brokerului
  (nimic secret în cod).
- **MQTT peste TLS** către HiveMQ Cloud; identitatea dispozitivului este MAC-ul
  (citit din eFuse), folosit în topic-uri și ca `clientId`.
- **Prezență prin LWT:** dacă dispozitivul dispare brusc, brokerul publică
  automat `offline` — serverul află imediat.
- **Telemetrie la 10 s** pentru o interfață reactivă; backend-ul stochează
  ~1 citire/minut (restul actualizează doar UI-ul live).

### Calibrare (persistentă în NVS)

Prin comenzi seriale, contra unui aparat de referință: `in <V>`, `out <V>`
(tensiuni), `cur_zero` (captează zgomotul de fond fără sarcină, scăzut apoi în
cuadratură din fiecare citire) și `cur <A>` (amplitudine, sub sarcină
cunoscută). Valorile supraviețuiesc restartului și reflash-ului.

### Împerecherea (claiming)

Cât timp e neclamat, dispozitivul afișează pe OLED un **cod de împerechere**;
utilizatorul îl introduce în aplicație. La eliberare, firmware-ul generează un
cod **nou** — un dispozitiv eliberat/vândut nu poate fi re-împerecheat cu codul
vechi.

## 5. Contractul MQTT

```
stabilizator/{MAC}/telemetrie  →  {"vin":228,"vout":230,"i":3.10,"p":713,
                                   "e":12.40,"out":1,"fw":"2.0.0"}    (10 s)
stabilizator/{MAC}/status      →  "online" / "offline"     (retained, LWT)
stabilizator/{MAC}/info        →  {"pair":"7F3K9Q","fw":"2.0.0"}  (retained)
stabilizator/{MAC}/comanda     ←  {"output":"on"|"off"}    (comanda releului)
stabilizator/{MAC}/claimed     ←  "true" / "false"         (retained)
```

## 6. Platforma web (`src/`)

Backend ASP.NET Core (.NET 10) cu **arhitectură curată pe patru straturi** —
dependențele curg doar spre interior, ceea ce face aplicația ușor de testat:

```
src/
├── StabilizatorHub.Domain/          # entități + logică pură (zero dependențe)
├── StabilizatorHub.Application/     # use case-uri, porturi (interfețe), DTO-uri
├── StabilizatorHub.Infrastructure/  # EF Core + SQLite, MQTTnet (TLS), loguri
│                                    # criptate, self-update prin GitHub Releases
└── StabilizatorHub.Web/             # API REST, SignalR, Identity, frontend static
```

Funcționalități: tablou de bord live (SignalR), control releu instant, grafice
de consum pe ore/zile/luni, evenimente de sub/supratensiune (≤215 V / ≥240 V),
multi-utilizator de tip „familie" (owner + membri prin coduri de invitație),
audit trail, panou de administrare, mod demo read-only cu date simulate.

## 7. Securitate (pe scurt — detalii în [SECURITY.md](SECURITY.md))

- acces public **doar** prin Cloudflare Tunnel — zero porturi deschise;
- administrare doar prin VPN privat (Tailscale); SSH exclusiv cu chei;
- MQTT criptat TLS, broker cu autentificare;
- parole stocate hash-uit (PBKDF2 + salt) cu blocarea contului la încercări repetate;
- loguri de telemetrie criptate **AES-256-GCM** cu rotație zilnică a fișierelor;
- serviciul rulează într-un **sandbox systemd** strict (utilizator neprivilegiat,
  `ProtectSystem=strict`, zero capabilități de kernel);
- izolare completă a datelor între conturi.

## 8. Compilare și rulare locală

```bash
git clone https://github.com/DeiuVRG/StabilizatorHubVRG.git
cd StabilizatorHubVRG
dotnet build -c Release          # necesită .NET SDK 10
dotnet test                      # suita de 78 de teste xUnit

cd src/StabilizatorHub.Web
dotnet run                       # http://localhost:5080
```

În mediul Development conectivitatea MQTT este dezactivată, deci aplicația
poate fi explorată **fără hardware și fără broker** (inclusiv modul demo).

**Firmware:** se deschide `firmware/stabilizator_esp32/stabilizator_esp32.ino`
în Arduino IDE (placă „ESP32 Dev Module"; biblioteci: WiFiManager, PubSubClient,
ArduinoJson, Adafruit GFX, Adafruit SSD1306).

## 9. Deploy și întreținere (Raspberry Pi)

```bash
ssh -t <user>@<pi> sudo ./deploy/setup-all.sh
```

Sistemul se întreține singur: la fiecare tag `vX.Y.Z`, CI-ul rulează testele și
publică un Release cu artefactul `linux-arm64`; serverul se **auto-actualizează**
cu verificare criptografică (SHA-256) și **rollback automat** dacă health-check-ul
eșuează.

## 10. Structura repository-ului

```
firmware/stabilizator_esp32/   # firmware ESP32 (Arduino)
src/                           # backend + frontend (arhitectură curată, 4 straturi)
tests/StabilizatorHub.Tests/   # 78 de teste xUnit
deploy/                        # systemd + scripturi de instalare/actualizare
.github/workflows/             # CI (build+teste) și Release (linux-arm64)
SECURITY.md                    # arhitectura de securitate
```

Documentația tehnică detaliată (schema electrică, studiul de caz, testarea,
rezultatele experimentale) se află în dosarul lucrării de licență.

---
*Autor: Rusu Andrei-Ioan · Coordonator: as. dr. ing. Camil-Vasile Jichici ·
UPT, Automatică și Calculatoare, 2026*
