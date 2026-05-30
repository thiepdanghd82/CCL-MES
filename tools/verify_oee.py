#!/usr/bin/env python3
"""
verify_oee.py — Kiểm chứng công thức OEE khớp ví dụ chuẩn ngành (Vorne).

Chạy:  python3 tools/verify_oee.py
Trả về exit code 0 nếu tất cả test PASS, khác 0 nếu có test FAIL.
Dùng được trong CI để bảo vệ công thức OEE khỏi bị sửa sai.
"""
import sys


def compute(run_min, down_min, setup_min, good, reject, ideal_cycle_sec):
    total = good + reject
    planned = run_min + down_min + setup_min
    availability = run_min / planned if planned > 0 else 0.0
    ideal_min = ideal_cycle_sec * total / 60.0
    performance = min(1.0, ideal_min / run_min) if run_min > 0 else 0.0
    quality = good / total if total > 0 else 0.0
    return availability, performance, quality, availability * performance * quality


def approx(a, b, tol=0.005):
    return abs(a - b) <= tol


def main():
    ok = True

    # Test 1 — ví dụ chuẩn Vorne: A=88.8%, P=86.1%, Q=97.8%, OEE=74.8%
    a, p, q, o = compute(373, 420 - 373, 0, 18848, 19271 - 18848, 1.0)
    t1 = approx(a, 0.888) and approx(p, 0.861) and approx(q, 0.978) and approx(o, 0.748)
    print(f"[{'PASS' if t1 else 'FAIL'}] Vorne: A={a*100:.1f}% P={p*100:.1f}% Q={q*100:.1f}% OEE={o*100:.1f}% (chuẩn 88.8/86.1/97.8/74.8)")
    ok &= t1

    # Test 2 — máy hoàn hảo: tất cả 100%
    a, p, q, o = compute(100, 0, 0, 6000, 0, 1.0)
    t2 = approx(o, 1.0)
    print(f"[{'PASS' if t2 else 'FAIL'}] Perfect: OEE={o*100:.0f}% (kỳ vọng 100%)")
    ok &= t2

    # Test 3 — có downtime + phế phẩm
    a, p, q, o = compute(80, 20, 0, 4000, 200, 1.0)
    t3 = approx(a, 0.80) and approx(q, 0.952) and approx(p, 0.875)
    print(f"[{'PASS' if t3 else 'FAIL'}] Mixed: A={a*100:.1f}% P={p*100:.1f}% Q={q*100:.1f}% OEE={o*100:.1f}%")
    ok &= t3

    print("\n" + ("✅ TẤT CẢ TEST PASS" if ok else "❌ CÓ TEST FAIL"))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
