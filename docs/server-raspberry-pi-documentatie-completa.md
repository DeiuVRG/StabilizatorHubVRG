# Server Raspberry Pi — Documentație completă de configurare
### Proiect licență: sistem de monitorizare stabilizator de tensiune

> Acest document descrie, pas cu pas, cum a fost instalat și configurat serverul Raspberry Pi care găzduiește aplicația web a proiectului — de la sistemul de operare, până la module și securitate. Pentru fiecare bucată sunt trecute **comenzile rulate** și **la ce ajută**.

**Data:** 10 iunie 2026

---

## 0. La ce folosește serverul

Un Raspberry Pi care rulează 24/7 acasă și găzduiește aplicația de monitorizare a stabilizatorului. Fluxul complet:

```
ESP32  ──MQTT──>  Mosquitto (broker pe Pi)  ──>  Backend ASP.NET Core (pe Pi)
                                                        │
                                            Cloudflare Tunnel (HTTPS)
                                                        │
                                                    Browser (oriunde)
```

- **ESP32** trimite telemetria stabilizatorului prin MQTT.
- **Mosquitto** = brokerul MQTT, primește mesajele.
- **Backend ASP.NET Core** = citește din MQTT, salvează în bază de date, servește frontend-ul și trimite date live în browser (SignalR).
- **Cloudflare Tunnel** = expune aplicația pe internet, securizat (HTTPS), fără port forwarding pe router.
- **Tailscale** = acces SSH privat la Pi de oriunde (pentru administrare/deploy).

---

## 1. Hardware

| Componentă | Detaliu |
|---|---|
| Plăcuță | Raspberry Pi 3 Model B |
| RAM | 1 GB |
| Arhitectură | aarch64 (ARMv8 64-bit) |
| WiFi | **doar 2.4 GHz** (nu prinde 5 GHz) |
| Ethernet | 100 Mbit |
| Card | microSD ~128 GB |
| Calculator pt. setare | MacBook Air (Apple Silicon, fără slot SD → folosit adaptor) |

---

## 2. Sistemul de operare — instalare

**Ales:** Raspberry Pi OS Lite (64-bit), bazat pe Debian Trixie.
- *De ce Lite:* server fără monitor/tastatură (headless), fără interfață grafică → consumă puțin pe doar 1 GB RAM.
- *De ce 64-bit:* Pi 3 e pe arhitectură aarch64, iar .NET pe arm64 are nevoie de OS 64-bit.

**Scris pe card** cu **Raspberry Pi Imager** (pe Mac). În setările avansate din Imager:
- hostname: `StabilizatorVRG`
- utilizator: `deiuvrg`
- SSH: activat (la început cu parolă — dezactivată ulterior, vezi cap. 9)
- WiFi configurat (rețeaua de acasă)

După primul boot, Pi-ul s-a conectat singur la WiFi; ne-am conectat prin SSH.

---

## 3. Rețea, locale, update sistem

**WiFi de acasă:** SSID `VRG - Inernet Pentru Saraci` (2.4 GHz). *(Parola e doar la tine.)*

**Dezactivare „power save" la WiFi** (Pi 3 are tendința să piardă conexiunea când stă; asta o stabilizează):
```bash
sudo tee /etc/NetworkManager/conf.d/wifi-powersave-off.conf >/dev/null <<'EOF'
[connection]
wifi.powersave = 2
EOF
```
*La ce ajută:* `2` = power save oprit → WiFi-ul rămâne stabil 24/7.

**Locale** (ca să nu apară avertismente la SSH):
```bash
sudo update-locale LANG=C.UTF-8
```
> Notă: avertismentele `setlocale` veneau de la Terminalul de pe Mac care trimitea variabilele de limbă. Fix opțional pe Mac: Terminal → Settings → Profiles → Advanced → debifează „Set locale environment variables on startup".

**Actualizare sistem** (kernel ajuns la 6.18.33):
```bash
sudo apt update
sudo apt full-upgrade -y
```
*La ce ajută:* aduce toate pachetele și kernelul la zi (corecturi de securitate și stabilitate).

---

## 4. Memorie — zram

`zram` creează „swap" comprimat în RAM → ajută un sistem cu doar 1 GB să nu rămână fără memorie.

**Important:** Raspberry Pi OS oferă deja zram din fabrică, prin serviciul `systemd-zram-setup@zram0.service` (~905 MB activ). Pachetul separat `zram-tools` era **redundant** și intra în conflict.

**Comenzi (dezactivarea pachetului redundant, păstrând zram-ul din OS):**
```bash
sudo systemctl disable zramswap
sudo systemctl reset-failed zramswap
```
*La ce ajută:* oprește serviciul duplicat `zramswap` și curăță starea de eroare, lăsând activ zram-ul nativ al OS-ului.

