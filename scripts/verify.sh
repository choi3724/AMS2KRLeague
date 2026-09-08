#!/usr/bin/env bash
set -euo pipefail
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(dirname "$0")/verify.ps1" "$@"
