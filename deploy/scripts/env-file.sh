# shellcheck shell=bash
# Reads keys from the compose .env file without running it. The file is data that docker compose
# parses, not a script: sourcing it would run anything in a value ($(...), backticks) as this user, and
# would break on a value compose accepts but bash does not. Sourced by backup.sh, restore.sh and
# backup-check.sh.

# env_get FILE KEY: prints KEY's value, or nothing when the file does not set it. The last line wins, as
# in compose. Surrounding single or double quotes are removed; nothing inside a value is expanded.
env_get() {
    local file=$1 key=$2 line value
    line=$(grep -E "^[[:space:]]*(export[[:space:]]+)?${key}=" "$file" | tail -n 1) || true
    [[ -n "$line" ]] || return 0
    value=${line#*=}
    value=${value%$'\r'}
    if [[ ${#value} -ge 2 && ( ( ${value:0:1} == '"' && ${value: -1} == '"' ) || ( ${value:0:1} == "'" && ${value: -1} == "'" ) ) ]]; then
        value=${value:1:${#value}-2}
    fi
    printf '%s' "$value"
}

# env_load FILE KEY...: sets and exports each KEY the file sets. A KEY already in the environment (from
# cron or the caller) keeps its value, so a one-off run can override the file.
env_load() {
    local file=$1 key value
    shift
    for key in "$@"; do
        if [[ -n "${!key+set}" ]]; then
            continue
        fi
        value=$(env_get "$file" "$key")
        if [[ -n "$value" ]]; then
            printf -v "$key" '%s' "$value"
            export "${key?}"
        fi
    done
}