> ⚠️ Nu dezinstala (`purge`) pachetul `zram-tools` — la oprire ar face `swapoff` pe zram-ul activ. Lasă-l instalat-dar-dezactivat.

**Verificare:**
```bash
zramctl
swapon --show
```

---

## 5. Runtime aplicație — .NET (ASP.NET Core)

Pe Pi se instalează doar **runtime-ul** (ca să ruleze aplicația), nu SDK-ul (compilarea se face pe Mac).

**Dependențe + scriptul oficial:**
```bash
sudo apt-get install -y curl libicu-dev
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
sudo bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /opt/dotnet
sudo ln -sf /opt/dotnet/dotnet /usr/bin/dotnet
```
*La ce ajută fiecare:*
- `libicu-dev` = bibliotecă de internaționalizare de care .NET are nevoie pe Linux.
- `dotnet-install.sh --runtime aspnetcore` = instalează runtime-ul ASP.NET Core (include și runtime-ul .NET de bază).
- `ln -sf ... /usr/bin/dotnet` = face comanda `dotnet` disponibilă peste tot.

**Verificare:**
```bash
dotnet --info
```
Rezultat confirmat: `Microsoft.AspNetCore.App 10.0.9` și `Microsoft.NETCore.App 10.0.9`, arhitectură `arm64`, fără SDK (corect).

> **Versiune instalată: .NET 10.0.9 (LTS).** Pentru decizia net9.0 vs net10.0 la backend, vezi cap. 11.

---

## 6. Broker MQTT — Mosquitto

**Instalare:**
```bash
sudo apt-get install -y mosquitto mosquitto-clients
```

**Configurare** `/etc/mosquitto/conf.d/stabilizator.conf`:
```bash
sudo tee /etc/mosquitto/conf.d/stabilizator.conf >/dev/null <<'EOF'
listener 1883 0.0.0.0
allow_anonymous false
password_file /etc/mosquitto/passwd
acl_file /etc/mosquitto/aclfile
EOF
```
*La ce ajută fiecare linie:*
- `listener 1883 0.0.0.0` = ascultă pe rețeaua locală, **nu doar localhost**. Necesar fiindcă **ESP-ul e un dispozitiv separat pe WiFi** și trebuie să ajungă la broker. (Documentația originală sugera `127.0.0.1`, valabil doar dacă ESP-ul ar fi pe același Pi — la noi nu e cazul.)
- `allow_anonymous false` = nimeni nu se conectează fără user/parolă.
- `password_file` / `acl_file` = fișierele cu utilizatori și cu permisiuni pe topicuri.

> Securitate: portul 1883 e doar pe rețeaua locală (nu e expus pe internet — Cloudflare expune doar aplicația web, nu MQTT). Cu autentificare + ACL, e sigur pe LAN.

**Creare utilizator `backend`** (cu el se conectează aplicația .NET):
```bash
sudo mosquitto_passwd -c /etc/mosquitto/passwd backend
```
*(cere o parolă de două ori — setată de tine; o folosești în config-ul backend-ului)*

**Fișier ACL** `/etc/mosquitto/aclfile`:
```bash
sudo tee /etc/mosquitto/aclfile >/dev/null <<'EOF'
user backend
topic readwrite stabilizator/#

pattern readwrite stabilizator/%u/#
EOF
```
*La ce ajută:*
- `user backend` + `topic readwrite stabilizator/#` = backend-ul citește/scrie pe toate topicurile `stabilizator/...`.
- `pattern readwrite stabilizator/%u/#` = fiecare dispozitiv (utilizator) are acces doar la propriul subarbore de topicuri (`%u` = numele lui). Izolare între dispozitive.

**Permisiuni** (ca brokerul să poată citi fișierele):
```bash
sudo chown mosquitto:mosquitto /etc/mosquitto/passwd /etc/mosquitto/aclfile
sudo chmod 600 /etc/mosquitto/passwd /etc/mosquitto/aclfile
```

**Repornire + verificare:**
```bash
sudo systemctl restart mosquitto
ss -tln | grep 1883          # trebuie 0.0.0.0:1883

# test fără parolă → trebuie RESPINS:
mosquitto_pub -h localhost -t 'stabilizator/test/telemetrie' -m 'hi'
# test cu backend → trebuie ACCEPTAT:
mosquitto_pub -h localhost -t 'stabilizator/test/telemetrie' -u backend -P 'PAROLA' -m '{"vin":228}'
```
Rezultat confirmat: ascultă pe `0.0.0.0:1883`, anonim respins, `backend` acceptat. ✅

**Topicuri MQTT (convenția proiectului):** `stabilizator/{deviceId}/{telemetrie | status | info | comanda | claimed}`

---

## 7. Acces public — Cloudflare Tunnel

Expune aplicația pe internet **fără port forwarding** și **fără a-ți expune IP-ul de acasă**.

