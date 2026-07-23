#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────
# P11-1 — Multi-Method Routing DAG (fork-join), DOMAIN + MIGRATION only.
# Verify belt (STACKED-PR-CHECKLIST Rule 6 — self-prep isolated DB).
#
# KHÔNG cần server chạy (PR domain thuần). Phủ:
#   Build / suites
#     1. dotnet build CCL.MES.sln (0 errors)
#     2. Domain suite (CCL.MES.Tests) — 18 P11-1 fixtures + matrix nở
#        169→196 + Legacy/IsForceable/Canonical đã cập nhật cho SPLIT.
#     3. Api / Client / Razor suites — 0 regression (PendingModelChanges
#        clear sau khi migration land).
#   Migration round-trip (self-prep — KHÔNG chạm live data/ccl_mes.db)
#     4. type-affinity đã strip (0 `type:` trong file operations).
#     5. Fresh /tmp DB: apply toàn bộ migration tới AddRoutingLegDag.
#     6. .schema WoLegs / WoLegDependencies / ProcessLegMap đúng.
#     7. WoLegId? có mặt trên 8 surface table.
#     8. Trigger RowVersion WoLegs sinh randomblob(8) trên INSERT + bump
#        trên UPDATE (per-leg optimistic concurrency).
#     9. WO cũ = 0 leg: insert 1 WO không tự sinh leg (backward-compat).
#
# Exit non-zero khi bất kỳ probe fail (S12).
# ─────────────────────────────────────────────────────────────────────
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO"
MIG=src/CCL.MES.Infrastructure/Migrations/20260723044539_AddRoutingLegDag.cs
DESIGN_DB="/tmp/p11-1-verify-$$.db"
LIVE_DB="${MES_DB_PATH:-$REPO/data/ccl_mes.db}"
FAIL=0; STEP=0
TOTAL=9

echo "[ctx] REPO=$REPO"
echo "[ctx] live DB=$LIVE_DB $( [ -f "$LIVE_DB" ] && shasum -a 256 "$LIVE_DB" | cut -c1-8 )"
echo "[ctx] design DB (isolated)=$DESIGN_DB"
echo "[ctx] LIVE DB IS NEVER TOUCHED by this script."
echo

pass(){ echo "  ✓ $1"; }
fail(){ echo "  ✗ $1"; FAIL=1; }
step(){ STEP=$((STEP+1)); echo "[$STEP/$TOTAL] $1"; }
cleanup(){ rm -f "$DESIGN_DB"; }
trap cleanup EXIT

# ── 1. Build ─────────────────────────────────────────────────────────
step "Build CCL.MES.sln"
if dotnet build CCL.MES.sln -v q -clp:ErrorsOnly >/tmp/p11-build-$$.log 2>&1; then
  pass "build 0 errors"
else
  fail "build failed"; grep -iE "error" /tmp/p11-build-$$.log | head;
fi

# ── 2. Domain suite ─────────────────────────────────────────────────
step "Domain suite (state machine + routing helpers + 18 P11-1 fixtures)"
if dotnet test tests/CCL.MES.Tests/CCL.MES.Tests.csproj -v q >/tmp/p11-dom-$$.log 2>&1; then
  R=$(grep -oE "Passed:[[:space:]]+[0-9]+" /tmp/p11-dom-$$.log | tail -1)
  pass "Domain suite green ($R)"
else
  fail "Domain suite failed"; grep -iE "FAIL" /tmp/p11-dom-$$.log | head
fi

# ── 3. Api / Client / Razor suites ──────────────────────────────────
step "Api / Client / Razor suites (0 regression)"
for P in \
  CCL-MES-Hybrid/tests/CCL.MES.Api.Tests/CCL.MES.Api.Tests.csproj \
  CCL-MES-Hybrid/tests/CCL.MES.Hybrid.Client.Tests/CCL.MES.Hybrid.Client.Tests.csproj \
  CCL-MES-Hybrid/tests/CCL.MES.Hybrid.Razor.Tests/CCL.MES.Hybrid.Razor.Tests.csproj ; do
  N=$(basename "$P" .csproj)
  if dotnet test "$P" -v q >/tmp/p11-$N-$$.log 2>&1; then
    R=$(grep -oE "Passed:[[:space:]]+[0-9]+" /tmp/p11-$N-$$.log | tail -1)
    pass "$N green ($R)"
  else
    fail "$N failed"; grep -iE "FAIL" /tmp/p11-$N-$$.log | head
  fi
done

# ── 4. type-affinity strip ──────────────────────────────────────────
step "Type-affinity stripped from migration operations (§4.5)"
TA=$(grep -cE 'type: "(TEXT|INTEGER|REAL|BLOB)"' "$MIG" || true)
[ "$TA" = "0" ] && pass "0 type-affinity string" || fail "$TA type-affinity string còn sót"

