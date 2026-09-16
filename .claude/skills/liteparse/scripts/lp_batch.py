#!/usr/bin/env python3
"""Batch-parse a folder of documents with LiteParse.

Mirrors the source tree into an output tree, writes Markdown / text / JSON,
and records a manifest with per-file page counts, timings and the
is-complex verdict.

Examples
--------
    python3 lp_batch.py ~/Documents/Specs -o ~/Documents/Specs_md
    python3 lp_batch.py ./in -o ./out --route            # OCR only where needed
    python3 lp_batch.py ./in -o ./out --format json --extract-blocks
    python3 lp_batch.py ./in -o ./out --screenshots ./shots --dpi 200
    python3 lp_batch.py ./in -o ./out --pool 4 --timeout 30
    python3 lp_batch.py ./in --dry-run

Requires: pip install liteparse   (LibreOffice for Office formats)
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import time
import traceback
from dataclasses import asdict, is_dataclass
from pathlib import Path

PDF_EXTS = {".pdf"}
IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".svg"}
OFFICE_EXTS = {
    ".doc", ".docx", ".docm", ".odt", ".rtf", ".pages",
    ".ppt", ".pptx", ".pptm", ".odp", ".key",
    ".xls", ".xlsx", ".xlsm", ".ods", ".csv", ".tsv", ".numbers",
}
DEFAULT_EXTS = PDF_EXTS | IMAGE_EXTS | OFFICE_EXTS

SKIP_DIRS = {".git", "__pycache__", "node_modules", ".venv", "venv", ".idea", ".vscode"}


def parse_args(argv=None):
    p = argparse.ArgumentParser(
        description="Batch-parse documents with LiteParse.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    p.add_argument("source", type=Path, help="Source file or folder")
    p.add_argument("-o", "--output", type=Path, help="Output folder (default: <source>_lp)")
    p.add_argument("--format", default="markdown", choices=["markdown", "text", "json"],
                   help="Output format (default: markdown)")
    p.add_argument("--ext", nargs="+", metavar="EXT", help="Only these extensions")
    p.add_argument("--flat", action="store_true", help="Flat output folder, no mirroring")
    p.add_argument("--overwrite", action="store_true", help="Redo files already converted")
    p.add_argument("--recursive", dest="recursive", action="store_true", default=True)
    p.add_argument("--no-recursive", dest="recursive", action="store_false")

    p.add_argument("--route", action="store_true",
                   help="Run is-complex first; parse with OCR only when a page needs it")
    p.add_argument("--no-ocr", action="store_true", help="Never run OCR")
    p.add_argument("--ocr-language", default="eng", help="Tesseract code (eng, vie, chi_sim…)")
    p.add_argument("--ocr-server-url", help="HTTP OCR server URL")
    p.add_argument("--ocr-nonfatal", action="store_true",
                   help="Do not abort a document when OCR fails systemically; "
                        "keep the natively extracted text")
    p.add_argument("--keep-headers-footers", action="store_true",
                   help="Do not strip repeated running headers/footers (also stops "
                        "a short document's title from being dropped)")

    p.add_argument("--extract-blocks", action="store_true",
                   help="Include classified layout blocks (json format only)")
    p.add_argument("--extract-images", metavar="DIR",
                   help="Extract embedded images into DIR")
    p.add_argument("--screenshots", metavar="DIR", help="Also render page PNGs into DIR")
    p.add_argument("--dpi", type=float, default=150, help="Render DPI (default: 150)")
    p.add_argument("--target-pages", help='Page selection, e.g. "1-5,10"')
    p.add_argument("--max-pages", type=int, default=1000)
    p.add_argument("--password", help="Password for protected documents")

    p.add_argument("--pool", type=int, metavar="N",
                   help="Worker-pool size (real parallelism; required for --timeout)")
    p.add_argument("--timeout", type=float, metavar="SEC",
                   help="Hard per-document deadline; needs --pool")

    p.add_argument("--dry-run", action="store_true")
    p.add_argument("-q", "--quiet", action="store_true", help="Only print the summary")
    return p.parse_args(argv)


def collect_files(source: Path, exts: set[str], recursive: bool) -> list[Path]:
    if source.is_file():
        return [source]
    files: list[Path] = []
    for root, dirs, names in os.walk(source):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        for name in sorted(names):
            if name.startswith("."):
                continue
            path = Path(root) / name
            if path.suffix.lower() in exts:
                files.append(path)
        if not recursive:
            break
    return sorted(files)


def out_suffix(fmt: str) -> str:
    return {"markdown": ".md", "text": ".txt", "json": ".json"}[fmt]


def output_path_for(src: Path, root: Path, out_root: Path, flat: bool, fmt: str) -> Path:
    suffix = out_suffix(fmt)
    if flat or root.is_file():
        return out_root / (src.stem + suffix)
    return out_root / src.relative_to(root).with_suffix(suffix)


def build_kwargs(args, ocr_enabled: bool) -> dict:
    kw = dict(
        ocr_enabled=ocr_enabled,
        ocr_language=args.ocr_language,
        output_format="json" if args.format == "json" else args.format,
        dpi=args.dpi,
        max_pages=args.max_pages,
        quiet=True,
    )
    if args.ocr_server_url:
        kw["ocr_server_url"] = args.ocr_server_url
    if args.target_pages:
        kw["target_pages"] = args.target_pages
    if args.password:
        kw["password"] = args.password
    if args.ocr_nonfatal:
        kw["ocr_failure_fatal"] = False
    if args.keep_headers_footers:
        kw["keep_headers_footers"] = True
    if args.extract_blocks:
        kw["extract_blocks"] = True
    if args.extract_images:
        kw["extract_images"] = True
        kw["image_output_dir"] = args.extract_images
    if args.pool:
        kw["pool_size"] = args.pool
        if args.timeout:
            kw["parse_timeout"] = args.timeout
    return kw


def result_to_json(result) -> str:
    def enc(o):
        if is_dataclass(o):
            d = asdict(o)
            return {k: v for k, v in d.items() if not isinstance(v, bytes)}
        if isinstance(o, bytes):
            return f"<{len(o)} bytes>"
        return str(o)

    payload = {
        "total_pages": result.total_pages,
        "creator": result.creator,
        "producer": result.producer,
        "text": result.text,
        "pages": result.pages,
    }
    return json.dumps(payload, default=enc, ensure_ascii=False, indent=2)


def main(argv=None) -> int:
    args = parse_args(argv)

    if args.timeout and not args.pool:
        sys.exit("--timeout requires --pool (only the worker pool can enforce a deadline)")

    source = args.source.expanduser().resolve()
    if not source.exists():
        sys.exit(f"Source not found: {source}")

    exts = {(e if e.startswith(".") else "." + e).lower()
            for e in (args.ext if args.ext else DEFAULT_EXTS)}

    files = collect_files(source, exts, args.recursive)
    if not files:
        print(f"No matching files under {source}")
        return 0

    out_root = (args.output.expanduser().resolve() if args.output
                else source.parent / ((source.name if source.is_dir() else source.stem) + "_lp"))

    jobs, skipped = [], 0
    for src in files:
        dest = output_path_for(src, source, out_root, args.flat, args.format)
        if dest.exists() and not args.overwrite:
            skipped += 1
            continue
        jobs.append((src, dest))

    if args.dry_run:
        print(f"Source : {source}")
        print(f"Output : {out_root}   format={args.format}")
        print(f"Matched: {len(files)}   to parse: {len(jobs)}   already done: {skipped}")
        for src, dest in jobs[:200]:
            print(f"  {src}  ->  {dest}")
        if len(jobs) > 200:
            print(f"  ... and {len(jobs) - 200} more")
        return 0

    try:
        from liteparse import LiteParse, ParseTimeoutError
    except ImportError:
        sys.exit("liteparse is not installed: pip install liteparse")

    ocr_default = not args.no_ocr
    parser = LiteParse(**build_kwargs(args, ocr_default))
    probe = LiteParse(quiet=True, pool_size=args.pool) if args.route else None
    parser_no_ocr = None
    if args.route and not args.no_ocr:
        parser_no_ocr = LiteParse(**build_kwargs(args, False))

    shot_root = Path(args.screenshots).expanduser().resolve() if args.screenshots else None
    if shot_root:
        shot_root.mkdir(parents=True, exist_ok=True)
    out_root.mkdir(parents=True, exist_ok=True)

    rows, counts = [], {"ok": 0, "empty": 0, "error": 0, "timeout": 0}
    total = len(jobs)

    for i, (src, dest) in enumerate(jobs, 1):
        started = time.perf_counter()
        needs_ocr_pages, reasons, used_ocr = "", "", ocr_default
        status, err, pages, chars = "ok", "", 0, 0
        try:
            active = parser
            if probe is not None:
                stats = probe.is_complex(str(src))
                flagged = [s for s in stats if s.needs_ocr]
                needs_ocr_pages = ",".join(str(s.page_number) for s in flagged)
                reasons = ",".join(sorted({r for s in flagged for r in s.reasons}))
                used_ocr = bool(flagged) and not args.no_ocr
                if not used_ocr and parser_no_ocr is not None:
                    active = parser_no_ocr

            result = active.parse(str(src))
            pages = result.total_pages
            text = result_to_json(result) if args.format == "json" else (result.text or "")
            chars = len(text)
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(text, encoding="utf-8")
            if not (result.text or "").strip():
                status = "empty"

            if shot_root:
                sub = shot_root / dest.stem
                sub.mkdir(parents=True, exist_ok=True)
                for s in active.screenshot(str(src)):
                    if s.is_solid_fill:
                        continue
                    (sub / f"page_{s.page_num:04d}.png").write_bytes(s.image_bytes)

        except ParseTimeoutError as exc:
            status, err = "timeout", f"ParseTimeoutError: {exc}"
        except Exception as exc:  # noqa: BLE001 - one bad file must not stop the run
            status, err = "error", f"{type(exc).__name__}: {exc}"

        elapsed_ms = round((time.perf_counter() - started) * 1000, 1)
        counts[status] += 1
        rows.append({
            "source": str(src), "output": str(dest), "status": status,
            "pages": pages, "chars": chars, "ms": elapsed_ms,
            "ocr_used": used_ocr, "pages_needing_ocr": needs_ocr_pages,
            "reasons": reasons, "error": err,
        })
        if not args.quiet:
            try:
                shown = src.relative_to(source)
            except ValueError:
                shown = src.name
            mark = {"ok": "OK   ", "empty": "EMPTY", "error": "FAIL ", "timeout": "TIME "}[status]
            extra = err if err else f"{pages}p {elapsed_ms}ms" + (" ocr" if used_ocr else "")
            print(f"[{i}/{total}] {mark} {shown}  ({extra})")

    for p in (parser, probe, parser_no_ocr):
        try:
            if p is not None:
                p.close()
        except Exception:  # noqa: BLE001
            pass

    manifest = out_root / "_manifest.csv"
    with manifest.open("w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(sorted(rows, key=lambda r: r["source"]))

    bad = [r for r in rows if r["status"] in ("error", "timeout")]
    if bad:
        with (out_root / "_errors.log").open("w", encoding="utf-8") as fh:
            for r in bad:
                fh.write(f"{r['source']}\n    {r['error']}\n")

    print(f"\nParsed {counts['ok']} | empty {counts['empty']} | failed {counts['error']} "
          f"| timeout {counts['timeout']} | skipped {skipped}")
    print(f"Output   : {out_root}")
    print(f"Manifest : {manifest}")
    if bad:
        print(f"Errors   : {out_root / '_errors.log'}")
    return 1 if bad else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\nInterrupted", file=sys.stderr)
        sys.exit(130)
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
