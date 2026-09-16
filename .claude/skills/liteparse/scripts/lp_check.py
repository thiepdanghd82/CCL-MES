#!/usr/bin/env python3
"""Check the LiteParse environment and report what will and will not work.

    python3 lp_check.py
    python3 lp_check.py --file sample.pdf    # also do a real parse + is-complex

Exit code 0 if liteparse imports, 1 otherwise.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time


def which_any(*names) -> str | None:
    for n in names:
        p = shutil.which(n)
        if p:
            return p
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--file", help="Optional document to smoke-test against")
    args = ap.parse_args()

    print(f"Python       : {sys.version.split()[0]}  ({sys.executable})")

    try:
        import liteparse
        print(f"liteparse    : {liteparse.__version__}")
    except ImportError as exc:
        print(f"liteparse    : NOT INSTALLED  ({exc})")
        print("\n  pip install liteparse")
        return 1

    lit = which_any("lit", "liteparse")
    if lit:
        try:
            ver = subprocess.run([lit, "--version"], capture_output=True, text=True,
                                 timeout=20).stdout.strip()
        except Exception:  # noqa: BLE001
            ver = ""
        print(f"lit CLI      : {lit}  {ver}")
    else:
        print("lit CLI      : not on PATH (library API still works)")

    soffice = which_any("soffice", "libreoffice")
    print(f"LibreOffice  : {soffice or 'NOT FOUND — .docx/.xlsx/.pptx/.odt will fail'}")

    tessdata = os.getenv("TESSDATA_PREFIX")
    print(f"TESSDATA_PREFIX: {tessdata or 'unset (bundled Tesseract data is used)'}")

    print("\nInput formats")
    print("-" * 58)
    print("  OK   PDF")
    print("  OK   Images (jpg png gif bmp tiff webp svg) — native Rust conversion")
    print(f"  {'OK ' if soffice else '-- '}  Office / OpenDocument — needs LibreOffice")

    node = shutil.which("node")
    npx = shutil.which("npx")
    print(f"\nNode         : {node or 'absent'}   npx: {npx or 'absent'}")
    print("               (npm i @llamaindex/liteparse for the Node binding,")
    print("                @llamaindex/liteparse-wasm for the browser build)")

    docker = shutil.which("docker")
    print(f"Docker       : {docker or 'absent'}   (needed for the REST/gRPC server image)")

    if args.file:
        from liteparse import LiteParse
        path = args.file
        if not os.path.exists(path):
            print(f"\n--file not found: {path}")
            return 0
        print(f"\nSmoke test on {path}")
        print("-" * 58)
        try:
            t0 = time.perf_counter()
            stats = LiteParse(quiet=True).is_complex(path)
            t1 = time.perf_counter()
            flagged = [s for s in stats if s.needs_ocr]
            print(f"  is_complex : {len(stats)} pages in {(t1 - t0) * 1000:.0f} ms; "
                  f"{len(flagged)} need OCR"
                  + (f" ({', '.join(sorted({r for s in flagged for r in s.reasons}))})"
                     if flagged else ""))

            t0 = time.perf_counter()
            res = LiteParse(output_format="markdown", ocr_enabled=False,
                            quiet=True).parse(path)
            t1 = time.perf_counter()
            print(f"  parse      : {res.total_pages} pages, {len(res.text)} chars "
                  f"in {(t1 - t0) * 1000:.0f} ms")
            print(f"  creator    : {res.creator}   producer: {res.producer}")
            items = res.pages[0].text_items if res.pages else []
            print(f"  page 1     : {len(items)} text items")
            if items:
                it = items[0]
                print(f"  first item : {it.text[:50]!r} @ ({it.x:.1f}, {it.y:.1f})")
        except Exception as exc:  # noqa: BLE001
            print(f"  FAILED: {type(exc).__name__}: {exc}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
