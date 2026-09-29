# Architektur

## Netz des Labs

Nachbildung der physischen Testumgebung aus dem IHK-Projekt als Container-Netz. Adressen wie im Original: Firewall `10.20.0.1/16`, Lehrer-PC statisch, Schüler-PCs aus dem früheren DHCP-Pool `10.20.10.100–200`.

```mermaid
flowchart LR
    subgraph LAN["LAN 10.20.0.0/16 (internal, kein direkter Weg nach außen)"]
        L["Lehrer-App (Web)<br/>10.20.0.10"]
        S1["schueler-pc-1<br/>10.20.10.101"]
        S2["schueler-pc-2<br/>10.20.10.102"]
    end
    FW["Firewall 10.20.0.1<br/>Router + NAT (nftables)<br/>Regel „Internet-Sperre Raum A“<br/>OPNsense-API (HTTPS)"]
    DB[("MariaDB<br/>internetsteuerung")]
    WAN(("Internet"))

    S1 -- "Default-Route" --> FW
    S2 -- "Default-Route" --> FW
    L -- "REST: search_rule, toggleRule, apply" --> FW
    L -- "Backend-Netz" --> DB
    FW -- "WAN, Masquerade" --> WAN
```

Die Sperre ist echt: Ist die Regel aktiv, verwirft der Firewall-Container weitergeleitete Pakete aus `10.20.0.0/16`. Die Schüler-PCs melden alle zwei Sekunden, ob sie `1.1.1.1:443` erreichen.

## Schichten und Klassen

```mermaid
classDiagram
    direction LR
    class SperrService {
        +GetStatusAsync(Raum) SperrStatus
        +SperrenAsync(Raum, Benutzer, int) SperrErgebnis
        +FreigebenAsync(Raum, string) SperrErgebnis
        +GebeAbgelaufeneFreiAsync() int
    }
    class AuthService {
        +AnmeldenAsync(string, string) Benutzer
    }
    class IFirewallClient {
        <<interface>>
        +IsRuleEnabledAsync(uuid) bool?
        +SetRuleEnabledAsync(uuid, bool) bool
        +ApplyAsync() bool
    }
    class OpnsenseClient
    class ISperreSpeicher { <<interface>> }
    class IProtokoll { <<interface>> }
    class IBenutzerRepository { <<interface>> }
    class IRaumRepository { <<interface>> }
    class IClock { <<interface>> }
    class AutoFreigabeDienst
    class Steuerung_razor
    class HauptForm

    SperrService --> IFirewallClient
    SperrService --> ISperreSpeicher
    SperrService --> IProtokoll
    SperrService --> IRaumRepository
    SperrService --> IClock
    AuthService --> IBenutzerRepository
    AuthService --> IProtokoll
    OpnsenseClient ..|> IFirewallClient
    AutoFreigabeDienst --> SperrService
    Steuerung_razor --> SperrService
    HauptForm --> SperrService
```

| Projekt | Aufgabe |
|---|---|
| `Internetsteuerung.Core` | Domäne und Logik ohne Abhängigkeit zu UI, Datenbank oder HTTP. Dadurch testbar mit Fake-Uhr und Fake-Firewall |
| `Internetsteuerung.Infrastructure` | MariaDB-Zugriff (asynchron, parametrisiert), `OpnsenseClient`, Zertifikats-Pinning, Registrierung im DI-Container |
| `Internetsteuerung.Web` | ASP.NET Core Blazor: Anmeldung, Steuerung, Protokoll, REST-API, Health-Checks, Hintergrunddienst |
| `Internetsteuerung.WinForms` | Desktop-Client wie im Original (Login-Dialog, Hauptfenster mit Ampel und Countdown) |
| `OpnsenseSim` | OPNsense-kompatible API im Lab, schaltet eine echte nftables-Regel |

Im Original lag die Logik direkt in `Form1`, und `Database` zeigte selbst `MessageBox`-Dialoge an (Doku 3.4.2). Jetzt kennt der Core keine Oberfläche. Web und Desktop teilen sich dieselbe Logik.

## Datenmodell

```mermaid
erDiagram
    benutzer {
        INT id PK
        VARCHAR benutzername UK
        VARCHAR passwort_hash "PBKDF2"
        ENUM rolle "Lehrer, Admin"
        TINYINT aktiv
    }
    raum {
        INT id PK
        VARCHAR name
        CHAR firewall_rule_uuid
        TINYINT aktiv
    }
    sperre {
        INT raum_id PK, FK
        DATETIME gesperrt_seit
        DATETIME sperre_ende "NULL = ohne Aufhebung"
        VARCHAR gesperrt_von
    }
    protokoll {
        BIGINT id PK
        DATETIME zeitpunkt
        INT raum_id
        VARCHAR akteur
        VARCHAR aktion
        VARCHAR details
    }
    raum ||--o| sperre : "hat höchstens eine"
```

`benutzer` und `raum` entsprechen dem Original. API-Key, Secret und Basis-URL stehen nicht mehr in `raum`, sondern in der Konfiguration. `sperre` und `protokoll` sind neu.

## Ablauf einer zeitgesteuerten Sperre

```mermaid
sequenceDiagram
    actor Lehrkraft
    participant UI as Web oder WinForms
    participant S as SperrService
    participant FW as Firewall-API
    participant DB as MariaDB
    participant D as AutoFreigabeDienst

    Lehrkraft->>UI: „Internet sperren“, 45 Minuten
    UI->>S: SperrenAsync(raum, benutzer, 45)
    S->>FW: POST toggleRule/{uuid}/1
    S->>FW: POST apply
    alt beide erfolgreich
        S->>DB: sperre (Ende = jetzt + 45 min)
        S->>DB: protokoll „Gesperrt“
        S-->>UI: Erfolg, Countdown startet
    else Fehler
        S->>DB: protokoll „Fehler“
        S-->>UI: „Die Firewall-Regel konnte nicht geändert werden.“
    end
    loop alle 2 Sekunden
        D->>S: GebeAbgelaufeneFreiAsync()
        S->>DB: abgelaufene Sperren?
    end
    Note over D,S: nach 45 Minuten, auch wenn kein Browser offen ist
    S->>FW: POST toggleRule/{uuid}/0, apply
    S->>DB: sperre löschen, protokoll „Automatisch freigegeben“
```
