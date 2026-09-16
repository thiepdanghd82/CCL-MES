# Command-line reference

```
markitdown [-h] [-v] [-o OUTPUT] [-x EXTENSION] [-m MIME_TYPE] [-c CHARSET]
           [-d | --use-cu] [-e ENDPOINT] [--cu-endpoint URL]
           [--cu-analyzer ID] [--cu-file-types LIST]
           [-p] [--list-plugins] [--keep-data-uris] [filename]
```

## Input and output

| Form | Command |
|---|---|
| File → stdout | `markitdown example.pdf` |
| File → file | `markitdown example.pdf -o example.md` |
| File → file (shell) | `markitdown example.pdf > example.md` |
| stdin → stdout | `cat example.pdf \| markitdown -x .pdf` |
| URL → stdout | `markitdown https://example.com/page.html` |

`filename` is optional; when omitted, input is read from stdin. Because stdin
carries no filename, give a hint with `-x`, `-m`, or `-c` whenever the content
type is not obvious to magika.

## Flags

| Flag | Meaning |
|---|---|
| `-v`, `--version` | Print version and exit |
| `-o`, `--output FILE` | Write to FILE instead of stdout |
| `-x`, `--extension EXT` | Hint the file extension, e.g. `-x .pdf` |
| `-m`, `--mime-type TYPE` | Hint the MIME type, e.g. `-m application/pdf` |
| `-c`, `--charset CS` | Hint the charset, e.g. `-c UTF-8` |
| `-p`, `--use-plugins` | Enable installed 3rd-party plugins (off by default) |
| `--list-plugins` | List installed plugins and exit |
| `--keep-data-uris` | Keep base64 `data:` URIs (images) instead of truncating |
| `-d`, `--use-docintel` | Use Azure Document Intelligence; needs `-e` |
| `-e`, `--endpoint URL` | Document Intelligence endpoint |
| `--use-cu`, `--use-content-understanding` | Use Azure Content Understanding; needs `--cu-endpoint` |
| `--cu-endpoint URL` | Content Understanding endpoint |
| `--cu-analyzer ID` | Custom CU analyzer ID (default: auto-select by file type) |
| `--cu-file-types LIST` | Comma-separated types routed to CU, e.g. `pdf,jpeg,mp4` |

`-d/--use-docintel` and `--use-cu` are mutually exclusive.

## Useful shell patterns

```bash
# Convert every PDF in a folder, keeping names
for f in *.pdf; do markitdown "$f" -o "${f%.pdf}.md"; done

# Recursive, structure-preserving — prefer scripts/md_batch.py, but in pure shell:
find . -name '*.docx' -print0 | while IFS= read -r -d '' f; do
  markitdown "$f" -o "${f%.docx}.md"
done

# Word count of extracted text
markitdown spec.pdf | wc -w

# Grep across a pile of documents without converting to disk
for f in *.pdf; do echo "== $f"; markitdown "$f" | grep -i 'tolerance'; done
```

## Behaviour notes

- Output is always UTF-8 Markdown on stdout unless `-o` is given.
- Conversion failures raise and exit non-zero; the message names the format and
  the converter that was attempted.
- `--list-plugins` shows plugin names only; plugins stay inactive until `-p`.
