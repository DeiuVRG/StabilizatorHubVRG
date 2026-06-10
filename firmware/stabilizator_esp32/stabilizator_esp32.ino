/*
 * ============================================================================
 *  VOLTAGE STABILIZER - ESP32 firmware (StabilizatorHubVRG)
 * ============================================================================
 *  Features:
 *   - WiFi provisioning (WiFiManager captive portal on first boot)
 *   - Device identity = MAC address; MQTT secret + pairing code stored in NVS
 *   - MQTT: telemetry every 60 s, on/off commands, online/offline via LWT
 *   - Device claiming: pairing code on the OLED while unclaimed; the backend
 *     publishes "claimed" true/false (retained). On "false" a FRESH pairing
 *     code is generated, so a released/sold device can never be re-claimed
 *     with the old code.
 *   - Sensors: 2x ZMPT101B (input/output voltage), ACS712-20A (current) - RMS
 *   - Variac regulation: DC motor via BTS7960 keeps the output at ~230 V
 *     (deadband control with proportional speed + stall protection)
 *   - SSR output relay (remote on/off from the web app)
 *
 *  MQTT contract (must match the backend - see docs/esp32-integrare.md):
 *   stabilizator/{deviceId}/telemetrie  -> {"vin":228,"vout":230,"i":3.10,
 *                                           "p":713,"e":12.40,"out":1,
 *                                           "fw":"1.1.0"}        (every 60 s)
 *   stabilizator/{deviceId}/status      -> "online"/"offline"    (retained, LWT)
 *   stabilizator/{deviceId}/info        -> {"pair":"7F3K9Q","fw":"1.1.0"} (retained)
 *   stabilizator/{deviceId}/comanda     <- {"output":"on"|"off"}
 *   stabilizator/{deviceId}/claimed     <- "true"/"false"        (retained)
 *
 *  Libraries (Arduino Library Manager):
 *   - WiFiManager (tzapu), PubSubClient (Nick O'Leary), ArduinoJson,
 *     Adafruit GFX, Adafruit SSD1306
 *
 *  NOTE: PubSubClient default MQTT_MAX_PACKET_SIZE (256) is enough here.
 *
 *  !! CALIBRATION required on real hardware: CAL_VIN / CAL_VOUT / ACS_SENS /
 *     DIVIDER_RATIO - adjust against a reference multimeter (see CONFIG).
 *
 *  !! SAFETY: 230 V mains. Hardware end-stop switches + diodes protect the
 *     variac travel limits independently of this code. The regulation loop
 *     runs even without network (local fail-safe).
 * ============================================================================
 */

#include <WiFi.h>
#include <WiFiManager.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <Preferences.h>
#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

// ============================ CONFIG ============================

#define FW_VERSION   "1.1.0"

// --- Pins (see docs/files schema: GPIO mapping table) ---
#define PIN_VIN      34      // ZMPT101B #1 - input voltage  (ADC, input-only)
#define PIN_VOUT     35      // ZMPT101B #2 - output voltage (ADC, input-only)
#define PIN_ILOAD    36      // ACS712 current, through divider (ADC, input-only)
#define PIN_RPWM     25      // BTS7960 RPWM
#define PIN_LPWM     26      // BTS7960 LPWM
#define PIN_MEN      27      // BTS7960 R_EN + L_EN (tied together)
#define PIN_SSR      14      // SSR RELAY_IN (direct or through NPN driver)
#define PIN_SDA      21      // OLED SDA
#define PIN_SCL      22      // OLED SCL

// --- MQTT broker = the Raspberry Pi on the LAN ---
// Find the Pi address with `hostname -I`; ideally give the Pi a DHCP
// reservation so this never changes.
#define MQTT_HOST    "192.168.1.10"
#define MQTT_PORT    1883

