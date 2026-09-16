#!/usr/bin/env bash
#
# CI gate — BẢNG CON CỦA WorkOrders PHẢI CÓ KHOÁ NGOẠI, VÀ BẰNG CHỨNG QC PHẢI RESTRICT.
#
# Gác hai luật, cả hai đều vừa phải trả giá bằng hai đợt migration (16-09-2026):
#
#   (A) Cột `WoId` / `WorkOrderId` mà KHÔNG có quan hệ về `WorkOrder` ⇒ đỏ.
#       Ratchet 0. Đo 16-09: 19 bảng trỏ về WO, **6 bảng không có FK nào** —
#       sống sót qua nhiều phase vì không ai canh. Hệ quả: xoá một WO để lại
#       dòng mồ côi IM LẶNG, và phải quét tay bằng purge-applied.sql.
#
#   (B) Hồ sơ QC CÓ CHỮ KÝ phải `Restrict`, không được `Cascade`. Đây là bằng
#       chứng — hồ sơ đã đóng băng, có chữ ký, mang ra cho khách audit xem. Xoá
#       WO mà mất luôn hồ sơ ấy thì hỏi lại không còn gì để đưa. Luật này từng
#       bị vi phạm ngay trong đợt 1: `WoIpqcChecks` đặt Cascade, tới khi chốt
#       "ảnh QC là bằng chứng" mới lộ ra mâu thuẫn và phải sửa lại thứ vừa áp.
#
# ĐỌC GÌ: `MesDbContextModelSnapshot.cs` — nguồn sự thật của model NẰM TRONG MÃ
# NGUỒN, nên gate chạy được ở CI không cần DB. (Bốn gate khác trong bộ này đọc
# DB live; ở đây không cần, và không cần thì đừng phụ thuộc.)
#
# Usage: bash scripts/gate-wo-child-fk.sh              (exit 0 = pass, 1 = fail)
#        bash scripts/gate-wo-child-fk.sh --self-test
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$here/../.." && pwd)"
# MES_SNAPSHOT cho phép chĩa gate vào một snapshot khác — dùng để thử detector
# với snapshot LỊCH SỬ (chứng minh nó bắt được ca thật, không chỉ ca tiêm).
SNAP="${MES_SNAPSHOT:-$ROOT/src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs}"

# Bảng mang BẰNG CHỨNG QC — bắt buộc Restrict. Thêm bảng hồ sơ có chữ ký mới thì
# thêm vào đây; đó là chỗ duy nhất khai danh sách này.
EVIDENCE="WoTraceSnapshot WoIpqcCheck WoQcCheck"

# MIỄN TRỪ CÓ THỜI HẠN — khai tường minh, KHÔNG giấu vào một con số baseline.
# Mỗi dòng: Entity.Cột|lý do. In ra mỗi lần chạy để không ai quên nó tồn tại.
#
# SemiLot.SourceWorkOrderId: "WO nào SẢN XUẤT RA lô này". Chưa gắn FK được vì
# chưa quyết được hành vi: một lô do WO-A làm ra có thể đang được WO-B giữ chỗ
# (SemiAllocations), nên Cascade là XOÁ TỒN KHO của WO khác. Cột lại NOT NULL
# nên SetNull không khả thi nếu không đổi schema. Chờ Henry — xem tờ trình
# STOP-gate. Đây cũng là chỗ gate bản đầu (16-09) MÙ: nó chỉ so tên chính xác
# `WoId`/`WorkOrderId` nên bỏ qua mọi tên biến thể. Nay so theo HẬU TỐ.
PENDING="SemiLot.SourceWorkOrderId|chờ Henry quyết hành vi xoá lô bán thành phẩm"

BASELINE=0