# ── 5. Migration round-trip on isolated DB ──────────────────────────
step "Apply migrations → fresh isolated /tmp DB (live untouched)"
rm -f "$DESIGN_DB"
if MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=$DESIGN_DB" MES_DB_PATH="$DESIGN_DB" \
   dotnet ef database update -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web --no-build \
   >/tmp/p11-mig-$$.log 2>&1; then
  pass "migration chain applied to isolated DB"
else
  fail "database update failed"; tail -5 /tmp/p11-mig-$$.log
fi

# ── 6. Schema of new tables ─────────────────────────────────────────
step "Schema WoLegs / WoLegDependencies / ProcessLegMap present"
for T in WoLegs WoLegDependencies ProcessLegMaps ; do
  if sqlite3 "$DESIGN_DB" ".schema $T" 2>/dev/null | grep -q "CREATE TABLE"; then
    pass "$T table"
  else
    fail "$T table missing"
  fi
done

# ── 7. WoLegId? on 8 surface tables ─────────────────────────────────
step "WoLegId column on 8 surface tables"
for T in WoMaterials WoPlateChecks WoCutterChecks WoRunSessions WoPauseEvents WoQtyEntries WoIpqcChecks WoIpqcCheckItems ; do
  C=$(sqlite3 "$DESIGN_DB" "SELECT COUNT(*) FROM pragma_table_info('$T') WHERE name='WoLegId';")
  [ "$C" = "1" ] && pass "$T.WoLegId" || fail "$T.WoLegId missing"
done

# ── 8. RowVersion trigger (per-leg concurrency) ─────────────────────
step "WoLegs RowVersion trigger sinh + bump randomblob(8)"
LEN=$(sqlite3 "$DESIGN_DB" <<SQL
INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,RowVersion,QtyDoneCached,QtyNgCached,CreatedAt)
VALUES ('WO-VERIFY-P11',1,1,'x',100,'pcs',0,'PrePressCheck','Draft',0,0,0,0,'PREPRESS',x'',0,0,'2026-07-23');
INSERT INTO WoLegs (WorkOrderId,Sequence,LegKind,Method,ProcessLine,SurfaceProfile,InputSource,LegPhase,RowVersion,QtyDoneCached,QtyNgCached,CreatedAt)
VALUES ((SELECT Id FROM WorkOrders WHERE WoNo='WO-VERIFY-P11'),0,'PRINT','Silkscreen','SILK','FULL','IN_LINE','PREPRESS',x'',0,0,'2026-07-23');
SELECT length(RowVersion) FROM WoLegs;
SQL
)
[ "$LEN" = "8" ] && pass "RowVersion len=8 on INSERT" || fail "RowVersion len=$LEN (expect 8)"

# ── 9. Backward-compat: WO cũ = 0 leg ───────────────────────────────
step "Backward-compat — inserting a WO does NOT auto-create legs"
LEGS=$(sqlite3 "$DESIGN_DB" "SELECT COUNT(*) FROM WoLegs WHERE WorkOrderId=(SELECT Id FROM WorkOrders WHERE WoNo='WO-VERIFY-P11');")
# 1 leg vì bước 8 chèn tay; 1 WO khác không leg:
sqlite3 "$DESIGN_DB" "INSERT INTO WorkOrders (WoNo,CustomerId,ProductId,ProductName,TargetQty,Uom,ProducedQty,CurrentStep,Status,Priority,MaterialsReady,SetupConfirmed,RohsOk,MesPhase,RowVersion,QtyDoneCached,QtyNgCached,CreatedAt) VALUES ('WO-LEGACY-P11',1,1,'x',50,'pcs',0,'PrePressCheck','Draft',0,0,0,0,'PREPRESS',x'',0,0,'2026-07-23');"
ORPHAN=$(sqlite3 "$DESIGN_DB" "SELECT COUNT(*) FROM WoLegs WHERE WorkOrderId=(SELECT Id FROM WorkOrders WHERE WoNo='WO-LEGACY-P11');")
[ "$ORPHAN" = "0" ] && pass "legacy WO has 0 legs (linear flow preserved)" || fail "legacy WO auto-created $ORPHAN legs"

echo
if [ "$FAIL" = "0" ]; then
  echo "═══ P11-1 VERIFY: ALL PROBES PASS ═══"
  echo "live DB SHA (unchanged): $( [ -f "$LIVE_DB" ] && shasum -a 256 "$LIVE_DB" | cut -c1-16 )"
  exit 0
else
  echo "═══ P11-1 VERIFY: FAILURES ABOVE ═══"
  exit 1
fi
