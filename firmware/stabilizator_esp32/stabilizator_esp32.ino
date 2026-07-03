/*
 * ============================================================================
 *  VOLTAGE STABILIZER - ESP32 firmware (StabilizatorHubVRG)
 * ============================================================================
 *  This firmware keeps the proven local hardware layer (sensors, complementary
 *  LEDC motor drive, SSR, deadband regulation) and adds network connectivity:
 *
 *   - WiFi provisioning: WiFiManager captive portal "Stabilizator-Setup" on the
 *     first boot (or when the saved network is gone). The cloud broker
 *     host/port/username/password are ALSO entered there and kept in NVS, so the
 *     device joins ANY WiFi/hotspot and needs no code change or reflash.
 *   - Cloud MQTT over TLS: the ESP and the Raspberry Pi backend both dial OUT to
 *     a cloud broker (HiveMQ Cloud, port 8883). The device therefore works from
 *     anywhere, even on a different network than the Pi - no LAN IPs, no
 *     port-forwarding. clientId = MAC; auth = the shared cloud broker account.
 *   - Telemetry every 60 s, remote on/off, "online"/"offline" presence via LWT.
 *   - Device identity = WiFi MAC (used in the topic tree and as clientId). The
 *     pairing code is generated once and stored in NVS (flash).
 *   - Claiming: while unclaimed the pairing code is shown on the OLED (it
 *     alternates with the live voltage screen for bench testing); the backend
 *     publishes "claimed" true/false (retained). On "false" a FRESH pairing
 *     code is generated, so a released device can't be re-claimed with the old
 *     code.
 *   - Local fail-safe: regulation, sensors and the SSR safety cutoff run on the
 *     Arduino loop() (core 1). ALL networking (WiFi portal, WiFi/MQTT
 *     reconnects) runs in a separate FreeRTOS task on core 0, so a dead/absent
 *     broker can never stall the 230 V control loop. State shared between the
 *     two cores is volatile (scalars) or mutex-guarded (pairing String, NVS).
 *   - The SSR output starts OFF after boot (safe default); it energizes only on
 *     'out_on' / an {"output":"on"} command AND only when mains are present.
 *
 *  HARDWARE (matches the real wiring of this build):
 *    V_in   ZMPT/divider  -> GPIO34 (ADC1, input-only)
 *    V_out  ZMPT/divider  -> GPIO35 (ADC1, input-only)
 *    I_load ACS712-20A    -> GPIO32 (ADC1)              <-- current sensor
 *    Motor BTS7960: L_PWM=26 (inverting), R_PWM=25 (non-inverting),
 *                   L_EN=27, R_EN=14   (complementary PWM, 50% = stop)
 *    SSR output relay     -> GPIO13 (active HIGH)
 *    OLED SSD1306 I2C     -> SDA=21, SCL=22 (0x3C)
 *
 *  IMPORTANT: GPIO32/34/35 are all ADC1 channels, so they keep working while
 *  WiFi is on (ADC2 would not - do not move the sensors to ADC2 pins).
 *
 *  MQTT contract (must match the backend):
 *    stabilizator/{MAC}/telemetrie -> {"vin":228,"vout":230,"i":3.10,"p":713,
 *                                      "e":12.40,"out":1,"fw":"2.0.0"}   (60 s)
 *    stabilizator/{MAC}/status     -> "online"/"offline"   (retained, LWT)
 *    stabilizator/{MAC}/info       -> {"pair":"7F3K9Q","fw":"2.0.0"} (retained)
 *    stabilizator/{MAC}/comanda    <- {"output":"on"|"off"}   (SSR remote)
 *    stabilizator/{MAC}/claimed    <- "true"/"false"          (retained)
 *
 *  Libraries (Arduino Library Manager):
 *    WiFiManager (tzapu), PubSubClient (Nick O'Leary), ArduinoJson,
 *    Adafruit GFX, Adafruit SSD1306
 *
 *  Serial (115200, "Newline"): auto|manual|up|down|stop|<0..100>|
 *    out_on|out_off|in <V>|out <V>|cur <A>|net|show
 *
 *  !! CALIBRATE on real hardware: in <V> / out <V> / cur <A> against a
 *     reference meter (or set CAL_VIN/CAL_VOUT/CAL_I below).
 * ============================================================================
 */

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include "esp_mac.h"           // esp_read_mac(): factory MAC without starting WiFi
#include <WiFiManager.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

// ============================ CONFIG ============================

#define FW_VERSION   "2.0.0"