**Domeniu:** `licenta-stabilizator-vrg.org` (înregistrat prin Cloudflare Registrar).
**Hostname public:** `https://app.licenta-stabilizator-vrg.org`

**Instalare `cloudflared`** (pe Debian Trixie trebuie cheia GPG nouă):
```bash
sudo mkdir -p --mode=0755 /usr/share/keyrings
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main" | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt-get update && sudo apt-get install -y cloudflared
```

**Creare tunel + rutare DNS:**
```bash
cloudflared tunnel login
cloudflared tunnel create stabilizator
cloudflared tunnel route dns stabilizator app.licenta-stabilizator-vrg.org
```

**Identificatori tunel (referință):**
- nume: `stabilizator`
- UUID: `9f652ca6-8a3b-44c8-a121-f4a3e1ae4321`
- credențiale: `/home/deiuvrg/.cloudflared/9f652ca6-8a3b-44c8-a121-f4a3e1ae4321.json`
- certificat: `/home/deiuvrg/.cloudflared/cert.pem`

**Configurare** `/etc/cloudflared/config.yml` (și o copie în `/home/deiuvrg/.cloudflared/config.yml`):
```yaml
tunnel: 9f652ca6-8a3b-44c8-a121-f4a3e1ae4321
credentials-file: /home/deiuvrg/.cloudflared/9f652ca6-8a3b-44c8-a121-f4a3e1ae4321.json

ingress:
  - hostname: app.licenta-stabilizator-vrg.org
    service: http://localhost:8080
  - service: http_status:404
```
*La ce ajută:* tot ce vine pe `app.licenta-stabilizator-vrg.org` e trimis către serviciul local de pe portul 8080; orice altceva primește 404.

**Rulare ca serviciu (pornire automată):**
```bash
sudo cloudflared service install
sudo systemctl enable --now cloudflared
```

**Pagina de test (placeholder)** până e gata aplicația — servită de un serviciu simplu:
- `webtest.service` rulează `python3 -m http.server 8080` (bind `127.0.0.1`) din `/home/deiuvrg/webtest/`.

> **Eroarea „Error 1033"** apare doar când `cloudflared` nu e conectat (ex. Pi offline). Dacă pagina se încarcă, tunelul merge.

**Verificare:**
```bash
systemctl status cloudflared
sudo journalctl -u cloudflared -n 50 --no-pager
```

---

## 8. Acces privat remote — Tailscale