// --- Regulation ---
const float TARGET_V    = 230.0f;  // output target [V]
const float DEADBAND_V  = 4.0f;    // +/- deadband [V] (no hunting inside it)
const float MAX_ERROR_V = 25.0f;   // error mapped to full motor speed
const int   DUTY_MIN    = 90;      // slow creep PWM near the target (0..255)
const int   DUTY_MAX    = 230;     // full speed PWM (0..255)
const float VIN_MIN_OK  = 120.0f;  // below this we assume no mains -> motor stop

// If the brush moves the wrong way (output drops when it should rise),
// flip this or swap the M+/M- motor wires.
const bool  INVERT_MOTOR = false;

// Stall protection: if the motor runs continuously in one direction longer
// than this without reaching the deadband, stop commanding it (end of travel
// or jammed). Hardware end-stops remain the real protection.
const unsigned long MAX_RUN_MS = 20000UL;

// --- Sensor calibration (ADJUST with a reference multimeter!) ---
const float ADC_VREF      = 3.30f;   // ESP32 ADC reference [V]
const int   ADC_MAX       = 4095;    // 12-bit
const float CAL_VIN       = 110.0f;  // (Vrms at ADC pin) -> mains Vrms, input  (CALIBRATE)
const float CAL_VOUT      = 110.0f;  // (Vrms at ADC pin) -> mains Vrms, output (CALIBRATE)
const float DIVIDER_RATIO = 0.6f;    // ACS divider: R2/(R1+R2) = 15/(10+15)
const float ACS_SENS      = 0.100f;  // V/A for ACS712-20A (0.066 for 30A, 0.185 for 5A)
const float I_NOISE_FLOOR = 0.05f;   // [A] below this report 0 (sensor noise)

// --- Timing ---
const unsigned long T_TELEMETRY_MS = 60000UL;  // telemetry every 60 seconds
const unsigned long T_CONTROL_MS   = 250UL;    // regulation step
const unsigned long T_OLED_MS      = 500UL;    // display refresh
const unsigned long BOOT_GRACE_MS  = 1500UL;   // let sensors settle, motor off

// Restore the SSR state from NVS after a power cut. Default false = the
// output stays OFF after boot until switched on (safe default).
const bool RESTORE_OUTPUT = false;

// ============================ GLOBALS ============================

WiFiClient        net;
PubSubClient      mqtt(net);
Preferences       prefs;
Adafruit_SSD1306  oled(128, 64, &Wire, -1);
bool              oledOk = false;

String deviceId;        // MAC without ":" (e.g. A1B2C3D4E5F6)
String deviceSecret;    // MQTT password of this device (NVS)
String pairingCode;     // code shown on the OLED while unclaimed (NVS)
bool   claimed = false;

String tTelemetry, tStatus, tInfo, tCommand, tClaimed;

float  vIn = 0, vOut = 0, iLoad = 0, powerW = 0;
double energyWh = 0;     // cumulative since boot (the backend integrates anyway)
bool   outputOn = false;

unsigned long lastTelemetry = 0, lastControl = 0, lastOled = 0;

// regulation state
int           lastDirection = 0;    // 0 stop, 1 raise, 2 lower
unsigned long directionSince = 0;
bool          stalled = false;

// ============================ IDENTITY ============================