// --- MQTT broker (cloud, TLS) ---
// The ESP and the Raspberry Pi backend both dial OUT to this broker, so the
// device works from ANY WiFi/hotspot regardless of where the Pi is. The host is
// not secret (safe to commit); the broker USERNAME/PASSWORD are entered once in
// the WiFiManager captive portal and kept in NVS - never in git.
#define MQTT_HOST_DEFAULT  "purpletawny-76e1ad92.a03.euc1.aws.hivemq.cloud"
#define MQTT_PORT_DEFAULT  8883        // HiveMQ Cloud TLS port

// --- OLED ---
#define SCREEN_WIDTH 128
#define SCREEN_HEIGHT 64
#define OLED_RESET   -1
#define OLED_ADDR    0x3C

// --- Pins: sensors / OLED ---
const int PIN_VIN  = 34;   // V_in  (ADC1)
const int PIN_VOUT = 35;   // V_out (ADC1)
const int PIN_ILOAD = 32;  // ACS712 current (ADC1)
const int PIN_SDA  = 21;
const int PIN_SCL  = 22;

// --- Pins: motor (BTS7960) ---
const int L_PWM = 26;   // inverting
const int R_PWM = 25;   // non-inverting
const int L_EN  = 27;   // enable left
const int R_EN  = 14;   // enable right

// --- Pin: SSR output relay ---
const int  PIN_SSR = 13;            // relay IN -> GPIO13 ; relay GND -> GND
const bool SSR_ACTIV_HIGH = true;   // set false if the relay is inverted

// --- Motor PWM (LEDC) ---
const int PWM_FREQ = 1000;
const int PWM_RES  = 11;            // 0..2047
const int PWM_MAX  = (1 << PWM_RES) - 1;

// --- Sensor calibration (ADC-count multipliers; CALIBRATE!) ---
float CAL_VIN  = 0.4120f;  // V per ADC-count RMS, input   ('in <V>')
float CAL_VOUT = 0.4727f;  // V per ADC-count RMS, output  ('out <V>')
float CAL_I    = 0.0500f;  // A per ADC-count RMS, current ('cur <A>')
const float I_NOISE_FLOOR = 0.05f; // [A] below this report 0 (sensor noise)

// --- Regulation (target +/- 1 V) ---
const float TINTA      = 230.0f;
const float BANDA      = 1.0f;      // +/- 1 V deadband
const float HISTEREZA  = 1.0f;      // anti-hunting
const float EROARE_MAX = 30.0f;     // beyond this -> full speed
const int   STEP_MIN   = 8;         // slow creep near the target (58%/42%)
const int   STEP_MAX   = 45;        // fast when far away
const float VIN_MIN_OK = 120.0f;    // below this = no mains -> motor stop + SSR off

// If regulation runs the wrong way (OUT drops when it should rise), set true:
const bool INVERSEAZA = false;
// When stable, optionally cut the enables (zero current through the motor):
const bool ENABLE_OFF_WHEN_STABLE = false;

// --- Safety / timing ---
const uint32_t MAX_RUN_MS    = 30000UL;  // stall guard: max continuous run
const uint32_t BOOT_GRACE_MS = 1500UL;   // sensors settle, motor off
const uint32_t MANUAL_MAX_MS = 15000UL;  // manual command auto-stop
const uint32_t FEREASTRA_US  = 60000UL;  // RMS window (60 ms = 3 cycles @50 Hz)
const uint32_t T_TELEMETRY_MS = 60000UL; // telemetry every 60 s
const uint32_t T_OLED_MS     = 500UL;    // OLED refresh

// Restore SSR state from NVS after a power cut. false = output OFF after boot.
const bool RESTORE_OUTPUT = false;

// ============================ GLOBALS ============================

WiFiClientSecure  net;              // TLS transport for the cloud broker
PubSubClient      mqtt(net);
Preferences       prefs;
Adafruit_SSD1306  display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);
bool oledOk = false;

// identity / network (NVS)
String deviceId;        // MAC without ":" (e.g. A1B2C3D4E5F6) == topic id + clientId
String deviceSecret;    // legacy per-device secret (kept for identity; not used for cloud auth)
String pairingCode;     // shown on the OLED while unclaimed (guarded by pairMutex)
String mqttHost = MQTT_HOST_DEFAULT;
int    mqttPort = MQTT_PORT_DEFAULT;
String mqttUser;        // cloud broker username (NVS, entered in the portal)
String mqttPass;        // cloud broker password (NVS, entered in the portal)
volatile bool claimed = false;

String tTelemetry, tStatus, tInfo, tCommand, tClaimed;