VPN privat (rețea „mesh") ca să te conectezi prin SSH la Pi **de oriunde** (inclusiv din cămin), fără să expui SSH-ul pe internet.

**Instalare + pornire:**
```bash
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up
```
*La ce ajută:* după autentificare, Pi-ul primește un IP privat fix în tailnet și pornește automat la boot.

**Adrese (referință):**
- Pi (`stabilizatorvrg`): `100.124.235.50`
- Mac: `100.77.191.80`

**Conectare SSH remote:**
```bash
ssh deiuvrg@100.124.235.50
# sau:  ssh deiuvrg@stabilizatorvrg
```
> Pe Mac, aplicația Tailscale trebuie să fie pornită.

---

## 9. Securitate

Context util pentru priorități: din internet, singurul lucru accesibil e tunelul Cloudflare → aplicația. **SSH și MQTT sunt doar pe LAN/Tailscale**, niciun port nu e forwardat pe router.

### 9.1 Update-uri de securitate automate
```bash
sudo apt-get install -y unattended-upgrades
echo 'APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";' | sudo tee /etc/apt/apt.conf.d/20auto-upgrades
```
*La ce ajută:* Pi-ul își descarcă și instalează singur patch-urile de securitate.

### 9.2 Chei SSH + dezactivarea parolei
**Pe Mac** — generare cheie (dacă nu există) și copiere pe Pi:
```bash
ssh-keygen -t ed25519
ssh-copy-id deiuvrg@100.124.235.50
ssh deiuvrg@100.124.235.50      # test: intră fără parolă
```

**Pe Pi** — dezactivarea autentificării cu parolă (doar cheie):
```bash
echo 'PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes' | sudo tee /etc/ssh/sshd_config.d/00-hardening.conf
sudo sshd -t                    # verifică să nu fie greșeli
sudo systemctl restart ssh
sudo sshd -T | grep -iE 'passwordauthentication|pubkeyauthentication'
```
> **Capcană rezolvată:** exista deja `/etc/ssh/sshd_config.d/50-cloud-init.conf` cu `PasswordAuthentication yes` (creat de Imager). SSH folosește **prima** valoare găsită, iar fișierele se citesc alfabetic. De aceea fișierul nostru a fost numit `00-hardening.conf` — ca să fie citit **primul** și să câștige `no`.

*La ce ajută:* nimeni nu mai poate intra cu parolă, doar cu cheia ta privată (mult mai sigur).

Verificare că parola e off (trebuie respins):
```bash
ssh -o PubkeyAuthentication=no deiuvrg@100.124.235.50   # → Permission denied (publickey)
```

### 9.3 Firewall — ufw
```bash
sudo apt-get install -y ufw
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw allow in on tailscale0
sudo ufw allow 1883/tcp
sudo ufw enable
sudo ufw status verbose
```
*La ce ajută fiecare:*
- `deny incoming` / `allow outgoing` = implicit blochează tot ce intră, permite tot ce iese.
- `allow 22/tcp` = SSH.
- `allow in on tailscale0` = tot traficul prin Tailscale (deci SSH-ul de la distanță).
- `allow 1883/tcp` = ca ESP-ul să ajungă la brokerul MQTT.
- Tunelul Cloudflare și backend-ul merg prin localhost → nu au nevoie de reguli.

> 💡 Înainte de `ufw enable`, ține deschisă o a doua sesiune SSH ca plasă de siguranță (să nu te blochezi).

### 9.4 Protecție DDoS
**Nu necesită configurare** — e acoperită automat de Cloudflare: protecția DDoS e mereu activă pe toate planurile, IP-ul real al Pi-ului e ascuns de tunel, iar atacatorii ajung doar la marginea rețelei Cloudflare.
Opțional, gratis din dashboard-ul Cloudflare: **Bot Fight Mode** (Security → Settings), câteva reguli custom de firewall, și „Under Attack" mode (doar dacă ești efectiv sub atac).
La nivel de aplicație (de adăugat în backend): **lockout la cont** în ASP.NET Identity și **rate limiting** pe endpoint-urile sensibile.

---

## 10. Comenzi utile (cheat sheet)

```bash
# Conectare la Pi (remote, prin Tailscale)
ssh deiuvrg@100.124.235.50

# Aflarea IP-ului local (e dinamic prin DHCP — se schimbă)
hostname -I

# Stare servicii
systemctl status cloudflared
systemctl status mosquitto
sudo tailscale status

# Loguri
sudo journalctl -u cloudflared -n 50 --no-pager

# După re-flash, șterge cheia veche de host de pe Mac:
ssh-keygen -R stabilizatorvrg.local
ssh-keygen -R 100.124.235.50
```

---

## 11. Starea finală + pașii următori

### Ce rulează acum pe Pi
| Componentă | Stare |
|---|---|
| OS | Raspberry Pi OS Lite 64-bit (Trixie) |
| Acces SSH | doar cu cheie (parolă dezactivată), prin Tailscale |
| Firewall `ufw` | activ (22, tailscale0, 1883) |
| Cloudflare Tunnel | `app.licenta-stabilizator-vrg.org` (acum pagina de test) |
| Mosquitto MQTT | activ, cu autentificare + ACL |
| Runtime .NET | ASP.NET Core 10.0.9 (arm64) |
| Update-uri automate | pornite |
| zram | gestionat de OS |

### Backend — aplicația (`StabilizatorHub`)
De construit (ASP.NET Core): serviciul MQTT (BackgroundService cu MQTTnet), SignalR (date live), EF Core + SQLite, Identity (autentificare + „claiming" de dispozitive), controllere, și frontend static în `wwwroot` (HTML/CSS/JS + Chart.js + SignalR JS din CDN).

**Decizia de versiune .NET pentru backend:**
- **Varianta A — net10.0 (recomandat):** Pi-ul are deja runtime-ul .NET 10 (LTS, suport până în noiembrie 2028). Zero treabă în plus pe Pi.
- **Varianta B — net9.0:** .NET 9 e suportat până în noiembrie 2026 (ok pentru licență). Necesită instalarea **și** a runtime-ului .NET 9 pe Pi:
  ```bash
  sudo bash /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /opt/dotnet
  ```
  (cele două runtime-uri pot coexista) și țintirea `net9.0` în proiect.

**Flux de deploy (pe scurt):**
1. Pe Mac: `dotnet publish -c Release -r linux-arm64 --self-contained false`
2. Copiezi rezultatul pe Pi (prin `scp`/`rsync` peste Tailscale).
3. Rulezi backend-ul ca serviciu systemd (ex. pe portul 5000).
4. În `config.yml` schimbi `service:` din `http://localhost:8080` în `http://localhost:5000`, oprești pagina de test (`sudo systemctl disable --now webtest`) și repornești `cloudflared`.

### Riscuri cunoscute
- WiFi-ul pe Pi 3 poate să nu se reconecteze după un reboot/restart de router (power-save oprit ajută; un cablu Ethernet ar fi soluția cea mai sigură). Tunelul, Tailscale și serviciile pornesc automat când revine rețeaua.
- IP-ul local se schimbă (DHCP) — verifică mereu cu `hostname -I`. Opțional: rezervare DHCP în router pentru un IP local fix.
