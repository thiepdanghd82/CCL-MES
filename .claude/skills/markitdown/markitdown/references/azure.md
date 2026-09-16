# Azure backends

Two optional cloud backends, both billable per call. Use them when the offline
converters are not enough — scanned documents, complex layouts, video, or
structured field extraction.

| Capability | Built-in converters | Document Intelligence | Content Understanding |
|---|---|---|---|
| Document conversion | Offline, format-specific | Cloud layout extraction | Cloud multimodal extraction |
| Structured fields | Not available | Not exposed by this integration | YAML front matter from analyzer fields |
| Custom analyzers | Not available | Not configurable here | `cu_analyzer_id` |
| Audio / video | Basic audio only, no video | Not supported | Audio and video analyzers |
| Cost | Local compute only | Billable Azure API calls | Billable Azure API calls |

## Azure Document Intelligence

```bash
pip install 'markitdown[az-doc-intel]'
markitdown path-to-file.pdf -o document.md -d -e "<document_intelligence_endpoint>"
```

```python
from markitdown import MarkItDown
md = MarkItDown(docintel_endpoint="<document_intelligence_endpoint>")
print(md.convert("test.pdf").markdown)
```

Good for scanned PDFs and complex page layouts where pdfplumber's reading order
breaks down. Setting up the resource:
<https://learn.microsoft.com/azure/ai-services/document-intelligence/how-to-guides/create-document-intelligence-resource>

## Azure Content Understanding

```bash
pip install 'markitdown[az-content-understanding]'
markitdown path-to-file.pdf --use-cu --cu-endpoint "<content_understanding_endpoint>"
```

Zero-config: the analyzer is auto-selected per file type.

```python
from markitdown import MarkItDown

md = MarkItDown(cu_endpoint="<content_understanding_endpoint>")
md.convert("report.pdf")    # documents → prebuilt-documentSearch
md.convert("meeting.mp4")   # video     → prebuilt-videoSearch
md.convert("call.wav")      # audio     → prebuilt-audioSearch
```

### Custom analyzer — structured field extraction

```python
md = MarkItDown(
    cu_endpoint="<content_understanding_endpoint>",
    cu_analyzer_id="my-invoice-analyzer",
)
print(md.convert("invoice.pdf").markdown)
```

Output carries the extracted fields as YAML front matter:

```markdown
---
contentType: document
fields:
  VendorName: CONTOSO LTD.
  InvoiceDate: '2019-11-15'
---
<!-- page 1 -->
...
```

When `cu_analyzer_id` is set, it is automatically scoped to file types
compatible with the analyzer's modality; incompatible types (audio through a
document analyzer, say) fall back to the default prebuilt analyzers.

### Controlling cost

Every `convert()` on a CU-routed format is a billable call. Restrict which
formats go to the cloud:

```python
from markitdown import MarkItDown
from markitdown.converters import ContentUnderstandingFileType

md = MarkItDown(
    cu_endpoint="<content_understanding_endpoint>",
    cu_file_types=[ContentUnderstandingFileType.PDF],   # only PDFs use CU
)
```

CLI equivalent: `--cu-file-types pdf,jpeg,mp4`.

`-d/--use-docintel` and `--use-cu` are mutually exclusive on the CLI.
Docs: <https://learn.microsoft.com/azure/ai-services/content-understanding/>

### When to reach for Content Understanding

- **Video** — the only option; nothing else in MarkItDown handles it.
- **Audio at quality** — better than the local SpeechRecognition path.
- **Field extraction** — invoice totals, receipt dates, contract clauses, via
  prebuilt or custom analyzers.
- **Hard documents** — scanned PDFs, complex tables, long multi-page files.
- **One API for every modality** — a single endpoint routes documents, images,
  audio and video.