scan() {  # $1 = đường dẫn snapshot
  python3 - "$1" "$EVIDENCE" "$PENDING" <<'PY'
import re, sys, os

path, evidence = sys.argv[1], sys.argv[2].split()
pending = {p.split("|")[0]: p.split("|")[1] for p in sys.argv[3].split("\n") if "|" in p}
if not os.path.exists(path):
    print("ERR|không thấy snapshot: " + path); sys.exit(0)
src = open(path, encoding="utf-8").read()

# Snapshot khai mỗi entity ở HAI khối: khối property và khối quan hệ. Gom cả hai
# theo tên entity rồi mới xét — xét từng khối rời sẽ báo động giả.
props, rels = {}, {}
for m in re.finditer(r'modelBuilder\.Entity\("([\w.]+)",\s*b\s*=>\s*\{', src):
    name = m.group(1).split(".")[-1]
    # cắt tới khối Entity kế tiếp (hoặc hết file)
    nxt = src.find('modelBuilder.Entity("', m.end())
    body = src[m.end(): nxt if nxt != -1 else len(src)]
    props.setdefault(name, set()).update(re.findall(r'b\.Property<[^>]+>\("(\w+)"\)', body))
    # HasOne("Target", ...) ... .HasForeignKey("Col") ... .OnDelete(DeleteBehavior.X)
    for r in re.finditer(
            r'\.HasOne\("([\w.]+)"[^)]*\)(.*?)(?=\.HasOne\("|\Z)', body, re.S):
        tgt = r.group(1).split(".")[-1]
        tail = r.group(2)
        fk = re.search(r'\.HasForeignKey\("(\w+)"\)', tail)
        od = re.search(r'\.OnDelete\(DeleteBehavior\.(\w+)\)', tail)
        rels.setdefault(name, []).append(
            (tgt, fk.group(1) if fk else None, od.group(1) if od else "Cascade"))

# So theo HẬU TỐ, không so tên chính xác: `SourceWorkOrderId` cũng là cột trỏ về
# WO. Bản đầu của gate so chính xác nên mù với mọi tên biến thể — đúng cái bệnh
# gate này sinh ra để chặn.
WOCOL = re.compile(r"(WorkOrderId|WoId)$")
for ent in sorted(props):
    wocols = [c for c in props[ent] if WOCOL.search(c)]
    if not wocols: continue
    for col in sorted(wocols):
        hit = [r for r in rels.get(ent, []) if r[0] == "WorkOrder" and r[1] == col]
        if not hit:
            key = "%s.%s" % (ent, col)
            if key in pending:
                print("WAIT|%s|%s|%s" % (ent, col, pending[key]))
            else:
                print("MISS|%s|%s" % (ent, col))
        elif ent in evidence and hit[0][2] != "Restrict":
            print("WEAK|%s|%s|%s" % (ent, col, hit[0][2]))

# Bằng chứng KHÔNG trỏ thẳng WO (vd WoQcPhoto treo vào item) vẫn phải Restrict
for ent in evidence:
    if ent not in props:
        print("GONE|%s" % ent)
PY
}