// --- Concurrency (control loop = core 1, network task = core 0) ---
// Scalars shared across cores are `volatile`: aligned 32-bit loads/stores are
// atomic on the ESP32, so a stale-by-one-cycle read is the worst case and is
// harmless. Non-atomic shared state (the pairingCode String, NVS/Preferences)
// is guarded by a mutex.
SemaphoreHandle_t nvsMutex;     // serializes all Preferences (NVS) access
SemaphoreHandle_t pairMutex;    // serializes pairingCode String access
TaskHandle_t      netTaskHandle = nullptr;

// measurements (control task writes, network task reads for telemetry)
float adcInRms_last = 0, adcOutRms_last = 0, adcIloadRms_last = 0;  // control-only
float vin = 0, vout = 0;                                            // control-only
volatile float vinFilt = 0, voutFilt = 0, iLoad = 0, powerW = 0, energyWh = 0;
volatile bool  ssrDorit = false;       // user/backend wants current at the output?
volatile bool  ssrStareReala = false;  // actual SSR pin state
volatile bool  netConnected = false;   // mirror of mqtt.connected() for core 1

// control / serial state (control task only)
String  buf = "";
bool    autoMode = true, manualActiv = false;
uint32_t manualStart = 0;
int     dutyAplicat = 50;
uint32_t lastOled = 0, lastEnergyTick = 0;

// WiFiManager: extra fields for the broker (filled in the captive portal)
char wmHost[64] = MQTT_HOST_DEFAULT;
char wmPort[8]  = "8883";
char wmUser[40] = "";
char wmPass[64] = "";
bool wmShouldSave = false;
void wmSaveCallback() { wmShouldSave = true; }

// ============================ IDENTITY ============================

String randomCode(int length) {
  static const char charset[] = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O,1/I
  String s;
  for (int i = 0; i < length; i++) s += charset[esp_random() % 32];
  return s;
}
String randomHex(int length) {
  static const char hexc[] = "0123456789ABCDEF";
  String s;
  for (int i = 0; i < length; i++) s += hexc[esp_random() % 16];
  return s;
}

void buildTopics() {
  String base = "stabilizator/" + deviceId;
  tTelemetry = base + "/telemetrie";
  tStatus    = base + "/status";
  tInfo      = base + "/info";
  tCommand   = base + "/comanda";
  tClaimed   = base + "/claimed";
}

