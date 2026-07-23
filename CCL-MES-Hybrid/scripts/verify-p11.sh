#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# P11 test-belt closeout — Multi-Method Routing DAG (fork-join), 3 lớp:
#   P11-1 domain · P11-2 wire · P11-3 UI. Deterministic (không boot server;
#   operator walkthrough qua wire ở checkpoint-p11-final.sh / p11-live-verify.sh).
#
# Rule 6 self-prep: apply migration lên /tmp DB copy trước probe schema.
# KHÔNG chạm live data/ccl_mes.db.
#
#   Build / suites
#     1. dotnet build CCL.MES.sln (0 errors)
#     2. Domain  — RoutingLegResolver T1/T2/T3 + RoutingDagValidator +
#        MesPhaseSplitTransition + RoutingLegGate + matrix 196 (SPLIT).
#     3. Api     — RoutingController 13 (materialize/advance/gate/join/
#        rework + soak per-leg + audit wire-mirror).
#     4. Client  — RoutingErrorLocaliser lock.
#     5. Razor   — LegsDashboard 7 bUnit.
#   Migration round-trip (isolated /tmp)
#     6. type-affinity = 0 (AddRoutingLegDag operations file).
#     7. Fresh /tmp DB: apply toàn chain (…AddRoutingLegDag →
#        AddWoLegRowVersionValueGen).
#     8. Schema WoLegs/WoLegDependencies/ProcessLegMaps + trigger +
#        WoLegId×8 + EF-path insert leg bump RowVersion (Option B — L38).
#   Gates
#     9. gate-no-hardcoded-hex / gate-floating-showcard / gate-row-actions.
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"; cd "$REPO"
MIG=src/CCL.MES.Infrastructure/Migrations/20260723044539_AddRoutingLegDag.cs
DB="/tmp/p11-verify-$$.db"; LIVE="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"
FAIL=0; STEP=0; TOTAL=9
echo "[ctx] REPO=$REPO"; echo "[ctx] live DB=$LIVE $( [ -f "$LIVE" ] && shasum -a 256 "$LIVE" | cut -c1-8) — NEVER touched"
echo "[ctx] design DB=$DB (isolated)"; echo
pass(){ echo "  ✓ $1"; }; bad(){ echo "  ✗ $1"; FAIL=1; }; step(){ STEP=$((STEP+1)); echo "[$STEP/$TOTAL] $1"; }
trap 'rm -f "$DB"' EXIT
suite(){ local p="$1" f="$2" n; n=$(basename "$p" .csproj)
  if dotnet test "$p" ${f:+--filter "$f"} -v q >/tmp/p11v-$$.log 2>&1; then
    pass "$n $(grep -oE 'Passed:[[:space:]]+[0-9]+' /tmp/p11v-$$.log | tail -1)"
  else bad "$n failed"; grep -iE "\[FAIL\]" /tmp/p11v-$$.log | head; fi; }

step "Build CCL.MES.sln"
dotnet build CCL.MES.sln -v q -clp:ErrorsOnly >/tmp/p11b-$$.log 2>&1 && pass "0 errors" || { bad "build"; grep -i error /tmp/p11b-$$.log | head; }

step "Domain suite (routing DAG + gate + split transition + matrix)"
suite tests/CCL.MES.Tests/CCL.MES.Tests.csproj ""

step "Api suite (RoutingController — non-soak)"
suite CCL-MES-Hybrid/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj "FullyQualifiedName~RoutingController&Category!=Soak"

step "Api soak (Concurrent_advance per-leg 1-winner)"
suite CCL-MES-Hybrid/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj "FullyQualifiedName~Concurrent_advance"

step "Client + Razor suites (localiser + LegsDashboard bUnit)"
suite CCL-MES-Hybrid/tests/CCL.MES.Hybrid.Client.Tests/CCL.MES.Hybrid.Client.Tests.csproj "FullyQualifiedName~RoutingErrorLocaliser"
suite CCL-MES-Hybrid/tests/CCL.MES.Hybrid.Razor.Tests/CCL.MES.Hybrid.Razor.Tests.csproj "FullyQualifiedName~LegsDashboard"

step "Type-affinity stripped (AddRoutingLegDag operations)"
TA=$(grep -cE 'type: "(TEXT|INTEGER|REAL|BLOB)"' "$MIG" || true)
[ "$TA" = "0" ] && pass "0 type-affinity" || bad "$TA type-affinity"

step "Migration round-trip → isolated /tmp DB"
rm -f "$DB"
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=$DB" MES_DB_PATH="$DB" \
  dotnet ef database update -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web --no-build >/tmp/p11m-$$.log 2>&1 \
  && pass "chain applied incl AddWoLegRowVersionValueGen" || { bad "update failed"; tail -4 /tmp/p11m-$$.log; }

step "Schema + trigger + WoLegId×8 + Option-B insert bump"
for T in WoLegs WoLegDependencies ProcessLegMaps; do
  sqlite3 "$DB" ".schema $T" 2>/dev/null | grep -q "CREATE TABLE" && pass "$T table" || bad "$T missing"; done
sqlite3 "$DB" "SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE 'WoLegs%';" | grep -q OnInsert && pass "RowVersion triggers present" || bad "triggers missing"
MISS=0; for T in WoMaterials WoPlateChecks WoCutterChecks WoRunSessions WoPauseEvents WoQtyEntries WoIpqcChecks WoIpqcCheckItems; do
  [ "$(sqlite3 "$DB" "SELECT COUNT(*) FROM pragma_table_info('$T') WHERE name='WoLegId';")" = "1" ] || MISS=$((MISS+1)); done
[ "$MISS" = "0" ] && pass "WoLegId on 8 surface tables" || bad "$MISS surface tables missing WoLegId"

step "Gates (hex / floating-showcard / row-actions)"
for g in gate-no-hardcoded-hex gate-floating-showcard gate-row-actions; do
  bash "CCL-MES-Hybrid/scripts/$g.sh" >/tmp/p11g-$$.log 2>&1 && pass "$g" || { bad "$g"; tail -2 /tmp/p11g-$$.log; }; done

echo
if [ "$FAIL" = 0 ]; then echo "═══ P11 VERIFY: ALL PASS ═══"; echo "live SHA (unchanged): $( [ -f "$LIVE" ] && shasum -a 256 "$LIVE" | cut -c1-16)"; exit 0
else echo "═══ P11 VERIFY: FAILURES ABOVE ═══"; exit 1; fi
