# 🏎️ F1 Reaction Service

Ein smarter C#/.NET Background Service, der die [OpenF1 API](https://openf1.org/) abfragt und Live-Rennereignisse (Flaggen, P1-Wechsel) echtzeitnah an einen MQTT-Broker sendet. Perfekt für die Integration in Home Assistant, um beispielsweise das Wohnzimmer bei einer gelben Flagge blinken zu lassen oder die Lampen in der Farbe des aktuell Führenden (oder des Pole-Setters) erstrahlen zu lassen.

## ✨ Features

* **Aktuelles 2026er Grid:** Beinhaltet eine statische Registry mit allen Fahrern, Teams und exakten Hex-Farbwörtern der Saison 2026 (inkl. Audi & Cadillac).
* **Smarte P1-Logik:** Unterscheidet intelligent zwischen Rennen (Fokus auf den physischen Leader) und Trainings/Qualifyings (Fokus auf die schnellste Runde).
* **Live-Erkennung (`is_live`):** Verhindert "Geister-Lichtshows" an rennfreien Tagen. Ein Event wird nur als `live` geflaggt, wenn das aktuelle Datum in das Zeitfenster der Session fällt.
* **TV-Synchronisation (Delay-Kalibrierung):** Da die API dem TV-Bild oft voraus ist, kann der Dienst per Knopfdruck mit dem Live-Bild synchronisiert werden. Alle folgenden Events werden dann mit exakt diesem Delay gesendet.
* **Ressourcenschonendes Smart-Polling:** Der Dienst läuft dauerhaft, geht aber in einen Schlafmodus (0 API-Aufrufe), wenn er nicht benötigt wird. Er wacht per MQTT-Befehl instanziiert (in Millisekunden) auf.
* **Demo-Modus:** Eingebaute Test-Schleife für Home Assistant. Simuliert einen Rennverlauf (Grün, Gelb, Safety Car, Rot, Teamwechsel), ohne die API zu belasten.

---

## 🚀 Installation & Deployment (Docker)

Der Dienst wird als Docker-Container bereitgestellt. Das Image ist für den Host-Mode vorkonfiguriert, damit es problemlos mit dem lokalen Netzwerk und OPNsense/AdGuard harmoniert.

### `docker-compose.yml`

```yaml
services:
  f1-reaction-service:
    image: ghcr.io/staglech/f1reactionservice:latest
    container_name: f1-reaction-service
    restart: unless-stopped
    network_mode: "host" 
    environment:
      - MQTT_SERVER=10.10.10.199
      - MQTT_PORT=1883
      - MQTT_USER=dein_user       # Optional
      - MQTT_PASSWORD=dein_pass   # Optional
```

*Tipp zur Versionierung: Anstatt `:latest` kann auch ein fixes Release-Tag (z.B. `:v1.0.0`) in Dockge verwendet werden, um automatische Updates bei Code-Änderungen auf dem Master-Branch zu verhindern.*

---

## 📡 MQTT Dokumentation

### Eingehende Befehle (Topic: `f1/service/command`)
Sende einfache Strings an dieses Topic, um den Dienst zu steuern:

| Payload | Beschreibung |
| :--- | :--- |
| `START` | Weckt den Dienst aus dem Standby auf und startet das API-Polling (5s Intervall). |
| `STOP` | Beendet das Polling sofort und versetzt den Dienst in den Standby. |
| `CALIBRATE_START` | Berechnet die Verzögerung zwischen der echten Datenzeit und dem TV-Bild (Knopfdruck exakt bei Session-Start/Grüner Flagge am TV). |
| `DEMO_START` | Startet eine künstliche Event-Schleife zum Testen von Hausautomatisierungen (keine API-Abfragen). |

### Ausgehende Events

**1. Flaggen-Status (Topic: `f1/race/flag_status`)**
```json
{
  "FLAG": "YELLOW",
  "MESSAGE": "Yellow in Sector 2"
}
```
*(Mögliche Flags: GREEN, YELLOW, SC, VSC, RED, UNKNOWN)*

**2. P1 Leader / Fastest Lap (Topic: `f1/race/p1`)**
```json
{
  "driver": "Lando Norris",
  "driver_number": "1"
  "short_name": "NOR",
  "team": "McLaren",
  "color": "#F47600",
  "reason": "Race Leader",
  "session": "Race",
  "is_live": true 
}
```
*(Wichtig: `is_live` ist nur `true`, wenn die Session physisch in diesem Moment stattfindet. Alte Trainingsdaten erhalten `false`.)*

---

## 🏠 Home Assistant Integration

Hier sind zwei Beispiele, wie der Dienst nahtlos in Home Assistant integriert werden kann.

### 1. Die Fernbedienung (MQTT Switches & Buttons)
Steuerung des Dienstes direkt aus dem HA-Dashboard. Füge dies in deine `configuration.yaml` (oder mqtt.yaml) ein:

```yaml
mqtt:
  switch:
    - name: "F1 Live Polling"
      command_topic: "f1/service/command"
      payload_on: "START"
      payload_off: "STOP"
      icon: mdi:car-sports
    - name: "F1 Demo Modus"
      command_topic: "f1/service/command"
      payload_on: "DEMO_START"
      payload_off: "STOP"
      icon: mdi:test-tube

  button:
    - name: "F1 TV Sync Kalibrierung"
      command_topic: "f1/service/command"
      payload_press: "CALIBRATE_START"
      icon: mdi:timer-sync
```

### 2. Die Licht-Automation (P1 Farbe)
Diese Automation ändert die Farbe der Wohnzimmerlampe auf die Teamfarbe des Erstplatzierten – aber **nur**, wenn das Event auch wirklich live ist. *Hierbei wird die moderne Home Assistant Syntax verwendet.*

```yaml
alias: "F1: P1 Teamfarbe (Live)"
mode: restart
trigger:
  - platform: mqtt
    topic: "f1/race/p1"
condition:
  # Nur triggern, wenn es sich um ein echtes Live-Event handelt
  - condition: template
    value_template: "{{ trigger.payload_json.is_live == true }}"
action:
  - action: light.turn_on
    target:
      entity_id: light.wohnzimmer # Hier deine Entität anpassen
    data:
      brightness_pct: 100
      rgb_color: >
        {% set hex = trigger.payload_json.color %}
        {% set hex = hex.lstrip('#') %}
        {{ [
          (hex[0:2] | int(base=16)),
          (hex[2:4] | int(base=16)),
          (hex[4:6] | int(base=16))
        ] }}
```

---

## 🛠️ Entwicklung & Versionierung

Dieses Repository nutzt GitHub Actions für den automatischen Docker-Build.
* **Commits auf `master`:** Aktualisieren das Image `ghcr.io/...:master`.
* **Git Tags (`v1.0.0`):** Erzeugen ein stabiles Release-Image und aktualisieren `:latest`.