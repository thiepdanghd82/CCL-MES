#!/usr/bin/env python3
"""Batch-convert a folder of documents to Markdown with MarkItDown.

Walks a source tree, converts every supported file, mirrors the folder
structure into an output tree, and writes a manifest of what happened.

Examples
--------
    python3 md_batch.py ~/Documents/Specs -o ~/Documents/Specs_md
    python3 md_batch.py ./in -o ./out --ext .pdf .docx --workers 4
    python3 md_batch.py ./in --single all.md
    python3 md_batch.py ./in -o ./out --plugins --overwrite
    python3 md_batch.py ./in -o ./out --dry-run

Requires: pip install 'markitdown[all]'
"""

from __future__ import annotations

import argparse
import csv
import os
import sys
import traceback
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

# Extensions MarkItDown handles out of the box (see references/formats.md).
DEFAULT_EXTS = {
    ".pdf", ".docx", ".xlsx", ".xls", ".pptx",
    ".csv", ".json", ".xml", ".html", ".htm", ".txt", ".md",
    ".epub", ".msg", ".ipynb", ".zip",
    ".jpg", ".jpeg", ".png",
    ".wav", ".mp3", ".m4a", ".mp4",
}

SKIP_DIR_NAMES = {".git", ".svn", "__pycache__", "node_modules", ".venv", "venv",
                  ".idea", ".vscode", ".DS_Store"}