String randomCode(int length) {
  // 32 unambiguous characters (no 0/O, 1/I).
  static const char charset[] = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
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

void loadIdentity() {
  prefs.begin("stab", false);

  deviceId = WiFi.macAddress();
  deviceId.replace(":", "");

  deviceSecret = prefs.getString("secret", "");
  if (deviceSecret == "") {                  // first boot ever
    deviceSecret = randomHex(32);
    prefs.putString("secret", deviceSecret);
  }

  pairingCode = prefs.getString("pair", "");
  if (pairingCode == "") {
    pairingCode = randomCode(6);
    prefs.putString("pair", pairingCode);
  }

  claimed = prefs.getBool("claimed", false);

  if (RESTORE_OUTPUT) {
    outputOn = prefs.getBool("out", false);
  }

  prefs.end();

  String base = "stabilizator/" + deviceId;
  tTelemetry = base + "/telemetrie";
  tStatus    = base + "/status";
  tInfo      = base + "/info";
  tCommand   = base + "/comanda";
  tClaimed   = base + "/claimed";

  // PROVISIONING (one-time, on the Pi): create the broker account for this
  // device:  sudo mosquitto_passwd /etc/mosquitto/passwd <deviceId>
  // and use the secret below as its password.
  Serial.println(F("=== DEVICE IDENTITY ==="));
  Serial.println("deviceId (MQTT user) : " + deviceId);
  Serial.println("secret (MQTT pass)   : " + deviceSecret);
  Serial.println("pairing code         : " + pairingCode);
  Serial.println(F("======================="));
}

/** Called when the backend releases the device: old code must die. */
void regeneratePairingCode() {
  pairingCode = randomCode(6);
  prefs.begin("stab", false);
  prefs.putString("pair", pairingCode);
  prefs.putBool("claimed", false);
  prefs.end();
  claimed = false;
}

// ============================ SENSORS ============================

// RMS of the AC component at an ADC pin [V], over ~5 mains cycles (50 Hz).
// Single-pass variance: RMS^2 = mean(x^2) - mean(x)^2 (removes DC offset).
float readAcRms(int pin) {
  const int N = 1000;
  const int sampleDelayUs = 100;      // ~100 ms total
  double sum = 0, sumSq = 0;

  for (int i = 0; i < N; i++) {
    double v = (double)analogRead(pin) * ADC_VREF / ADC_MAX;
    sum += v;
    sumSq += v * v;
    delayMicroseconds(sampleDelayUs);
  }

  double mean = sum / N;
  double var = sumSq / N - mean * mean;
  if (var < 0) var = 0;
  return (float)sqrt(var);
}

void readSensors() {
  vIn  = readAcRms(PIN_VIN)  * CAL_VIN;
  vOut = readAcRms(PIN_VOUT) * CAL_VOUT;

  float acAtPin = readAcRms(PIN_ILOAD);
  float acAtAcs = acAtPin / DIVIDER_RATIO;   // undo the divider
  iLoad = acAtAcs / ACS_SENS;                // to amps
  if (iLoad < I_NOISE_FLOOR) iLoad = 0;

  powerW = vOut * iLoad;                     // apparent power [VA ~ W resistive]
}

// ============================ MOTOR (BTS7960) ============================

void motorStop() {
  analogWrite(PIN_RPWM, 0);
  analogWrite(PIN_LPWM, 0);
}

void motorRun(bool raiseOutput, int duty) {
  digitalWrite(PIN_MEN, HIGH);
  bool forward = raiseOutput != INVERT_MOTOR;
  analogWrite(PIN_RPWM, forward ? duty : 0);
  analogWrite(PIN_LPWM, forward ? 0 : duty);
}

/**
 * Deadband regulation with proportional speed and stall protection:
 * creep near the target, full speed when far away.
 */
void controlLoop() {
  float error = TARGET_V - vOut;     // >0 -> output too low -> raise
  int wanted = 0;                    // 0 stop, 1 raise, 2 lower

  if (vIn < VIN_MIN_OK)              wanted = 0;   // no mains -> stay put
  else if (error >  DEADBAND_V)      wanted = 1;
  else if (error < -DEADBAND_V)      wanted = 2;

  int duty = 0;
  if (wanted != 0) {
    long magnitude = (long)fabs(error);
    magnitude = constrain(magnitude, (long)DEADBAND_V, (long)MAX_ERROR_V);
    duty = map(magnitude, (long)DEADBAND_V, (long)MAX_ERROR_V, DUTY_MIN, DUTY_MAX);
  }

  if (wanted != lastDirection) {     // new intent -> restart the stall timer
    directionSince = millis();
    stalled = false;
  }
  if (wanted != 0 && millis() - directionSince > MAX_RUN_MS) {
    stalled = true;                  // ran too long without reaching the band
  }
  lastDirection = wanted;

  int applied = stalled ? 0 : wanted;

  switch (applied) {
    case 1:  motorRun(true, duty);  break;
    case 2:  motorRun(false, duty); break;
    default: motorStop();           break;
  }
}

// ============================ SSR ============================

void setOutput(bool on) {
  outputOn = on;
  digitalWrite(PIN_SSR, on ? HIGH : LOW);

  if (RESTORE_OUTPUT) {
    prefs.begin("stab", false);
    prefs.putBool("out", on);
    prefs.end();
  }
}

// ============================ OLED ============================

void oledPairingScreen() {
  if (!oledOk) return;
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 0);  oled.println(F("Pair in the web app:"));
  oled.setTextSize(2);
  oled.setCursor(10, 18); oled.println(pairingCode);
  oled.setTextSize(1);
  oled.setCursor(0, 44); oled.print(F("ID: ")); oled.println(deviceId.substring(6));
  oled.setCursor(0, 54);
  oled.print(WiFi.isConnected() ? F("WiFi OK") : F("WiFi..."));
  oled.print(mqtt.connected() ? F("  MQTT OK") : F("  MQTT..."));
  oled.display();
}

