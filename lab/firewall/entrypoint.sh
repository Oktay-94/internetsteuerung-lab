#!/bin/sh
# Firewall container: router between the classroom LAN (10.20.0.0/16) and the WAN,
# NAT like the OPNsense in the original setup, plus the OPNsense-compatible API.
set -eu

LAN_NET="${SIM_LAN_NET:-10.20.0.0/16}"
LAN_IF=$(ip -o -4 addr show | awk '$4 ~ /^10\.20\./ { print $2; exit }')
if [ -z "$LAN_IF" ]; then
    echo "LAN-Interface mit 10.20.x.x nicht gefunden" >&2
    exit 1
fi
echo "LAN-Interface: $LAN_IF, Netz: $LAN_NET"

# Chain "sperre" stays empty (internet allowed) until the API applies the block rule.
nft -f - <<EOF
table inet lab {
    chain sperre {
    }
    chain forward {
        type filter hook forward priority 0; policy accept;
        jump sperre
    }
}
table ip nat {
    chain postrouting {
        type nat hook postrouting priority 100;
        ip saddr $LAN_NET oifname != "$LAN_IF" masquerade
    }
}
EOF

# Self-signed certificate, as on a fresh OPNsense. The fingerprint is shared with the web app,
# which pins it instead of accepting every certificate.
mkdir -p /certs /shared
openssl req -x509 -newkey rsa:2048 -nodes -days 365 -subj "/CN=opnsense-lab" \
    -addext "subjectAltName=IP:10.20.0.1,DNS:firewall" \
    -keyout /certs/key.pem -out /certs/cert.pem 2>/dev/null
openssl x509 -in /certs/cert.pem -outform der | openssl dgst -sha256 -r | cut -d' ' -f1 > /shared/fw-cert.sha256
echo "Zertifikat SHA-256: $(cat /shared/fw-cert.sha256)"

export ASPNETCORE_URLS="https://+:443"
export ASPNETCORE_Kestrel__Certificates__Default__Path=/certs/cert.pem
export ASPNETCORE_Kestrel__Certificates__Default__KeyPath=/certs/key.pem
exec dotnet /app/OpnsenseSim.dll
