#!/usr/bin/env python3
"""
import_qc_library.py — Phương án C, Bước 1.
Nạp THƯ VIỆN HẠNG MỤC KIỂM (IPQC/FQC/OQC) từ IPQC_Library_CMES_v2.csv vào
bảng CheckItemLibraries của CCL-MES SQLite, đồng thời mở rộng ReasonCode
(Kind=Scrap) theo các Defect code trong thư viện.

Idempotency:
  UPSERT theo natural key ItemId (ON CONFLICT(ItemId) DO UPDATE). Chạy 2 lần
  với cùng input ra cùng kết quả (lần 2: 0 insert, 0 update, 0 reason mới).
  ReasonCode chỉ thêm code chưa tồn tại (Kind=Scrap). Tất cả trong 1 transaction.

"Audit" (theo mẫu import_npi.py): in BEFORE/AFTER + counters
  (lib_inserted / lib_updated / reason_added) — không tạo AuditLog row.

Tiền đề: chạy `dotnet run --project src/CCL.MES.Web` 1 lần để EF tạo bảng
  CheckItemLibraries (migration AddCheckItemLibrary), HOẶC app đã boot.

Cách dùng:
  python3 tools/import_qc_library.py --csv IPQC_Library_CMES_v2.csv --db data/ccl_mes.db
"""
import argparse
import csv
import os
import sqlite3
import sys
from datetime import datetime, timezone

csv.field_size_limit(10_000_000)

# Map vị trí cột 0..18 (đã đối soát file v2) → tên cột DB.
COLS = ["ItemId", "ProcessLine", "GroupLabel", "Code", "ItemVi", "ItemEn",
        "AcceptanceVi", "AcceptanceEn", "Method", "Severity", "Aql", "Sampling",
        "CheckType", "DefectCode", "ParetoPct", "ShortForm", "IsoRef",
        "AppliesWhen", "Note"]
NOT_NULL = {"ProcessLine", "GroupLabel", "Code", "ItemVi", "ItemEn", "AcceptanceVi", "AcceptanceEn"}


def now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")


