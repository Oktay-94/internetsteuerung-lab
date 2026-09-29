#!/bin/sh
# Simulated student PC: default route via the firewall (10.20.0.1), then every 2 seconds
# a TCP check towards the internet. The result is reported to the teacher app.
set -u

ip route replace default via 10.20.0.1
NAME="${HOSTNAME:-schueler-pc}"
LETZTER=""

while true; do
    if nc -z -w 2 1.1.1.1 443 >/dev/null 2>&1; then ONLINE=true; else ONLINE=false; fi
    if [ "$ONLINE" != "$LETZTER" ]; then
        echo "$(date -u +%H:%M:%S) $NAME: Internet $( [ "$ONLINE" = true ] && echo erreichbar || echo GESPERRT )"
        LETZTER="$ONLINE"
    fi
    curl -s -o /dev/null -m 2 -H "Content-Type: application/json" \
        -d "{\"name\":\"$NAME\",\"online\":$ONLINE}" http://10.20.0.10:8080/api/lab/heartbeat || true
    sleep 2
done