void oledLiveScreen() {
  if (!oledOk) return;
  oled.clearDisplay();
  oled.setTextColor(SSD1306_WHITE);
  oled.setTextSize(1);
  oled.setCursor(0, 0);  oled.print(F("IN : ")); oled.print(vIn, 0);  oled.println(F(" V"));
  oled.setCursor(0, 12); oled.print(F("OUT: ")); oled.print(vOut, 0); oled.println(F(" V"));
  oled.setCursor(0, 24); oled.print(F("I  : ")); oled.print(iLoad, 2); oled.println(F(" A"));
  oled.setCursor(0, 36); oled.print(F("P  : ")); oled.print(powerW, 0); oled.println(F(" W"));
  oled.setCursor(0, 52);
  oled.print(outputOn ? F("OUT ON ") : F("OUT OFF"));
  oled.print(stalled ? F(" LIMIT") : F(""));
  oled.print(mqtt.connected() ? F(" online") : F(" ..."));
  oled.display();
}

// ============================ MQTT ============================

void publishInfo() {
  StaticJsonDocument<128> doc;
  if (!claimed) doc["pair"] = pairingCode;   // never expose the code once claimed
  doc["fw"] = FW_VERSION;

  char buffer[128];
  size_t n = serializeJson(doc, buffer);
  mqtt.publish(tInfo.c_str(), (uint8_t*)buffer, n, true);   // retained
}

void publishTelemetry() {
  StaticJsonDocument<192> doc;
  doc["vin"]  = roundf(vIn * 10) / 10.0f;
  doc["vout"] = roundf(vOut * 10) / 10.0f;
  doc["i"]    = roundf(iLoad * 100) / 100.0f;
  doc["p"]    = roundf(powerW * 10) / 10.0f;
  doc["e"]    = roundf(energyWh / 10.0) / 100.0;   // kWh with 2 decimals
  doc["out"]  = outputOn ? 1 : 0;
  doc["fw"]   = FW_VERSION;

  char buffer[192];
  size_t n = serializeJson(doc, buffer);
  mqtt.publish(tTelemetry.c_str(), (uint8_t*)buffer, n, false);
}

void onMqttMessage(char* topic, byte* payload, unsigned int length) {
  String t(topic);
  String msg;
  msg.reserve(length);
  for (unsigned int i = 0; i < length; i++) msg += (char)payload[i];

  if (t == tCommand) {
    StaticJsonDocument<96> doc;
    if (deserializeJson(doc, msg) == DeserializationError::Ok) {
      const char* output = doc["output"] | "";
      bool changed = false;

      if (strcmp(output, "on") == 0 && !outputOn)  { setOutput(true);  changed = true; }
      if (strcmp(output, "off") == 0 && outputOn)  { setOutput(false); changed = true; }

      if (changed) {
        publishTelemetry();          // instant feedback for the dashboard
        lastTelemetry = millis();
      }
    }
    return;
  }

  if (t == tClaimed) {
    bool nowClaimed = (msg == "true" || msg == "1");

    if (nowClaimed && !claimed) {
      claimed = true;
      prefs.begin("stab", false);
      prefs.putBool("claimed", true);
      prefs.end();
      publishInfo();                 // info without the pairing code
    } else if (!nowClaimed && claimed) {
      regeneratePairingCode();       // released: fresh code, show it again
      publishInfo();
    }
  }
}

