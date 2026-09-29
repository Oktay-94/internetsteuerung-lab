#!/usr/bin/env bash
# Real OPNsense in a QEMU virtual machine, to test the client against the genuine API.
#   scripts/opnsense-vm.sh start   create overlay disk (once) and boot in the background
#   scripts/opnsense-vm.sh status  show whether the VM runs and the API answers
#   scripts/opnsense-vm.sh stop    shut down
#
# NIC 1 (vtnet0) = LAN 192.168.1.0/24, OPNsense default 192.168.1.1; GUI and API forwarded to
#                  https://127.0.0.1:8443 on the host only.
# NIC 2 (vtnet1) = WAN (QEMU user network 10.0.2.0/24, internet via NAT).
# This matches the assignment OPNsense chooses on first boot (LAN -> vtnet0, WAN -> vtnet1).
# On Apple Silicon x86_64 is emulated (TCG): booting takes several minutes.
set -euo pipefail

VM_DIR="${OPNSENSE_VM_DIR:-$HOME/VMs/opnsense}"
BASIS="$VM_DIR/OPNsense-26.7-nano-amd64.img"
DISK="$VM_DIR/opnsense-lab.qcow2"
PID="$VM_DIR/qemu.pid"
KONSOLE="$VM_DIR/console.log"
PORT="${OPNSENSE_HOST_PORT:-8443}"
KONSOLE_PORT="${OPNSENSE_CONSOLE_PORT:-4555}"   # serial console, e.g. nc 127.0.0.1 4555

laeuft() { [[ -f "$PID" ]] && kill -0 "$(cat "$PID")" 2>/dev/null; }

case "${1:-}" in
    start)
        laeuft && { echo "VM läuft bereits (PID $(cat "$PID"))"; exit 0; }
        [[ -f "$BASIS" ]] || { echo "Image fehlt: $BASIS (siehe docs/opnsense-vm.md)" >&2; exit 1; }
        if [[ ! -f "$DISK" ]]; then
            # Copy-on-write overlay: the downloaded image stays untouched, resetting = deleting the overlay.
            qemu-img create -q -f qcow2 -b "$BASIS" -F raw "$DISK" 8G
            echo "Overlay angelegt: $DISK"
        fi
        : > "$KONSOLE"
        qemu-system-x86_64 \
            -machine q35 -accel tcg,thread=multi -cpu qemu64 -smp 2 -m 2048 \
            -drive file="$DISK",if=virtio,format=qcow2 \
            -netdev "user,id=lan,net=192.168.1.0/24,host=192.168.1.2,dhcpstart=192.168.1.200,hostfwd=tcp:127.0.0.1:${PORT}-192.168.1.1:443" \
            -device virtio-net-pci,netdev=lan \
            -netdev user,id=wan \
            -device virtio-net-pci,netdev=wan \
            -display none \
            -chardev "socket,id=ser0,host=127.0.0.1,port=${KONSOLE_PORT},server=on,wait=off,logfile=${KONSOLE}" \
            -serial chardev:ser0 \
            -daemonize -pidfile "$PID"
        echo "VM gestartet (PID $(cat "$PID")). Konsole: $KONSOLE"
        echo "Weboberfläche nach dem Boot: https://127.0.0.1:${PORT}"
        ;;
    status)
        if laeuft; then echo "VM läuft (PID $(cat "$PID"))"; else echo "VM läuft nicht"; fi
        code=$(curl -sk -o /dev/null -w '%{http_code}' --max-time 5 "https://127.0.0.1:${PORT}/" || true)
        echo "HTTPS auf 127.0.0.1:${PORT}: ${code:-keine Antwort}"
        tail -3 "$KONSOLE" 2>/dev/null || true
        ;;
    stop)
        if laeuft; then kill "$(cat "$PID")"; echo "VM gestoppt"; else echo "VM läuft nicht"; fi
        ;;
    *)
        echo "Aufruf: scripts/opnsense-vm.sh start|status|stop" >&2
        exit 2
        ;;
esac
