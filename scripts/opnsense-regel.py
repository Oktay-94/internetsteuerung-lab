#!/usr/bin/env python3
"""Creates the block rule "Internet-Sperre Raum A" on a real OPNsense through its REST API.

Rule as in the original setup: interface LAN, action block, IPv4+IPv6, source LAN net, destination any,
initially disabled. Reads OPNSENSE_KEY/OPNSENSE_SECRET from ~/VMs/opnsense/api.env and appends
OPNSENSE_RULE_UUID. The certificate is pinned against ~/VMs/opnsense/cert.sha256.

  scripts/opnsense-regel.py [basis-url]
"""
import base64
import hashlib
import json
import os
import socket
import ssl
import sys
import urllib.error
import urllib.request

BASIS = (sys.argv[1] if len(sys.argv) > 1 else "https://127.0.0.1:8443").rstrip("/")
VM = os.path.expanduser("~/VMs/opnsense")
ENV = os.path.join(VM, "api.env")
BESCHREIBUNG = "Internet-Sperre Raum A"


def lese_env() -> dict[str, str]:
    with open(ENV) as f:
        return dict(z.strip().split("=", 1) for z in f if "=" in z)


def gepinnter_kontext() -> ssl.SSLContext:
    # Self-signed certificate: verify the fingerprint instead of the chain.
    erwartet = open(os.path.join(VM, "cert.sha256")).read().strip().upper()
    host, port = BASIS.split("//")[1].split(":")
    kontext = ssl.create_default_context()
    kontext.check_hostname = False
    kontext.verify_mode = ssl.CERT_NONE
    with socket.create_connection((host, int(port)), timeout=10) as roh, kontext.wrap_socket(roh) as tls:
        tatsaechlich = hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest().upper()
    if tatsaechlich != erwartet:
        sys.exit(f"Zertifikat passt nicht zum Pin: {tatsaechlich[:16]}...")
    return kontext


def api(methode: str, pfad: str, daten: dict | None, env: dict[str, str], kontext: ssl.SSLContext) -> dict:
    token = base64.b64encode(f"{env['OPNSENSE_KEY']}:{env['OPNSENSE_SECRET']}".encode()).decode()
    kopf = {"Authorization": f"Basic {token}"}
    body = None
    if methode == "POST":
        # Content-Type only with a body: OPNsense answers a bodiless request with JSON content type with 400.
        body = json.dumps(daten if daten is not None else {}).encode()
        kopf["Content-Type"] = "application/json"
    anfrage = urllib.request.Request(f"{BASIS}/api/{pfad}", data=body, method=methode, headers=kopf)
    try:
        with urllib.request.urlopen(anfrage, context=kontext, timeout=60) as antwort:
            return json.loads(antwort.read() or b"{}")
    except urllib.error.HTTPError as fehler:
        sys.exit(f"{methode} {pfad}: HTTP {fehler.code} {fehler.read().decode('utf-8', 'replace')[:300]}")


def main() -> int:
    env = lese_env()
    kontext = gepinnter_kontext()

    vorhanden = [r for r in api("GET", "firewall/filter/search_rule?show_all=1", None, env, kontext).get("rows", [])
                 if r.get("description") == BESCHREIBUNG]
    if vorhanden:
        uuid = vorhanden[0]["uuid"]
        print(f"Regel existiert bereits: {uuid}")
    else:
        regel = {"rule": {
            "enabled": "0", "action": "block", "quick": "1", "interface": "lan", "direction": "in",
            "ipprotocol": "inet46", "protocol": "any", "source_net": "lan", "destination_net": "any",
            "description": BESCHREIBUNG,
        }}
        antwort = api("POST", "firewall/filter/add_rule", regel, env, kontext)
        if antwort.get("result") != "saved":
            sys.exit(f"Anlegen fehlgeschlagen: {json.dumps(antwort)[:300]}")
        uuid = antwort["uuid"]
        print(f"Regel angelegt: {uuid}")

    # OPNsense 26.7 answers "OK", older versions "ok": compare case-insensitively.
    status = api("POST", "firewall/filter/apply", {}, env, kontext).get("status", "").strip().lower()
    print(f"apply: {status}")

    if env.get("OPNSENSE_RULE_UUID") != uuid:
        with open(ENV, "a") as f:
            f.write(f"OPNSENSE_RULE_UUID={uuid}\n")
    return 0 if status == "ok" else 1


if __name__ == "__main__":
    sys.exit(main())
