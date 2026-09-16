#!/usr/bin/env sh
# Links this folder into .claude/skills so Claude Code picks the skill up.
# Run from the repo root:  sh tools/diagrify/install.sh
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
mkdir -p "$ROOT/.claude/skills"
if [ -e "$ROOT/.claude/skills/diagrify" ] && [ ! -L "$ROOT/.claude/skills/diagrify" ]; then
  echo "✖ $ROOT/.claude/skills/diagrify already exists and is not a symlink — remove it first."; exit 1
fi
ln -sfn "$ROOT/tools/diagrify" "$ROOT/.claude/skills/diagrify"
node "$ROOT/tools/diagrify/bin/diagrify.mjs" doctor
echo "✔ .claude/skills/diagrify → tools/diagrify"
