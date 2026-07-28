#!/usr/bin/env bash
# Forced command for the CI deploy key. Point authorized_keys at this file so a stolen key gets a
# deploy and nothing else - no shell, no arbitrary command:
#
#   command="/home/deploy/Wbskt/deploy/scripts/ssh-forced-command.sh",no-pty,no-agent-forwarding,\
#   no-port-forwarding,no-X11-forwarding ssh-ed25519 AAAA... ci-deploy
#
# sshd puts whatever the client asked to run in SSH_ORIGINAL_COMMAND and runs this instead. The
# workflows send `<path>/deploy.sh --tag ...`, so drop the path the client chose and keep only the
# arguments - the path is not the client's to pick. deploy.sh validates every argument itself.
set -euo pipefail

if [[ -z "${SSH_ORIGINAL_COMMAND:-}" ]]; then
    echo "this key may only run deploy.sh" >&2
    exit 1
fi

# Word-splitting is the intent: SSH_ORIGINAL_COMMAND is a flat string. deploy.sh rejects anything
# that is not a known flag or a well-formed tag, so a crafted string cannot reach a shell.
read -r -a argv <<< "$SSH_ORIGINAL_COMMAND"

exec "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/deploy.sh" "${argv[@]:1}"
