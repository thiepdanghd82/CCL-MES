#!/usr/bin/env python3
"""
oee_from_csv.py — Tính OEE từ file log sản xuất CSV (xuất từ máy hoặc nhập tay).

Công thức (khớp với OeeService.ComputeAsync trong .NET):
    Availability = Run / (Run + Stop + Setup)
    Performance  = min(1.0, IdealCycleTime(s) * TotalCount / Run(s))
    Quality      = Good / (Good + Reject)
    OEE          = Availability * Performance * Quality

Định dạng CSV đầu vào (header bắt buộc):
    machine,event,start,end,good,reject
  - event: Run | Stop | Setup | Idle
  - start,end: ISO datetime, ví dụ 2026-05-30T08:00:00
  - good,reject: số nguyên (để 0 nếu không có)

Cách dùng:
    python3 tools/oee_from_csv.py tools/sample_production_log.csv
    python3 tools/oee_from_csv.py my_log.csv --ideal-cycle 0.4 --json
"""
import argparse
import csv
import json
import sys
from collections import defaultdict
from datetime import datetime


def parse_dt(s: str) -> datetime:
    s = (s or "").strip()
    for fmt in ("%Y-%m-%dT%H:%M:%S", "%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M", "%Y-%m-%d %H:%M"):
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    raise ValueError(f"Không đọc được thời gian: {s!r}")


def minutes(start: str, end: str) -> float:
    return (parse_dt(end) - parse_dt(start)).total_seconds() / 60.0


def compute_oee(run_min, stop_min, setup_min, good, reject, ideal_cycle_sec):
    total = good + reject
    planned = run_min + stop_min + setup_min
    availability = run_min / planned if planned > 0 else 0.0
    ideal_min = ideal_cycle_sec * total / 60.0
    performance = min(1.0, ideal_min / run_min) if run_min > 0 else 0.0
    quality = good / total if total > 0 else 0.0
    oee = availability * performance * quality
    return dict(
        planned_min=round(planned, 2), run_min=round(run_min, 2),
        stop_min=round(stop_min, 2), setup_min=round(setup_min, 2),
        good=good, reject=reject,
        availability=round(availability, 4), performance=round(performance, 4),
        quality=round(quality, 4), oee=round(oee, 4),
    )


def main():
    ap = argparse.ArgumentParser(description="Tính OEE theo máy từ CSV log.")
    ap.add_argument("csv_path", help="Đường dẫn file CSV log.")
    ap.add_argument("--ideal-cycle", type=float, default=0.4,
                    help="Ideal cycle time (giây/sản phẩm). Mặc định 0.4 (máy ACNC3).")
    ap.add_argument("--json", action="store_true", help="In kết quả dạng JSON.")
    args = ap.parse_args()

    agg = defaultdict(lambda: dict(run=0.0, stop=0.0, setup=0.0, good=0, reject=0))
    try:
        with open(args.csv_path, newline="", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            required = {"machine", "event", "start", "end"}
            if not required.issubset({c.strip() for c in (reader.fieldnames or [])}):
                sys.exit(f"❌ CSV thiếu cột. Cần ít nhất: {sorted(required)}")
            for row in reader:
                m = row["machine"].strip()
                ev = row["event"].strip().lower()
                dur = minutes(row["start"], row["end"])
                if ev == "run":
                    agg[m]["run"] += dur
                elif ev == "stop":
                    agg[m]["stop"] += dur
                elif ev == "setup":
                    agg[m]["setup"] += dur
                agg[m]["good"] += int(row.get("good") or 0)
                agg[m]["reject"] += int(row.get("reject") or 0)
    except FileNotFoundError:
        sys.exit(f"❌ Không tìm thấy file: {args.csv_path}")

    results = {}
    for m, a in sorted(agg.items()):
        results[m] = compute_oee(a["run"], a["stop"], a["setup"],
                                 a["good"], a["reject"], args.ideal_cycle)

    if args.json:
        print(json.dumps(results, ensure_ascii=False, indent=2))
        return

    print(f"\n=== OEE theo máy (ideal cycle = {args.ideal_cycle}s) ===\n")
    hdr = f"{'May':<10}{'Run':>8}{'Stop':>8}{'Setup':>8}{'Good':>8}{'Reject':>8}{'Avail':>8}{'Perf':>8}{'Qual':>8}{'OEE':>8}"
    print(hdr)
    print("-" * len(hdr))
    for m, r in results.items():
        print(f"{m:<10}{r['run_min']:>8.1f}{r['stop_min']:>8.1f}{r['setup_min']:>8.1f}"
              f"{r['good']:>8}{r['reject']:>8}"
              f"{r['availability']*100:>7.1f}%{r['performance']*100:>7.1f}%"
              f"{r['quality']*100:>7.1f}%{r['oee']*100:>7.1f}%")
    print()


if __name__ == "__main__":
    main()
