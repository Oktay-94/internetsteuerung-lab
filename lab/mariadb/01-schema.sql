-- Schema of the database "internetsteuerung".
-- benutzer and raum follow the original data model; sperre and protokoll are new
-- (persistent automatic release and audit log from the original outlook).

CREATE TABLE IF NOT EXISTS benutzer (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    benutzername  VARCHAR(50)  NOT NULL UNIQUE,
    passwort_hash VARCHAR(255) NOT NULL COMMENT 'PBKDF2, legacy SHA-256 is upgraded on login',
    rolle         ENUM('Lehrer', 'Admin') NOT NULL DEFAULT 'Lehrer',
    aktiv         TINYINT(1)   NOT NULL DEFAULT 1
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- API key, secret and base URL moved to configuration, the room keeps the rule UUID.
CREATE TABLE IF NOT EXISTS raum (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    name               VARCHAR(50) NOT NULL,
    firewall_rule_uuid CHAR(36)    NOT NULL,
    aktiv              TINYINT(1)  NOT NULL DEFAULT 1
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS sperre (
    raum_id       INT         NOT NULL PRIMARY KEY,
    gesperrt_seit DATETIME(3) NOT NULL COMMENT 'UTC',
    sperre_ende   DATETIME(3) NULL COMMENT 'UTC, NULL = ohne automatische Aufhebung',
    gesperrt_von  VARCHAR(50) NOT NULL,
    CONSTRAINT fk_sperre_raum FOREIGN KEY (raum_id) REFERENCES raum (id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS protokoll (
    id        BIGINT AUTO_INCREMENT PRIMARY KEY,
    zeitpunkt DATETIME(3)  NOT NULL COMMENT 'UTC',
    raum_id   INT          NULL,
    akteur    VARCHAR(50)  NOT NULL,
    aktion    VARCHAR(40)  NOT NULL,
    details   VARCHAR(500) NULL,
    INDEX ix_protokoll_zeitpunkt (zeitpunkt)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;
