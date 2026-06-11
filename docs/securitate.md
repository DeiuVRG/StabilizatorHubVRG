# Securitatea aplicației StabilizatorHubVRG

Apărare pe straturi — aplicația comandă un echipament la 230 V, deci securitatea
nu e opțională. Acest document acoperă stratul aplicației; stratul de sistem
(SSH, firewall, Cloudflare, Tailscale) este documentat separat, în
documentația de configurare a serverului din dosarul lucrării.

## 1. Autentificare și conturi

- **Parolele nu se stochează niciodată** — ASP.NET Core Identity persistă doar
  hash PBKDF2 cu salt unic per utilizator (`AspNetUsers.PasswordHash`).
- **Politică de parolă**: minim 10 caractere, literă mare + mică + cifră.
- **Lockout de cont**: 5 încercări eșuate → cont blocat 15 minute
  (`auth.login.lockout` în audit).
- **Rate limiting per IP**: politica `auth` (10 cereri/min) pe login, register
  și claim; limită globală 300 cereri/min/IP — protejează și Pi-ul de flood.
- **Mesaje de eroare generice** la login („Invalid email or password") — nu se
  divulgă existența unui cont.
- Cookie de sesiune: **HttpOnly** (inaccesibil din JS), **SameSite=Lax**,
  expirare 7 zile cu reînnoire glisantă; răspunsuri **401/403 JSON** (fără
  redirect-uri de tip HTML).
- Cheile Data Protection sunt **persistate** în directorul de date → sesiunile
  supraviețuiesc restart-urilor și update-urilor.

## 2. CSRF (cookie-to-header)

Toate metodele care schimbă stare cer headerul `X-XSRF-TOKEN` egal cu tokenul
emis în cookie-ul `XSRF-TOKEN` (citibil de JS, emis după autentificare, legat
de identitate). Validarea se face în `ValidateAntiforgeryTokenFilter` pe toate
controllerele. SameSite=Lax pe cookie-ul de sesiune e al doilea strat.

## 3. Autorizare și izolare multi-tenant

- Toate endpoint-urile de date cer autentificare; cele de administrare cer
  rolul `Admin` (seed-uit din configurare, nu din cod).
- **Verificarea de proprietate e centralizată** în `DeviceAccessService`:
  orice operație pe un dispozitiv (telemetrie, istoric, releu, redenumire)
  trece prin ea. Răspunsul pentru „nu există" și „nu e al tău" este identic —
  nu se poate enumera ce dispozitive există.
- SignalR: clienții sunt adăugați **doar în grupurile dispozitivelor proprii**
  (verificare și la `JoinDevice`).

## 4. Claiming și invitații — protecția codurilor

- Codul de împerechere (6 caractere din 32 → ~10^9 combinații) e stocat **doar
  ca hash PBKDF2 cu salt** (100k iterații); e **single-use** (hash-ul se șterge
  la claim) și **se regenerează** la release — un dispozitiv vândut nu poate fi
  revendicat cu codul vechi.
- Codurile de **invitație** (membri ai casei): 8 caractere, tot hash PBKDF2,
  **expiră în 48 h**, maximum 10 utilizări, cel mult 5 active per dispozitiv,
  pot fi create **doar de owner**; invitațiile expirate sunt șterse zilnic.
- Online brute-force blocat pentru ambele: rate limiting per IP + lockout per
  utilizator (5 eșecuri/15 min, `InMemoryClaimAttemptLimiter`) + audit la
  fiecare eșec (un singur „strike" per încercare, indiferent de tipul codului).
- Un dispozitiv deja revendicat nu apare în candidații de claim, indiferent de
  cod; accesul ulterior e guvernat de rolul din `DeviceMemberships`
  (owner: gestiune completă; member: vizualizare + releu).

## 5. Transport și headere

- HTTPS este terminat de **Cloudflare** (tunel outbound; aplicația ascultă doar
  pe `127.0.0.1:5000`).
- `SecurityHeadersMiddleware`: **CSP strict** (`default-src 'self'` — Chart.js
  și SignalR sunt servite local, zero resurse externe), `X-Content-Type-Options:
  nosniff`, `X-Frame-Options: DENY` + `frame-ancestors 'none'`,
  `Referrer-Policy`, `Permissions-Policy`.
- Corpul cererilor limitat la 64 KB (payload-urile reale sunt minuscule).

## 6. MQTT

- `allow_anonymous false`; backend-ul are utilizatorul `backend`, fiecare
  dispozitiv are **propriul utilizator = deviceId** cu parolă secretă generată
  la prima pornire.
- **ACL**: `pattern readwrite stabilizator/%u/#` — un dispozitiv compromis vede
  doar topicurile lui; browserele nu ating brokerul deloc.
- Brokerul e doar pe LAN (firewall), nu e expus pe internet.
- Backend-ul **validează** orice mesaj: formă topic + deviceId (alfanumeric,
  4–32), JSON corect, valori plauzibile (V ≤ 1000, P ≤ 50 kW) — un payload
  otrăvit e aruncat, nu doboară pipeline-ul.

## 7. Loguri criptate de telemetrie

- Fiecare linie CSV e sigilată individual cu **AES-256-GCM** (criptare
  autentificată: nici citire, nici modificare nedetectată). Format:
  `base64(nonce | tag | ciphertext)`.
- **Rotație zilnică** (`telemetry-YYYYMMDD.csv.enc`) + **retenție** (implicit
  90 de zile, configurabil).
- Cheia: din configurare (`TelemetryLog:KeyBase64`) sau generată la prima
  rulare în fișier cu permisiuni **0600** în directorul de date.
- Decriptarea e posibilă doar pentru admin, prin API, și e **auditată**
  (`system.log_decrypted`).
- Limitare asumată: cheia stă pe același host (acceptabil pentru un singur Pi;
  o extensie ar muta-o într-un KMS/HSM).

## 8. Audit trail

`AuditEntries` reține: register/login (reușit, eșuat, lockout), schimbare de
parolă, claim (reușit/eșuat), release, **fiecare comandă de releu**, cererea de
update și decriptarea logurilor — cu utilizator, dispozitiv, IP și timestamp.
Vizibil în pagina de admin.

## 9. Update sigur (separare de privilegii)

Aplicația **nu se actualizează singură și nu rulează nimic privilegiat**: adminul
declanșează → aplicația scrie un fișier-trigger în directorul ei de date →
`stabilizatorhub-update.path` (systemd, root) pornește updater-ul care
descarcă release-ul de pe GitHub, **verifică SHA-256**, schimbă atomic
simlink-ul `current` și repornește serviciul; dacă health-check-ul pică, face
**rollback** la versiunea anterioară.

## 10. Hardening systemd (deploy/stabilizatorhub.service)

`NoNewPrivileges`, `ProtectSystem=strict` (sistem de fișiere read-only, doar
`StateDirectory` scriibil), `ProtectHome`, `PrivateTmp`,
`ProtectKernel*`, `RestrictAddressFamilies`, `CapabilityBoundingSet=` (zero
capabilități), utilizator dedicat `stabhub` fără shell.

## 11. Secrete

Nimic sensibil în repo: parola MQTT, contul de admin și orice override stau în
`/etc/stabilizatorhub/secrets.env` (0600, citit de systemd prin
`EnvironmentFile`). În dezvoltare: `appsettings.Development.json` cu valori
de test + broker dezactivat.
