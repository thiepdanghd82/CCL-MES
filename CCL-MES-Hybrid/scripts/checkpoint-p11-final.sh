#!/usr/bin/env bash
# P11 operator checkpoint (final) — full fork-join walkthrough qua wire
# thật trên copy live DB. Delegates to p11-live-verify.sh (8 scenario:
# auth · header 428/400/409 · T1 no-fork · T2 join · T3 HARD-gate+join ·
# unmapped 422 · rework · concurrency per-leg). S12: per-step + SUMMARY +
# non-zero-on-fail. Live DB + :5100/:5050 KHÔNG bị đụng.
set -uo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "═══ P11 CHECKPOINT (final) — delegating to p11-live-verify.sh ═══"
exec bash "$DIR/p11-live-verify.sh" "$@"