void loadIdentity() {
  prefs.begin("stab", false);

  // Read the factory STA MAC from eFuse - reliable BEFORE WiFi is started
  // (WiFi.macAddress() returns 00:00:.. until the driver is up).
  uint8_t mac[6];
  esp_read_mac(mac, ESP_MAC_WIFI_STA);
  char macStr[13];
  snprintf(macStr, sizeof(macStr), "%02X%02X%02X%02X%02X%02X",
           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
  deviceId = String(macStr);

  deviceSecret = prefs.getString("secret", "");
  if (deviceSecret == "") { deviceSecret = randomHex(32); prefs.putString("secret", deviceSecret); }

  pairingCode = prefs.getString("pair", "");
  if (pairingCode == "") { pairingCode = randomCode(6); prefs.putString("pair", pairingCode); }

  claimed   = prefs.getBool("claimed", false);
  mqttHost  = prefs.getString("mqttHost", MQTT_HOST_DEFAULT);
  mqttPort  = prefs.getInt("mqttPort", MQTT_PORT_DEFAULT);
  mqttUser  = prefs.getString("mqttUser", "");
  mqttPass  = prefs.getString("mqttPass", "");
  CAL_VIN   = prefs.getFloat("calVin",  CAL_VIN);   // persisted calibration
  CAL_VOUT  = prefs.getFloat("calVout", CAL_VOUT);  // (falls back to the
  CAL_I     = prefs.getFloat("calI",    CAL_I);     //  compile-time defaults)
  if (RESTORE_OUTPUT) ssrDorit = prefs.getBool("out", false);

  prefs.end();

  buildTopics();
  strncpy(wmHost, mqttHost.c_str(), sizeof(wmHost) - 1);
  snprintf(wmPort, sizeof(wmPort), "%d", mqttPort);
  strncpy(wmUser, mqttUser.c_str(), sizeof(wmUser) - 1);
  strncpy(wmPass, mqttPass.c_str(), sizeof(wmPass) - 1);

  Serial.println(F("=== DEVICE IDENTITY ==="));
  Serial.println("deviceId (topic id)  : " + deviceId);
  Serial.println("pairing code         : " + pairingCode);
  Serial.println("MQTT broker          : " + mqttHost + ":" + String(mqttPort) + " (TLS)");
  Serial.println("MQTT user            : " + (mqttUser.length() ? mqttUser : String("(not set - enter in portal)")));
  Serial.println(F("======================="));
}

/** Backend released the device: the old code must die. (network task) */
void regeneratePairingCode() {
  String fresh = randomCode(6);

  xSemaphoreTake(pairMutex, portMAX_DELAY);
  pairingCode = fresh;
  xSemaphoreGive(pairMutex);

  xSemaphoreTake(nvsMutex, portMAX_DELAY);
  prefs.begin("stab", false);
  prefs.putString("pair", fresh);
  prefs.putBool("claimed", false);
  prefs.end();
  xSemaphoreGive(nvsMutex);

  claimed = false;
}

// ============================ SSR ============================

void ssrScrie(bool on) {
  ssrStareReala = on;
  digitalWrite(PIN_SSR, (on == SSR_ACTIV_HIGH) ? HIGH : LOW);
}
void setOutputDorit(bool on) {
  ssrDorit = on;
  if (RESTORE_OUTPUT) {
    xSemaphoreTake(nvsMutex, portMAX_DELAY);
    prefs.begin("stab", false); prefs.putBool("out", on); prefs.end();
    xSemaphoreGive(nvsMutex);
  }
}

// ============================ MOTOR PWM (LEDC) ============================

void setDutyCycle(int percent) {
  percent = constrain(percent, 0, 100);
  dutyAplicat = percent;
  int d = map(percent, 0, 100, 0, PWM_MAX);
#if ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcWrite(L_PWM, PWM_MAX - d);
  ledcWrite(R_PWM, d);
#else
  ledcWrite(0, PWM_MAX - d);
  ledcWrite(1, d);
#endif
}
void pwmInit() {
#if ESP_ARDUINO_VERSION_MAJOR >= 3
  ledcAttach(L_PWM, PWM_FREQ, PWM_RES);
  ledcAttach(R_PWM, PWM_FREQ, PWM_RES);
#else
  ledcSetup(0, PWM_FREQ, PWM_RES); ledcAttachPin(L_PWM, 0);
  ledcSetup(1, PWM_FREQ, PWM_RES); ledcAttachPin(R_PWM, 1);
#endif
}
void motorMove(int percent) {
  digitalWrite(L_EN, HIGH); digitalWrite(R_EN, HIGH);
  setDutyCycle(percent);
}
void motorStop() {
  setDutyCycle(50);
  if (ENABLE_OFF_WHEN_STABLE) { digitalWrite(L_EN, LOW); digitalWrite(R_EN, LOW); }
}
int dutyReglaj(float eroare) {
  long mag = (long)constrain(fabs(eroare), BANDA, EROARE_MAX);
  long step = map(mag, (long)BANDA, (long)EROARE_MAX, STEP_MIN, STEP_MAX);
  bool crestem = (eroare > 0);
  bool spre100 = crestem ? !INVERSEAZA : INVERSEAZA;
  return spre100 ? (50 + step) : (50 - step);
}

// ============================ SENSORS ============================

// RMS of the AC component at an ADC pin, in raw counts, over FEREASTRA_US.
float citesteRMS(int pin) {
  uint32_t t0 = micros(); double sum = 0, sumSq = 0; uint32_t n = 0;
  while (micros() - t0 < FEREASTRA_US) { int r = analogRead(pin); sum += r; sumSq += (double)r * r; n++; }
  if (n == 0) return 0;
  double mean = sum / n, var = sumSq / n - mean * mean; if (var < 0) var = 0;
  return (float)sqrt(var);
}

void readSensors() {
  float adcIn = citesteRMS(PIN_VIN), adcOut = citesteRMS(PIN_VOUT), adcI = citesteRMS(PIN_ILOAD);
  adcInRms_last = adcIn; adcOutRms_last = adcOut; adcIloadRms_last = adcI;

  vin  = adcIn  * CAL_VIN;
  vout = adcOut * CAL_VOUT;
  vinFilt  = (vinFilt == 0)  ? vin  : 0.7f * vinFilt  + 0.3f * vin;
  voutFilt = (voutFilt == 0) ? vout : 0.7f * voutFilt + 0.3f * vout;

  iLoad = adcI * CAL_I;
  if (iLoad < I_NOISE_FLOOR) iLoad = 0;
  powerW = voutFilt * iLoad;   // apparent power [VA ~ W resistive]
}

// ============================ MQTT ============================

void publishInfo() {
  StaticJsonDocument<128> doc;
  if (!claimed) {                            // never expose the code once claimed
    xSemaphoreTake(pairMutex, portMAX_DELAY);
    String code = pairingCode;
    xSemaphoreGive(pairMutex);
    doc["pair"] = code;
  }
  doc["fw"] = FW_VERSION;
  char buffer[128];
  size_t n = serializeJson(doc, buffer);
  mqtt.publish(tInfo.c_str(), (uint8_t*)buffer, n, true);   // retained
}

void publishTelemetry() {
  StaticJsonDocument<192> doc;
  doc["vin"]  = roundf(vinFilt * 10) / 10.0f;
  doc["vout"] = roundf(voutFilt * 10) / 10.0f;
  doc["i"]    = roundf(iLoad * 100) / 100.0f;
  doc["p"]    = roundf(powerW * 10) / 10.0f;
  doc["e"]    = roundf(energyWh / 10.0) / 100.0;   // kWh, 2 decimals
  doc["out"]  = ssrStareReala ? 1 : 0;
  doc["fw"]   = FW_VERSION;
  char buffer[192];
  size_t n = serializeJson(doc, buffer);
  mqtt.publish(tTelemetry.c_str(), (uint8_t*)buffer, n, false);
}

void onMqttMessage(char* topic, byte* payload, unsigned int length) {
  String t(topic), msg;
  msg.reserve(length);
  for (unsigned int i = 0; i < length; i++) msg += (char)payload[i];

  if (t == tCommand) {
    StaticJsonDocument<96> doc;
    if (deserializeJson(doc, msg) == DeserializationError::Ok) {
      const char* output = doc["output"] | "";
      bool changed = false;
      if (strcmp(output, "on") == 0  && !ssrDorit) { setOutputDorit(true);  changed = true; }
      if (strcmp(output, "off") == 0 &&  ssrDorit) { setOutputDorit(false); changed = true; }
      // Instant dashboard feedback. SSR safety gating still happens on core 1,
      // so "out" here reflects the request; the next periodic frame confirms it.
      if (changed && mqtt.connected()) publishTelemetry();
    }
    return;
  }

  if (t == tClaimed) {
    bool nowClaimed = (msg == "true" || msg == "1");
    if (nowClaimed && !claimed) {
      claimed = true;
      xSemaphoreTake(nvsMutex, portMAX_DELAY);
      prefs.begin("stab", false); prefs.putBool("claimed", true); prefs.end();
      xSemaphoreGive(nvsMutex);
      publishInfo();                 // info without the pairing code
    } else if (!nowClaimed && claimed) {
      regeneratePairingCode();       // released: fresh code, show it again
      publishInfo();
    }
  }
}

void mqttConnect() {
  if (mqtt.connected()) return;
  if (mqttUser.length() == 0) {       // no cloud credentials yet -> don't spin
    Serial.println(F("MQTT: no broker user set (enter it in the WiFi portal)"));
    netConnected = false;
    return;
  }
  Serial.print(F("MQTT connecting... "));

  // clientId = deviceId (unique per board). Auth = the cloud broker account.
  // LWT: if we vanish, the broker publishes retained "offline" for us.
  bool ok = mqtt.connect(
    deviceId.c_str(),
    mqttUser.c_str(),                 // cloud broker username
    mqttPass.c_str(),                 // cloud broker password
    tStatus.c_str(), 1, true, "offline");

  if (!ok) { Serial.print(F("failed, rc=")); Serial.println(mqtt.state()); netConnected = false; return; }

  Serial.println(F("connected"));
  mqtt.publish(tStatus.c_str(), "online", true);    // retained
  publishInfo();
  mqtt.subscribe(tCommand.c_str(), 1);
  mqtt.subscribe(tClaimed.c_str(), 1);
  publishTelemetry();               // instant fresh state (incl. out=0 after boot)
  netConnected = true;
}

// ============================ NETWORK TASK (core 0) ============================
// Everything that can block on I/O lives here, OFF the control loop. The captive
// portal, WiFi (re)connects and MQTT (re)connects never stall the regulation on
// core 1 - that is the whole point of the "works with or without network" claim.

void networkTask(void* arg) {
  // Captive portal "Stabilizator-Setup": WiFi + cloud broker host/port/user/pass.
  // Blocks THIS task only (up to 180 s); core 1 keeps regulating the whole time.
  WiFiManager wm;
  WiFiManagerParameter pHost("host", "MQTT broker host", wmHost, sizeof(wmHost));
  WiFiManagerParameter pPort("port", "MQTT port (TLS 8883)", wmPort, sizeof(wmPort));
  WiFiManagerParameter pUser("user", "MQTT username", wmUser, sizeof(wmUser));
  WiFiManagerParameter pPass("pass", "MQTT password", wmPass, sizeof(wmPass));
  wm.addParameter(&pHost);
  wm.addParameter(&pPort);
  wm.addParameter(&pUser);
  wm.addParameter(&pPass);
  wm.setSaveConfigCallback(wmSaveCallback);
  wm.setConfigPortalTimeout(180);

  bool wifiOk = wm.autoConnect("Stabilizator-Setup");

  if (wmShouldSave) {                       // user (re)entered the broker fields
    mqttHost = pHost.getValue();
    mqttPort = atoi(pPort.getValue());
    if (mqttPort <= 0) mqttPort = MQTT_PORT_DEFAULT;
    mqttUser = pUser.getValue();
    mqttPass = pPass.getValue();
    xSemaphoreTake(nvsMutex, portMAX_DELAY);
    prefs.begin("stab", false);
    prefs.putString("mqttHost", mqttHost);
    prefs.putInt("mqttPort", mqttPort);
    prefs.putString("mqttUser", mqttUser);
    prefs.putString("mqttPass", mqttPass);
    prefs.end();
    xSemaphoreGive(nvsMutex);
    Serial.println("Saved broker: " + mqttHost + ":" + String(mqttPort) + " user=" + mqttUser);
  }

  if (!wifiOk) Serial.println(F("WiFi not configured - running offline, will retry"));

  net.setInsecure();                        // skip CA validation (fine for a thesis)
  mqtt.setServer(mqttHost.c_str(), mqttPort);
  mqtt.setSocketTimeout(4);                 // TLS handshake needs a bit longer
  mqtt.setCallback(onMqttMessage);

  uint32_t lastMqttAttempt = 0, lastTelemetry = millis(), lastWifiRetry = 0;

  for (;;) {
    if (WiFi.status() != WL_CONNECTED) {
      netConnected = false;
      if (millis() - lastWifiRetry > 5000) { lastWifiRetry = millis(); WiFi.reconnect(); }
    } else if (!mqtt.connected()) {
      netConnected = false;
      if (millis() - lastMqttAttempt > 3000) { lastMqttAttempt = millis(); mqttConnect(); }
    } else {
      mqtt.loop();
    }

    if (mqtt.connected() && millis() - lastTelemetry >= T_TELEMETRY_MS) {
      lastTelemetry = millis();
      publishTelemetry();
    }

    vTaskDelay(pdMS_TO_TICKS(50));          // yield; control loop is independent
  }
}

// ============================ SERIAL ============================

// Persist the three calibration factors to NVS so they survive reboot/reflash.
void saveCalibration() {
  xSemaphoreTake(nvsMutex, portMAX_DELAY);
  prefs.begin("stab", false);
  prefs.putFloat("calVin",  CAL_VIN);
  prefs.putFloat("calVout", CAL_VOUT);
  prefs.putFloat("calI",    CAL_I);
  prefs.end();
  xSemaphoreGive(nvsMutex);
}

void printHelp() {
  Serial.println();
  Serial.println(F("=== STABILIZER - commands ==="));
  Serial.println(F("  auto / manual"));
  Serial.println(F("  up/down/stop (manual) | <0..100> (manual)"));
  Serial.println(F("  out_on / out_off  (output relay / SSR)"));
  Serial.println(F("  in <V> | out <V> | cur <A>  (calibration)"));
  Serial.println(F("  net   (network/MQTT info) | show"));
  Serial.printf ("  TARGET=%.0fV | CAL_VIN=%.4f CAL_VOUT=%.4f CAL_I=%.4f | mode=%s | SSR=%s\n",
                 TINTA, CAL_VIN, CAL_VOUT, CAL_I, autoMode ? "AUTO" : "MANUAL", ssrDorit ? "ON" : "OFF");
  Serial.println(F("=============================="));
}

void printNet() {
  Serial.printf("WiFi: %s (%s, RSSI %d)  MQTT: %s @ %s:%d\n",
                WiFi.isConnected() ? "OK" : "...",
                WiFi.localIP().toString().c_str(), WiFi.RSSI(),
                netConnected ? "connected" : "down",
                mqttHost.c_str(), mqttPort);
  xSemaphoreTake(pairMutex, portMAX_DELAY);
  String code = pairingCode;
  xSemaphoreGive(pairMutex);
  Serial.println("deviceId: " + deviceId + " | claimed: " + (claimed ? "yes" : "no") +
                 " | pair: " + (claimed ? "-" : code));
}

void handleSerial() {
  while (Serial.available()) {
    char c = Serial.read();
    if (c == '\n' || c == '\r') {
      buf.trim(); buf.toLowerCase();
      if (buf.length() == 0) {}
      else if (buf == "show" || buf == "?") printHelp();
      else if (buf == "net") printNet();
      else if (buf == "auto")   { autoMode = true;  Serial.println(F(">> AUTO")); }
      else if (buf == "manual") { autoMode = false; manualActiv = false; motorStop(); Serial.println(F(">> MANUAL (stop)")); }
      else if (buf == "up")   { if (autoMode) Serial.println(F(">> in AUTO; type 'manual'")); else { manualActiv = true; manualStart = millis(); motorMove(100); Serial.println(F(">> UP 100%")); } }
      else if (buf == "down") { if (autoMode) Serial.println(F(">> in AUTO; type 'manual'")); else { manualActiv = true; manualStart = millis(); motorMove(0);   Serial.println(F(">> DOWN 0%")); } }
      else if (buf == "stop") { manualActiv = false; motorStop(); Serial.println(F(">> STOP")); }
      else if (buf == "out_on")  { setOutputDorit(true);  Serial.println(F(">> SSR: ON (output energized)")); }
      else if (buf == "out_off") { setOutputDorit(false); Serial.println(F(">> SSR: OFF (output de-energized)")); }
      else {
        int sp = buf.indexOf(' ');
        if (sp > 0) {
          String cmd = buf.substring(0, sp); float v = buf.substring(sp + 1).toFloat();
          if (cmd == "in")  { if (adcInRms_last < 5) Serial.println(F(">> IN too low.")); else if (v <= 0) Serial.println(F(">> invalid.")); else { CAL_VIN = v / adcInRms_last; saveCalibration(); Serial.printf(">> CAL_VIN=%.4f (saved)\n", CAL_VIN); } }
          else if (cmd == "out") { if (adcOutRms_last < 5) Serial.println(F(">> OUT too low.")); else if (v <= 0) Serial.println(F(">> invalid.")); else { CAL_VOUT = v / adcOutRms_last; saveCalibration(); Serial.printf(">> CAL_VOUT=%.4f (saved)\n", CAL_VOUT); } }
          else if (cmd == "cur") { if (adcIloadRms_last < 2) Serial.println(F(">> I too low (apply a load).")); else if (v <= 0) Serial.println(F(">> invalid.")); else { CAL_I = v / adcIloadRms_last; saveCalibration(); Serial.printf(">> CAL_I=%.4f (saved)\n", CAL_I); } }
          else Serial.println(F(">> unknown. 'show'."));
        } else if (isDigit(buf[0])) {
          if (autoMode) Serial.println(F(">> in AUTO; type 'manual'"));
          else { int val = constrain(buf.toInt(), 0, 100); manualStart = millis();
                 if (val == 50) { manualActiv = false; motorStop(); } else { manualActiv = true; motorMove(val); }
                 Serial.printf(">> duty %d%%\n", val); }
        } else Serial.println(F(">> unknown. 'show'."));
      }
      buf = "";
    } else { buf += c; if (buf.length() > 30) buf = ""; }
  }
}

// ============================ OLED ============================

void oledPairingScreen() {
  if (!oledOk) return;
  xSemaphoreTake(pairMutex, portMAX_DELAY);
  String code = pairingCode;
  xSemaphoreGive(pairMutex);

  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1); display.setCursor(0, 0);  display.println(F("Pair in the web app:"));
  display.setTextSize(2); display.setCursor(10, 18); display.println(code);
  display.setTextSize(1); display.setCursor(0, 44); display.print(F("ID: ")); display.println(deviceId.substring(6));
  display.setCursor(0, 54);
  display.print(WiFi.isConnected() ? F("WiFi OK") : F("WiFi..."));
  display.print(netConnected ? F("  MQTT OK") : F("  MQTT..."));
  display.display();
}

