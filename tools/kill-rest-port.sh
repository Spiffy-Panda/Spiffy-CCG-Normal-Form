#!/usr/bin/env bash
# kill-rest-port.sh — terminate whatever is holding the Ccgnf.Rest HTTP port.
#
# Usage:
#   tools/kill-rest-port.sh             # kills listener on 19397 (default)
#   tools/kill-rest-port.sh 19398       # kills listener on an alternate port
#
# Call this when `make rest` or `dotnet run --project src/Ccgnf.Rest` fails
# with "address already in use" — typically a previous run didn't shut down
# cleanly (preview panel still holding the port, stray dotnet process, etc.).
#
# Tries Linux tools first (lsof, then fuser); on Windows / WSL it falls back
# to netstat.exe + taskkill.exe so it works without installing extra tools.
# Exits 0 when nothing is listening.

set -euo pipefail

PORT="${1:-19397}"

kill_linux() {
    local pids
    if command -v lsof >/dev/null 2>&1; then
        pids=$(lsof -iTCP:"$PORT" -sTCP:LISTEN -t 2>/dev/null || true)
        if [ -n "$pids" ]; then
            echo "kill-rest-port: killing PID(s) $pids on port $PORT (lsof)"
            # shellcheck disable=SC2086
            kill -9 $pids
            return 0
        fi
    fi
    if command -v fuser >/dev/null 2>&1; then
        if fuser -k "${PORT}/tcp" >/dev/null 2>&1; then
            echo "kill-rest-port: killed listener on port $PORT (fuser)"
            return 0
        fi
    fi
    return 1
}

kill_windows() {
    if ! command -v netstat.exe >/dev/null 2>&1; then return 1; fi
    if ! command -v taskkill.exe >/dev/null 2>&1; then return 1; fi
    local pids
    pids=$(netstat.exe -ano \
        | awk -v port=":$PORT" '$2 ~ port"$" && $4 == "LISTENING" { print $5 }' \
        | sort -u | tr -d '\r')
    if [ -z "$pids" ]; then return 1; fi
    echo "kill-rest-port: killing PID(s) $pids on port $PORT (netstat.exe)"
    for pid in $pids; do
        taskkill.exe //F //PID "$pid" >/dev/null 2>&1 || true
    done
    return 0
}

if kill_linux; then exit 0; fi
if kill_windows; then exit 0; fi

echo "kill-rest-port: no listener on port $PORT."
exit 0