void mqttConnect() {
  if (mqtt.connected()) return;

  Serial.print(F("MQTT connecting... "));

  // LWT: if we vanish, the broker publishes retained "offline" for us.
  bool ok = mqtt.connect(
    deviceId.c_str(),
    deviceId.c_str(),                 // MQTT user = deviceId
    deviceSecret.c_str(),             // MQTT pass = device secret
    tStatus.c_str(), 1, true, "offline");

  if (!ok) {
    Serial.print(F("failed, rc="));
    Serial.println(mqtt.state());
    return;
  }

  Serial.println(F("connected"));
  mqtt.publish(tStatus.c_str(), "online", true);    // retained
  publishInfo();
  mqtt.subscribe(tCommand.c_str(), 1);
  mqtt.subscribe(tClaimed.c_str(), 1);
}

// ============================ SETUP / LOOP ============================

void setup() {
  Serial.begin(115200);
  delay(200);

  pinMode(PIN_RPWM, OUTPUT);
  pinMode(PIN_LPWM, OUTPUT);
  pinMode(PIN_MEN, OUTPUT);
  pinMode(PIN_SSR, OUTPUT);
  digitalWrite(PIN_MEN, LOW);
  motorStop();

  analogReadResolution(12);

  Wire.begin(PIN_SDA, PIN_SCL);
  oledOk = oled.begin(SSD1306_SWITCHCAPVCC, 0x3C);   // some modules use 0x3D
  if (oledOk) {
    oled.clearDisplay();
    oled.setTextColor(SSD1306_WHITE);
    oled.setTextSize(1);
    oled.setCursor(0, 0);
    oled.println(F("StabilizatorHub VRG"));
    oled.println(F("booting..."));
    oled.display();
  }

  WiFi.mode(WIFI_STA);
  loadIdentity();
  setOutput(outputOn);               // applies the (restored or off) SSR state

  // Captive portal "Stabilizator-Setup" when no WiFi credentials are stored.
  WiFiManager wm;
  wm.setConfigPortalTimeout(180);
  if (!wm.autoConnect("Stabilizator-Setup")) {
    Serial.println(F("WiFi not configured - restarting"));
    delay(2000);
    ESP.restart();
  }

  mqtt.setServer(MQTT_HOST, MQTT_PORT);
  mqtt.setCallback(onMqttMessage);

  lastTelemetry = millis();          // first telemetry after one full interval
}

void loop() {
  if (WiFi.status() != WL_CONNECTED) {
    WiFi.reconnect();
    delay(500);
  }

  static unsigned long lastMqttAttempt = 0;
  if (!mqtt.connected() && millis() - lastMqttAttempt > 3000) {
    lastMqttAttempt = millis();
    mqttConnect();                   // non-blocking retry every 3 s
  }
  mqtt.loop();

  unsigned long now = millis();

  // Regulation always runs - local fail-safe, with or without the server.
  if (now - lastControl >= T_CONTROL_MS) {
    lastControl = now;
    readSensors();
    if (now >= BOOT_GRACE_MS) controlLoop(); else motorStop();
  }

  // Telemetry + local energy integration every 60 s.
  if (now - lastTelemetry >= T_TELEMETRY_MS) {
    double hours = (now - lastTelemetry) / 3600000.0;
    energyWh += powerW * hours;
    lastTelemetry = now;
    if (mqtt.connected()) publishTelemetry();
  }

  if (now - lastOled >= T_OLED_MS) {
    lastOled = now;
    if (claimed) oledLiveScreen(); else oledPairingScreen();
  }
}