void oledLiveScreen(const char* stare) {
  if (!oledOk) return;
  display.clearDisplay();
  display.setTextColor(SSD1306_WHITE);
  display.setTextSize(1); display.setCursor(0, 0); display.printf("TARGET %.0fV", TINTA);
  display.setCursor(92, 0); display.print(ssrStareReala ? "OUT:ON" : "OUT:--");
  display.drawFastHLine(0, 10, SCREEN_WIDTH, SSD1306_WHITE);
  display.setTextSize(2);
  display.setCursor(0, 14); display.printf("IN %4.0fV", vinFilt);
  display.setCursor(0, 34); display.printf("OUT%4.0fV", voutFilt);
  display.setTextSize(1); display.setCursor(0, 56);
  display.print(stare);
  display.setCursor(92, 56); display.print(netConnected ? "NET" : "...");
  display.display();
}

// ============================ SETUP ============================

void setup() {
  Serial.begin(115200); delay(200);

  nvsMutex  = xSemaphoreCreateMutex();
  pairMutex = xSemaphoreCreateMutex();

  pinMode(PIN_SSR, OUTPUT); ssrScrie(false);            // SSR off at boot
  pinMode(L_EN, OUTPUT); pinMode(R_EN, OUTPUT);
  digitalWrite(L_EN, HIGH); digitalWrite(R_EN, HIGH);
  pwmInit(); setDutyCycle(50);

  analogReadResolution(12);
  analogSetPinAttenuation(PIN_VIN,   ADC_11db);
  analogSetPinAttenuation(PIN_VOUT,  ADC_11db);
  analogSetPinAttenuation(PIN_ILOAD, ADC_11db);

  Wire.begin(PIN_SDA, PIN_SCL);
  oledOk = display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR);
  if (!oledOk) Serial.println(F("OLED not found (0x3C/0x3D?)."));
  if (oledOk) {
    display.clearDisplay(); display.setTextColor(SSD1306_WHITE);
    display.setTextSize(1); display.setCursor(0, 0);
    display.println(F("StabilizatorHub VRG")); display.println(F("booting..."));
    display.display();
  }

  WiFi.mode(WIFI_STA);
  loadIdentity();

  lastEnergyTick = millis();

  // Network (WiFi portal + MQTT) runs on core 0 so it can never block the
  // control loop, which stays real-time on core 1 (the Arduino loop()).
  xTaskCreatePinnedToCore(networkTask, "net", 8192, nullptr, 1, &netTaskHandle, 0);
}