def parse_args(argv=None):
    p = argparse.ArgumentParser(
        description="Batch-convert documents to Markdown using MarkItDown.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    p.add_argument("source", type=Path, help="Source file or folder")
    p.add_argument("-o", "--output", type=Path,
                   help="Output folder (default: <source>_md)")
    p.add_argument("--single", type=Path, metavar="FILE",
                   help="Concatenate everything into one Markdown file instead")
    p.add_argument("--ext", nargs="+", metavar="EXT",
                   help="Only these extensions, e.g. --ext .pdf .docx")
    p.add_argument("--flat", action="store_true",
                   help="Put all output in one folder instead of mirroring the tree")
    p.add_argument("--overwrite", action="store_true",
                   help="Reconvert files that already have a .md output")
    p.add_argument("--workers", type=int, default=1,
                   help="Parallel workers (keep at 1 for OCR/Azure backends)")
    p.add_argument("--plugins", action="store_true",
                   help="Enable installed MarkItDown plugins (e.g. OCR)")
    p.add_argument("--llm-model", metavar="MODEL",
                   help="Vision model for image descriptions / OCR, e.g. gpt-4o "
                        "(needs the openai package and OPENAI_API_KEY)")
    p.add_argument("--recursive", dest="recursive", action="store_true", default=True,
                   help="Recurse into subfolders (default)")
    p.add_argument("--no-recursive", dest="recursive", action="store_false",
                   help="Only convert files directly in the source folder")
    p.add_argument("--keep-data-uris", action="store_true",
                   help="Keep base64 data: URIs instead of truncating them")
    p.add_argument("--dry-run", action="store_true",
                   help="List what would be converted and exit")
    p.add_argument("-q", "--quiet", action="store_true", help="Only print the summary")
    return p.parse_args(argv)


def collect_files(source: Path, exts: set[str], recursive: bool) -> list[Path]:
    if source.is_file():
        return [source]
    files: list[Path] = []
    for root, dirs, names in os.walk(source):
        dirs[:] = [d for d in dirs if d not in SKIP_DIR_NAMES and not d.startswith(".")]
        for name in sorted(names):
            if name.startswith("."):
                continue
            path = Path(root) / name
            if path.suffix.lower() in exts:
                files.append(path)
        if not recursive:
            break
    return sorted(files)


def output_path_for(src: Path, source_root: Path, out_root: Path, flat: bool) -> Path:
    if flat or source_root.is_file():
        return out_root / (src.stem + ".md")
    rel = src.relative_to(source_root)
    return out_root / rel.with_suffix(".md")


def build_converter(args):
    from markitdown import MarkItDown

    kwargs = {"enable_plugins": bool(args.plugins)}
    if args.llm_model:
        try:
            from openai import OpenAI
        except ImportError:
            sys.exit("--llm-model needs the openai package: pip install openai")
        kwargs["llm_client"] = OpenAI()
        kwargs["llm_model"] = args.llm_model
    return MarkItDown(**kwargs)


def convert_one(md, src: Path, dest: Path | None, keep_data_uris: bool):
    """Return (status, chars, error). status in {ok, empty, error}."""
    try:
        result = md.convert(str(src), keep_data_uris=keep_data_uris)
        text = result.markdown or ""
        if dest is not None:
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_text(text, encoding="utf-8")
        return ("ok" if text.strip() else "empty", len(text), "", text)
    except Exception as exc:  # noqa: BLE001 - one bad file must not stop the run
        detail = f"{type(exc).__name__}: {exc}"
        return ("error", 0, detail, "")


def main(argv=None) -> int:
    args = parse_args(argv)

    source: Path = args.source.expanduser().resolve()
    if not source.exists():
        sys.exit(f"Source not found: {source}")

    exts = {e if e.startswith(".") else "." + e for e in
            (args.ext if args.ext else DEFAULT_EXTS)}
    exts = {e.lower() for e in exts}

    files = collect_files(source, exts, args.recursive)
    if not files:
        print(f"No matching files under {source}")
        return 0

    single_mode = args.single is not None
    if single_mode:
        out_root = args.single.expanduser().resolve().parent
        single_out = args.single.expanduser().resolve()
    else:
        out_root = (args.output.expanduser().resolve() if args.output
                    else source.parent / (source.name + "_md")
                    if source.is_dir() else source.parent / (source.stem + "_md"))
        single_out = None

    # Skip files already converted, unless --overwrite
    jobs: list[tuple[Path, Path | None]] = []
    skipped = 0
    for src in files:
        dest = None if single_mode else output_path_for(src, source, out_root, args.flat)
        if dest is not None and dest.exists() and not args.overwrite:
            skipped += 1
            continue
        jobs.append((src, dest))

    if args.dry_run:
        print(f"Source     : {source}")
        print(f"Output     : {single_out if single_mode else out_root}")
        print(f"Matched    : {len(files)}   to convert: {len(jobs)}   already done: {skipped}")
        for src, dest in jobs[:200]:
            print(f"  {src}  ->  {dest if dest else '(single file)'}")
        if len(jobs) > 200:
            print(f"  ... and {len(jobs) - 200} more")
        return 0

    try:
        md = build_converter(args)
    except ImportError:
        sys.exit("markitdown is not installed: pip install 'markitdown[all]'")

    out_root.mkdir(parents=True, exist_ok=True)
    rows: list[dict] = []
    pieces: dict[Path, str] = {}
    counts = {"ok": 0, "empty": 0, "error": 0}
    total = len(jobs)

    def run(job):
        src, dest = job
        status, chars, err, text = convert_one(md, src, dest, args.keep_data_uris)
        return src, dest, status, chars, err, text

    workers = max(1, args.workers)
    if workers == 1:
        results_iter = (run(j) for j in jobs)
    else:
        pool = ThreadPoolExecutor(max_workers=workers)
        futures = [pool.submit(run, j) for j in jobs]
        results_iter = (f.result() for f in as_completed(futures))

    done = 0
    for src, dest, status, chars, err, text in results_iter:
        done += 1
        counts[status] += 1
        if single_mode and status != "error":
            pieces[src] = text
        rows.append({
            "source": str(src),
            "output": "" if dest is None else str(dest),
            "status": status,
            "chars": chars,
            "error": err,
        })
        if not args.quiet:
            mark = {"ok": "OK   ", "empty": "EMPTY", "error": "FAIL "}[status]
            try:
                shown = src.relative_to(source)
            except ValueError:
                shown = src.name
            print(f"[{done}/{total}] {mark} {shown}"
                  + (f"  <- {err}" if err else f"  ({chars} chars)"))

    if single_mode:
        single_out.parent.mkdir(parents=True, exist_ok=True)
        with single_out.open("w", encoding="utf-8") as fh:
            for src in files:
                if src not in pieces:
                    continue
                try:
                    label = src.relative_to(source)
                except ValueError:
                    label = src.name
                fh.write(f"\n\n# ---- {label} ----\n\n")
                fh.write(pieces[src])
        print(f"\nWrote {single_out}")

    manifest = (single_out.with_name(single_out.stem + "_manifest.csv")
                if single_mode else out_root / "_manifest.csv")
    with manifest.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=["source", "output", "status", "chars", "error"])
        writer.writeheader()
        writer.writerows(sorted(rows, key=lambda r: r["source"]))

    errors = [r for r in rows if r["status"] == "error"]
    if errors:
        errlog = manifest.with_name(manifest.stem.replace("_manifest", "") + "_errors.log")
        with errlog.open("w", encoding="utf-8") as fh:
            for r in errors:
                fh.write(f"{r['source']}\n    {r['error']}\n")

    print(f"\nConverted {counts['ok']} | empty {counts['empty']} | failed "
          f"{counts['error']} | skipped {skipped}")
    print(f"Output   : {single_out if single_mode else out_root}")
    print(f"Manifest : {manifest}")
    if errors:
        print(f"Errors   : {errlog}")
    return 1 if counts["error"] else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\nInterrupted", file=sys.stderr)
        sys.exit(130)
    except Exception:  # noqa: BLE001
        traceback.print_exc()
        sys.exit(1)