def read_rows(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        recs = list(csv.reader(f))
    rows, skipped = [], 0
    for r in recs[1:]:                       # bỏ header
        if not r or not (r[0] or "").strip():
            skipped += 1
            continue
        d = {}
        for i, name in enumerate(COLS):
            v = (r[i].strip() if i < len(r) and r[i] is not None else "")
            d[name] = v if (v != "" or name in NOT_NULL) else None
        rows.append(d)
    return rows, skipped


def main():
    ap = argparse.ArgumentParser(description="Nạp thư viện hạng mục kiểm vào CCL-MES SQLite.")
    ap.add_argument("--csv", default="IPQC_Library_CMES_v2.csv", help="File CSV thư viện.")
    ap.add_argument("--db", default="data/ccl_mes.db", help="Đường dẫn file SQLite.")
    args = ap.parse_args()

    if not os.path.exists(args.db):
        sys.exit(f"ERROR: không thấy DB {args.db}. Chạy app/migration tạo bảng CheckItemLibraries trước.")
    if not os.path.exists(args.csv):
        sys.exit(f"ERROR: không thấy CSV {args.csv}.")

    rows, skipped = read_rows(args.csv)
    print(f"DB   : {args.db}")
    print(f"CSV  : {args.csv}  (rows={len(rows)}, skipped={skipped})")

    conn = sqlite3.connect(args.db)
    cur = conn.cursor()
    # Bảng phải tồn tại (migration).
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='CheckItemLibraries';")
    if cur.fetchone() is None:
        conn.close()
        sys.exit("ERROR: bảng CheckItemLibraries chưa tồn tại — chạy migration AddCheckItemLibrary trước.")

    before_lib = cur.execute("SELECT COUNT(*) FROM CheckItemLibraries;").fetchone()[0]
    existing_ids = {r[0] for r in cur.execute("SELECT ItemId FROM CheckItemLibraries;").fetchall()}
    # ReasonCode.Kind lưu dạng STRING qua EF .HasConversion<string>() ("Scrap"), KHÔNG phải int.
    existing_scrap = {r[0] for r in cur.execute("SELECT Code FROM ReasonCodes WHERE Kind='Scrap';").fetchall()}
    print(f"BEFORE: CheckItemLibraries={before_lib}, ReasonCode(Scrap)={len(existing_scrap)}")

    lib_inserted = lib_updated = reason_added = 0
    ts = now()
    # Cột business sẽ update + dùng so sánh thay đổi (UpdatedAt KHÔNG nằm trong điều
    # kiện đổi để re-run không bị tính là update khi field giữ nguyên → idempotent thật).
    upd_cols = COLS[1:] + ["Sort"]
    set_sql = ", ".join(f'"{c}"=excluded."{c}"' for c in upd_cols) + ', "UpdatedAt"=:UpdatedAt, "UpdatedBy"=:UpdatedBy'
    where_sql = " OR ".join(f'"{c}" IS NOT excluded."{c}"' for c in upd_cols)
    ins_cols = ["ItemId", "ProcessLine", "ProductCode", "QcStage", "GroupLabel", "Code", "ItemVi",
                "ItemEn", "AcceptanceVi", "AcceptanceEn", "Method", "Severity", "Aql", "Sampling",
                "CheckType", "DefectCode", "ParetoPct", "ShortForm", "IsoRef", "AppliesWhen", "Note",
                "Active", "Sort", "CreatedAt", "CreatedBy"]
    ins_sql = (f'INSERT INTO "CheckItemLibraries" ({",".join(chr(34)+c+chr(34) for c in ins_cols)}) '
               f'VALUES ({",".join(":"+c for c in ins_cols)}) '
               f'ON CONFLICT("ItemId") DO UPDATE SET {set_sql} WHERE {where_sql}')
    try:
        cur.execute("BEGIN;")
        for i, d in enumerate(rows):
            payload = {**d, "QcStage": "IPQC", "ProductCode": None, "Active": 1,
                       "Sort": (i + 1) * 10, "CreatedAt": ts, "CreatedBy": "import_qc_library",
                       "UpdatedAt": ts, "UpdatedBy": "import_qc_library"}
            is_new = d["ItemId"] not in existing_ids
            cur.execute(ins_sql, payload)
            if is_new:
                lib_inserted += 1
            elif cur.rowcount:        # chỉ đếm khi WHERE khớp (field thực sự đổi)
                lib_updated += 1

        # Mở rộng ReasonCode (Scrap) theo Defect code.
        defects = []
        seen = set()
        for d in rows:
            dc = (d.get("DefectCode") or "").strip()
            if dc and dc not in existing_scrap and dc not in seen:
                seen.add(dc)
                defects.append(dc)
        for j, dc in enumerate(defects):
            cur.execute(
                'INSERT INTO "ReasonCodes" ("Code","LabelEn","LabelVi","Kind","Active","Sort","CreatedAt","CreatedBy") '
                "VALUES (?,?,?,'Scrap',1,?,?,?)",
                (dc, dc, dc, 200 + (j + 1) * 10, ts, "import_qc_library"),
            )
        reason_added = len(defects)
        conn.commit()
    except Exception as exc:
        conn.rollback()
        conn.close()
        sys.exit(f"ERROR: transaction rolled back — {exc}")

    after_lib = cur.execute("SELECT COUNT(*) FROM CheckItemLibraries;").fetchone()[0]
    after_scrap = cur.execute("SELECT COUNT(*) FROM ReasonCodes WHERE Kind='Scrap';").fetchone()[0]
    conn.close()

    print("")
    print("RESULT:")
    print(f"  lib_inserted = {lib_inserted}")
    print(f"  lib_updated  = {lib_updated}")
    print(f"  reason_added = {reason_added}")
    print(f"AFTER : CheckItemLibraries={after_lib}, ReasonCode(Scrap)={after_scrap}")
    print("OK (idempotent — chạy lại sẽ ra 0/0/0).")


if __name__ == "__main__":
    main()
