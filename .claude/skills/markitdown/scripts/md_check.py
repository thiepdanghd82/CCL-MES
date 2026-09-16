#!/usr/bin/env python3
"""Check that MarkItDown is installed and report which formats are usable.

    python3 md_check.py            # summary
    python3 md_check.py --verbose  # + import errors and paths

Exit code 0 if markitdown imports, 1 otherwise.
"""

from __future__ import annotations

import argparse
import importlib
import os
import shutil
import sys

# (label, extra to install, modules that must import)
FEATURES = [
    ("PDF",                    "pdf",                      ["pdfminer", "pdfplumber"]),
    ("Word .docx",             "docx",                     ["mammoth", "lxml"]),
    ("Excel .xlsx",            "xlsx",                     ["pandas", "openpyxl"]),
    ("Excel .xls (legacy)",    "xls",                      ["pandas", "xlrd"]),
    ("PowerPoint .pptx",       "pptx",                     ["pptx"]),
    ("Outlook .msg",           "outlook",                  ["olefile"]),
    ("Audio transcription",    "audio-transcription",      ["pydub", "speech_recognition"]),
    ("YouTube transcripts",    "youtube-transcription",    ["youtube_transcript_api"]),
    ("Azure Doc Intelligence", "az-doc-intel",             ["azure.ai.documentintelligence"]),
    ("Azure Content Underst.", "az-content-understanding", ["azure.ai.contentunderstanding"]),
]

ALWAYS = ["bs4", "requests", "markdownify", "magika", "charset_normalizer", "defusedxml"]


def can_import(name: str) -> tuple[bool, str]:
    try:
        importlib.import_module(name)
        return True, ""
    except Exception as exc:  # noqa: BLE001
        return False, f"{type(exc).__name__}: {exc}"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    print(f"Python      : {sys.version.split()[0]}  ({sys.executable})")
    if sys.version_info < (3, 10):
        print("  !! MarkItDown requires Python 3.10 or higher.")

    try:
        import markitdown
        print(f"markitdown  : {markitdown.__version__}")
    except ImportError as exc:
        print(f"markitdown  : NOT INSTALLED  ({exc})")
        print("\n  pip install 'markitdown[all]'")
        return 1

    cli = shutil.which("markitdown")
    print(f"CLI on PATH : {cli or 'no (use: python3 -m markitdown)'}")

    missing_core = [m for m in ALWAYS if not can_import(m)[0]]
    print(f"Core deps   : {'all present' if not missing_core else 'MISSING ' + ', '.join(missing_core)}")

    exiftool = os.getenv("EXIFTOOL_PATH") or shutil.which("exiftool")
    print(f"exiftool    : {exiftool or 'not found (image/audio metadata will be skipped)'}")

    print("\nFormat support")
    print("-" * 58)
    missing_extras: list[str] = []
    for label, extra, modules in FEATURES:
        results = [(m, *can_import(m)) for m in modules]
        ok = all(r[1] for r in results)
        print(f"  {'OK ' if ok else '-- '} {label:<24} {'' if ok else f'pip install markitdown[{extra}]'}")
        if not ok:
            missing_extras.append(extra)
            if args.verbose:
                for m, good, err in results:
                    if not good:
                        print(f"        {m}: {err}")

    print("\nAlways available: HTML, CSV, JSON, XML, plain text, EPub, "
          "Jupyter .ipynb, ZIP, images (EXIF), Wikipedia/RSS/Bing URLs")

    try:
        from importlib.metadata import entry_points
        plugins = list(entry_points(group="markitdown.plugin"))
    except Exception:  # noqa: BLE001
        plugins = []
    print(f"\nPlugins     : {', '.join(p.name for p in plugins) if plugins else 'none installed'}"
          + ("" if plugins else "   (OCR: pip install markitdown-ocr)"))
    if plugins:
        print("              enable with --use-plugins / MarkItDown(enable_plugins=True)")

    has_openai = can_import("openai")[0]
    print(f"openai pkg  : {'present' if has_openai else 'absent (needed for image captions / OCR plugin)'}")
    if has_openai:
        print(f"OPENAI_API_KEY: {'set' if os.getenv('OPENAI_API_KEY') else 'not set'}")

    if missing_extras:
        print(f"\nInstall everything missing:\n  pip install 'markitdown[{','.join(sorted(set(missing_extras)))}]'")
    else:
        print("\nAll optional formats available.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
