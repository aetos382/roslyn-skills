#!/usr/bin/env bash
# PostToolUse hook: validate whichever manifest the edited file belongs to.
# The repository root holds the marketplace manifest and plugin/ holds the plugin manifest,
# so the two are validated by separate commands, exactly as CI does it.
set -u

path=$(jq -r '.tool_input.file_path // empty' | tr '\\' '/')
[ -n "$path" ] || exit 0

status=0

case "$path" in
    */plugin/*) claude plugin validate ./plugin --strict || status=1 ;;
esac

case "$path" in
    */marketplace.json) claude plugin validate . --strict || status=1 ;;
esac

exit "$status"
