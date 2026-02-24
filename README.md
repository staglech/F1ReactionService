# 🏎️ F1 Reaction Service

A lightweight, fully dockerized .NET 10 background worker that brings the thrill of Formula 1 directly into your Smart Home. 

This service acts as a bridge between the [OpenF1 API](https://openf1.org/) and your MQTT broker. It monitors live race sessions and publishes real-time events—such as Track Status (Red Flags, Safety Cars) and Leader Changes (P1)—so your Home Assistant can trigger spectacular lighting and automation effects.

---

## 💡 Inspiration & Credits
A huge shoutout goes to [Moeren588's Dashboard-Reaction-Service](https://github.com/Moeren588/Dashboard-Reaction-Service). This project was heavily inspired by their awesome work and takes the core idea to the next level by introducing dynamic API fetches, a zero-maintenance architecture, and full test coverage.

## ✨ Features
* **Real-Time Flag Alerts:** Instantly detects Green, Yellow, Red, SC, and VSC flags and pushes them via MQTT.
* **Live P1 Tracking:** Monitors who is currently leading the race (or setting the fastest lap in practice/quali).
* **Zero-Maintenance Driver Grid:** Automatically fetches the current driver lineup and official team colors directly from the OpenF1 API at the start of every session. 
* **Smart Standby:** Automatically goes to sleep when no active session is running and wakes up via a signal or timer to save resources.
* **Hollywood Demo Mode:** Includes a built-in simulation sequence (Formation Lap, Overtakes, Crashes, Red Flags) to easily test your Home Assistant automations without having to wait for Sunday.
* **Clean Architecture:** Fully tested codebase separating the API muscle from the business logic brain.

## 🚀 Quick Start (Docker Compose)

The easiest way to run the F1 Reaction Service is via Docker Compose. Because of the host-mode networking, the container can easily reach your local MQTT broker without complicated port mappings.

```yaml
services:
  f1-reaction-service:
    image: ghcr.io/staglech/f1-reaction-service:latest
    container_name: f1-reaction-service
    restart: unless-stopped
    network_mode: "host"
    environment:
      - MQTT_HOST=127.0.0.1
      - MQTT_PORT=1883
      - MQTT_USER=your_mqtt_user
      - MQTT_PASSWORD=your_mqtt_password
```

## 📡 MQTT Interface

### 📤 Outbound Events (Home Assistant Triggers)
The service publishes lightweight JSON payloads when important events happen on track.

**1. Track Status / Flags (`f1/race/flag_status`)**
Fired whenever the track condition changes (e.g., Green, Yellow, Red, SC, VSC).
```json
{
  "flag": "RED",
  "message": "Session Suspended"
}
```

**2. Leader Change (`f1/race/p1`)**
Fired whenever a new driver takes P1 in the race, or sets the fastest lap in a practice/qualifying session.
```json
{
  "driver": "Lando Norris",
  "driver_number": 1,
  "short_name": "NOR",
  "team": "McLaren",
  "color": "#FF8000",
  "reason": "Race Leader",
  "session": "Race",
  "is_live": true
}
```

---

## 🏡 Home Assistant Integration Guide

To make the integration as seamless as possible, here are fully working YAML examples using the modern Home Assistant syntax.

### 1. Controlling the Service (Script)
Create this script to easily send commands (like starting the demo mode) from a Home Assistant dashboard button.

```yaml
f1_service_control:
  alias: "F1 Service Control"
  description: "Send commands to the F1 Reaction Service"
  icon: mdi:racing-helmet
  mode: single
  fields:
    command:
      name: Command
      description: "Available commands: START, STOP, CALIBRATE_START, DEMO_START"
      required: true
      example: "START"
  sequence:
    - action: mqtt.publish
      data:
        topic: "f1/control"
        payload: "{{ command }}"
```

### 2. The Red Flag Alert (Automation & Light Effect)
This automation listens to the MQTT topic. When a Red Flag is detected, it temporarily saves your current light settings, flashes the lights red for 10 seconds, and then restores them.

```yaml
automation:
  - alias: "F1: Red Flag Alert"
    trigger:
      - trigger: mqtt
        topic: f1/race/flag_status
    condition:
      - condition: template
        value_template: "{{ trigger.payload_json.FLAG == 'RED' }}"
    action:
      # 1. Save the current state of your lights
      - action: scene.create
        data:
          scene_id: before_red_flag
          snapshot_entities:
            - light.living_room_lights

      # 2. Turn the lights bright RED
      - action: light.turn_on
        target:
          entity_id: light.living_room_lights
        data:
          color_name: red
          brightness_pct: 100

      # 3. Wait for 10 seconds to show the alert
      - delay:
          seconds: 10

      # 4. Restore the lights to how they were before
      - action: scene.turn_on
        target:
          entity_id: scene.before_red_flag
    mode: restart
```

## 🛠️ Development & Testing
This project embraces clean architecture and uses `xUnit` alongside `FluentAssertions` and `NSubstitute` for unit testing. 
To run the tests locally:
```bash
dotnet test
```