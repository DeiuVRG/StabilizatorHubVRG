# Securitate — StabilizatorHubVRG

Prezentare a arhitecturii de securitate a platformei (backend pe Raspberry Pi +
ESP32 + broker MQTT cloud). Documentul descrie *designul* — nu conține adrese,
secrete sau detalii specifice de instalare (acelea stau în dosarul lucrării, în
afara acestui repo public).

## Model de amenințare (pe scurt)

- Dispozitivul comută sarcini de **230 V** → controlul releului trebuie autorizat
  și dispozitivul rămâne sursa de adevăr pentru starea reală.
- Backend-ul e expus pe internet (interfață web) → suprafață de atac web.
- Telemetria trece prin internet (broker cloud) → confidențialitate în tranzit.

## Apărare pe straturi (defense in depth)

### 1. Acces public fără porturi deschise
Aplicația web este publicată printr-un **tunel Cloudflare** (outbound). Backend-ul
ascultă doar pe `127.0.0.1` — **niciun port de aplicație nu este deschis spre
internet**, nu există port-forwarding pe router.

### 2. Administrare prin VPN
Accesul administrativ (SSH) la Raspberry Pi se face printr-o rețea privată
**Tailscale**, nu prin expunere directă.

### 3. Broker MQTT cloud peste TLS
ESP32 și backend-ul se conectează *outbound* la un **broker MQTT în cloud, pe TLS
(port 8883)**. Nu se expune niciun broker local spre internet. Contractul de
topic-uri este `stabilizator/{deviceId}/...`, cu `deviceId = MAC`.

### 4. Sandbox systemd al serviciului
Serviciul aplicației rulează sub un utilizator neprivilegiat, într-un sandbox
systemd strict:

| Directivă | Valoare |
|---|---|
| `User` | utilizator dedicat, neprivilegiat |
| `ProtectSystem` | `strict` (sistem read-only) |
| `ProtectHome` | `yes` |
| `PrivateTmp` | `yes` |
| `NoNewPrivileges` | `yes` |
| `ProtectKernelTunables` / `ProtectKernelModules` / `ProtectControlGroups` | `yes` |
| `RestrictAddressFamilies` | `AF_INET AF_INET6 AF_UNIX` |
| `RestrictNamespaces` / `LockPersonality` | `yes` |
| `CapabilityBoundingSet` | *gol* (zero capabilități de kernel) |

Un eventual compromis al procesului web are astfel o suprafață minimă: fără root,
fără scriere pe sistem, fără capabilități de kernel.
(Vezi `deploy/stabilizatorhub.service`.)

### 5. SSH doar cu cheie
Autentificarea prin parolă este dezactivată (`PasswordAuthentication no`);
se folosesc doar chei publice.

### 6. Firewall
Firewall activ, politică implicită **deny** pe intrare; se permit explicit doar
serviciile necesare.

### 7. Secrete în afara codului
Parolele (broker, cont admin) se află într-un fișier de secrete cu permisiuni
restrictive (`600`), încărcat de systemd ca `EnvironmentFile` — **niciodată
comise în git**. Vezi `deploy/install.sh`.

### 8. Loguri de telemetrie criptate
Logurile de telemetrie sunt criptate **AES-256-GCM**, cu cheie separată și
rotație zilnică (vezi `src/StabilizatorHub.Infrastructure/Logging/`).

### 9. Control autorizat al releului
Comutarea ieșirii (releu SSR) este permisă doar membrilor dispozitivului, iar
firmware-ul pornește mereu cu ieșirea **OPRITĂ** (safe-by-default) și o
energizează doar la comandă explicită **și** doar când există tensiune de rețea.

### 10. Împerechere sigură (claiming)
Dispozitivele se leagă de conturi printr-un cod de împerechere. La eliberare,
firmware-ul **generează un cod nou**, astfel încât un dispozitiv eliberat nu
poate fi re-împerecheat cu codul vechi. Codurile sunt stocate hash-uite.

### 11. Actualizări
- Update-uri de securitate automate pentru sistemul de operare
  (`unattended-upgrades`).
- Backend cu **self-update** semnat prin checksum SHA-256 și **rollback automat**
  la eșecul health-check-ului (vezi `deploy/update.sh`).

## Auditare (read-only)

Starea de securitate a serverului se poate inventaria cu comenzi read-only
(status servicii, porturi în ascultare, sandbox systemd, config SSH/firewall).
Procedura detaliată este documentată în dosarul lucrării.

## Raportarea vulnerabilităților

Proiect academic (licență, UPT). Pentru probleme de securitate, contactați
autorul prin canalele indicate în lucrare.
