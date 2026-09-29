#!/usr/bin/env python3
"""Creates an OPNsense API key through the VM's serial console, without the web GUI.

Logs in on the console (default credentials of a fresh image), opens the shell (menu 8) and calls
OPNsense\\Auth\\API::createKey, the same function the GUI uses. Key and secret are written to a
file with mode 600 and never printed.

  scripts/opnsense-apikey.py [benutzer] [zieldatei]
"""
import json
import os
import re
import socket
import sys
import time

HOST, PORT = "127.0.0.1", int(os.environ.get("OPNSENSE_CONSOLE_PORT", "4555"))
_ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
BENUTZER = _ARGS[0] if _ARGS else "root"
ZIEL = _ARGS[1] if len(_ARGS) > 1 else os.path.expanduser("~/VMs/opnsense/api.env")
LOGIN, PASSWORT = "root", os.environ.get("OPNSENSE_ROOT_PASSWORT", "opnsense")


def verbinde(timeout: float = 600) -> socket.socket:
    ende = time.time() + timeout
    while True:
        try:
            s = socket.create_connection((HOST, PORT), timeout=5)
            s.settimeout(1)
            return s
        except OSError:
            if time.time() > ende:
                raise
            time.sleep(2)


def warte(s: socket.socket, muster: str, timeout: float, puffer: list[str]) -> str:
    ende = time.time() + timeout
    while time.time() < ende:
        try:
            daten = s.recv(4096).decode("utf-8", "replace")
            puffer.append(daten)
        except socket.timeout:
            pass
        text = "".join(puffer)
        if re.search(muster, text):
            return text
    raise TimeoutError(f"'{muster}' nicht gesehen")


def sende(s: socket.socket, text: str) -> None:
    s.sendall(text.encode() + b"\r")


def main() -> int:
    s = verbinde()
    puffer: list[str] = []
    sende(s, "")
    # The console can be at the login prompt, in the OPNsense menu or already in a root shell.
    text = warte(s, r"login:\s*$|Enter an option:\s*$|# $", 900, puffer)
    if re.search(r"login:\s*$", text):
        sende(s, LOGIN)
        warte(s, r"Password:", 30, puffer)
        sende(s, PASSWORT)
        text = warte(s, r"Enter an option:\s*$", 120, puffer)
    if re.search(r"Enter an option:\s*$", text):
        puffer.clear()
        sende(s, "8")
        warte(s, r"# $", 60, puffer)
    puffer.clear()
    # --erneuern: revoke the key currently stored in the target file first (rotation after a leak).
    widerrufen = ""
    if "--erneuern" in sys.argv and os.path.exists(ZIEL):
        with open(ZIEL) as f:
            alt = dict(z.strip().split("=", 1) for z in f if "=" in z).get("OPNSENSE_KEY", "")
        if re.fullmatch(r"[A-Za-z0-9+/=]+", alt):
            widerrufen = f"$a->dropKey(\"{BENUTZER}\",\"{alt}\");"
    php = (
        "php -r 'require_once(\"/usr/local/opnsense/mvc/script/load_phalcon.php\");"
        "$a=(new \\OPNsense\\Auth\\AuthenticationFactory())->get(\"Local API\");"
        + widerrufen +
        f"echo \"APIKEY=\".json_encode($a->createKey(\"{BENUTZER}\")).\"=ENDE\";'"
    )
    sende(s, php)
    text = warte(s, r"APIKEY=\{.*\}=ENDE", 120, puffer)
    daten = json.loads(re.search(r"APIKEY=(\{.*?\})=ENDE", text).group(1))
    sende(s, "exit")
    s.close()

    os.makedirs(os.path.dirname(ZIEL), exist_ok=True)
    fd = os.open(ZIEL, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f:
        f.write(f"OPNSENSE_KEY={daten['key']}\nOPNSENSE_SECRET={daten['secret']}\n")
    print(f"API-Schlüssel für {BENUTZER} angelegt, gespeichert in {ZIEL} (Rechte 600)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