# ── self-test ─────────────────────────────────────────────────────────────────
if [ "${1:-}" = "--self-test" ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  cp "$SNAP" "$tmp/snap.cs" 2>/dev/null || { echo "[gate:wo-child-fk] self-test FAILED — không đọc được snapshot"; exit 1; }

  # WAIT = miễn trừ đã khai tường minh, KHÔNG phải vi phạm — lọc ra trước khi
  # khẳng định "bản nguyên vẹn phải im lặng". (Chính self-test bắt được chỗ này
  # lúc gate mở rộng sang so khớp hậu tố.)
  clean="$(scan "$tmp/snap.cs" | grep -v '^WAIT|' || true)"
  if [ -n "$clean" ]; then
    echo "[gate:wo-child-fk] self-test FAILED — bản sao NGUYÊN VẸN đã bị báo vi phạm (báo động giả):"
    printf '%s\n' "$clean" | sed 's/^/    /'
    exit 1
  fi

  # Tiêm hai kiểu vi phạm vào entity ĐẦU TIÊN khớp mẫu (không nhắm entity cụ
  # thể — mục đích là chứng minh detector bắt được KIỂU vi phạm):
  #   (A) gỡ một quan hệ WorkOrder ⇒ cột WoId/WorkOrderId trơ trọi, không FK
  #   (B) hạ một Restrict xuống Cascade ⇒ bằng chứng QC mất gác
  python3 - "$tmp/snap.cs" <<'PY'
import io,sys,re
p=sys.argv[1]; s=io.open(p,encoding="utf-8").read()
a=re.sub(r'(b\.HasOne\("CCL\.MES\.Domain\.Entities\.WorkOrder", null\)\s*\.WithMany\(\)\s*\.HasForeignKey\("WoId"\)\s*\.OnDelete\(DeleteBehavior\.Cascade\)\s*\.IsRequired\(\);)',
         '', s, count=1)
assert a != s, "tiêm A thất bại — cấu trúc snapshot đã đổi"
b=a.replace('.HasForeignKey("WorkOrderId")\n                        .OnDelete(DeleteBehavior.Restrict)',
            '.HasForeignKey("WorkOrderId")\n                        .OnDelete(DeleteBehavior.Cascade)', 1)
assert b != a, "tiêm B thất bại — cấu trúc snapshot đã đổi"
io.open(p,"w",encoding="utf-8").write(b)
PY
  [ $? -eq 0 ] || { echo "[gate:wo-child-fk] self-test FAILED — không tiêm được (snapshot đã đổi cấu trúc)."; exit 1; }

  out="$(scan "$tmp/snap.cs")"
  if echo "$out" | grep -q '^MISS|' && echo "$out" | grep -q '^WEAK|'; then
    echo "[gate:wo-child-fk] self-test OK — bản nguyên vẹn XANH, và bắt được CẢ HAI kiểu vi phạm:"
    echo "$out" | sed 's/^/    /'
    exit 0
  fi
  echo "[gate:wo-child-fk] self-test FAILED — tiêm 2 vi phạm mà detector không bắt đủ:"
  printf '%s\n' "${out:-(không báo gì)}"
  exit 1
fi

# ── quét thật ─────────────────────────────────────────────────────────────────
out="$(scan "$SNAP")"
if echo "$out" | grep -q '^ERR|'; then
  echo "[gate:wo-child-fk] ${out#ERR|}"
  echo "  Không kết luận được — sửa đường dẫn, ĐỪNG gỡ gate."
  exit 2
fi
printf '%s\n' "$out" | grep '^WAIT|' | while IFS='|' read -r _ ent col why; do
  echo "[gate:wo-child-fk] ⏳ MIỄN TRỪ: $ent.$col — $why"
done
out="$(printf '%s\n' "$out" | grep -v '^WAIT|' || true)"
count="$(printf '%s' "$out" | grep -c . || true)"

if [ "$count" -gt "$BASELINE" ]; then
  echo "[gate:wo-child-fk] vi phạm: $count (baseline $BASELINE)"
  while IFS='|' read -r kind ent col extra; do
    [ -z "$kind" ] && continue
    case "$kind" in
      MISS) echo "  [thiếu FK] $ent.$col trỏ về WorkOrder nhưng KHÔNG có quan hệ nào."
            echo "             → khai b.Entity<$ent>().HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.$col)" ;;
      WEAK) echo "  [bằng chứng mất gác] $ent.$col đang ON DELETE $extra, phải là Restrict."
            echo "             → hồ sơ QC có chữ ký thì DB phải CHẶN, không được xoá theo WO." ;;
      GONE) echo "  [danh sách lệch] entity '$ent' khai trong EVIDENCE nhưng không còn trong model."
            echo "             → sửa danh sách EVIDENCE ở đầu gate cho khớp." ;;
    esac
  done <<< "$out"
  exit 1
fi

echo "[gate:wo-child-fk] vi phạm: $count (baseline $BASELINE)"
echo "[gate:wo-child-fk:OK] mọi cột trỏ WorkOrder đều có FK, và bằng chứng QC vẫn Restrict."
exit 0