// ============================ LOOP ============================

void loop() {
  static int      ultimaDir = 0;
  static uint32_t startDir = 0;
  static bool     stall = false;
  static bool     stabil = false;

  // Network (WiFi/MQTT) is handled by networkTask on core 0 - never here, so
  // the regulation below stays real-time even with the broker down or absent.

  // --- sensors + serial ---
  readSensors();
  handleSerial();

  // --- SSR: on only if wanted AND mains present (safety), after the grace ---
  bool retea = (vinFilt >= VIN_MIN_OK);
  bool ssrFinal = ssrDorit && retea && (millis() >= BOOT_GRACE_MS);
  if (ssrFinal != ssrStareReala) ssrScrie(ssrFinal);

  if (millis() < BOOT_GRACE_MS) { motorStop(); if (oledOk) oledLiveScreen("init..."); return; }

  const char* stare;

  if (!autoMode) {
    if (manualActiv && millis() - manualStart > MANUAL_MAX_MS) { motorStop(); manualActiv = false; Serial.println(F(">> manual: auto-stopped")); }
    stare = manualActiv ? "MANUAL  ON" : "MANUAL  stop";
  } else {
    float eroare = TINTA - voutFilt, ae = fabs(eroare);
    if (stabil) { if (ae > BANDA + HISTEREZA) stabil = false; }
    else        { if (ae <= BANDA)            stabil = true;  }

    int dir;
    if (!retea)      dir = 0;
    else if (stabil) dir = 0;
    else             dir = (eroare > 0) ? 1 : -1;

    if (dir != ultimaDir) { startDir = millis(); stall = false; }
    if (dir != 0 && millis() - startDir > MAX_RUN_MS) stall = true;
    ultimaDir = dir;

    if (dir == 0 || stall) motorStop();
    else                   motorMove(dutyReglaj(eroare));

    if (!retea)      stare = "NO MAINS";
    else if (stall)  stare = "LIMIT";
    else if (dir == 1)  stare = "ADJUST +";
    else if (dir == -1) stare = "ADJUST -";
    else             stare = "STABLE  OK";
  }

  // --- energy integration (control task owns it; networkTask only reads it) ---
  uint32_t now = millis();
  double dtHours = (now - lastEnergyTick) / 3600000.0;
  lastEnergyTick = now;
  energyWh = energyWh + (float)(powerW * dtHours);

  // --- OLED ---
  if (now - lastOled >= T_OLED_MS) {
    lastOled = now;
    if (claimed) {
      oledLiveScreen(stare);
    } else {
      // Unclaimed: alternate the pairing code with live voltages every 4 s,
      // so the readings are visible during bench testing too.
      if ((now / 4000) % 2 == 0) oledPairingScreen();
      else                       oledLiveScreen(stare);
    }
  }

  Serial.printf("IN:%6.1fV | OUT:%6.1fV | I:%5.2fA | P:%6.1fW | %-11s | duty=%d%% | SSR=%s | %s | NET=%s\n",
                vinFilt, voutFilt, iLoad, powerW, stare, dutyAplicat,
                ssrStareReala ? "ON" : "OFF", autoMode ? "AUTO" : "MANUAL",
                netConnected ? "up" : "down");
  delay(20);
}
