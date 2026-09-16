# Python API

```python
from markitdown import MarkItDown

md = MarkItDown()                 # built-ins on, plugins off
result = md.convert("test.xlsx")
print(result.markdown)
```

## Constructor

```python
MarkItDown(
    *,
    enable_builtins: bool | None = None,   # default True
    enable_plugins: bool | None = None,    # default False
    **kwargs,
)
```

Recognised `kwargs` (forwarded to converters and to plugins'
`register_converters()`):

| kwarg | Purpose |
|---|---|
| `llm_client` | OpenAI-compatible client for image descriptions / OCR |
| `llm_model` | e.g. `"gpt-4o"` — must support vision |
| `llm_prompt` | Custom prompt for the vision call |
| `exiftool_path` | Explicit path to the exiftool binary |
| `style_map` | mammoth style map for DOCX → HTML mapping |
| `requests_session` | Reuse your own `requests.Session` (proxies, auth, retries) |
| `docintel_endpoint` | Azure Document Intelligence endpoint |
| `cu_endpoint`, `cu_analyzer_id`, `cu_file_types` | Azure Content Understanding |

`enable_builtins(**kwargs)` / `enable_plugins(**kwargs)` let you turn each set
on later if you constructed with them disabled — call each at most once.

## Conversion methods

| Method | Accepts |
|---|---|
| `convert(source, *, stream_info=None, **kw)` | Dispatcher: `str` path, `str` URI (`http:`/`https:`/`file:`/`data:`), `Path`, `requests.Response`, or binary stream |
| `convert_local(path, *, stream_info=None, **kw)` | Filesystem path |
| `convert_stream(stream, *, stream_info=None, **kw)` | Any `BinaryIO` (must be binary, not text) |
| `convert_uri(uri, *, stream_info=None, mock_url=None, **kw)` | `http:`, `https:`, `file:`, `data:` |
| `convert_response(response, *, stream_info=None, **kw)` | A `requests.Response` you already fetched |

**Security:** call the narrowest method for the job. `convert()` will happily
follow a URL if the string looks like one — a real risk when the string comes
from user input.

## Result object

```python
result.markdown        # the Markdown text
result.text_content    # soft-deprecated alias of .markdown
result.title           # Optional[str]
str(result)            # same as .markdown
```

## StreamInfo — telling MarkItDown what it is looking at

```python
from markitdown import MarkItDown, StreamInfo

md = MarkItDown()
with open("no_extension_file", "rb") as f:
    result = md.convert_stream(
        f,
        stream_info=StreamInfo(
            extension=".pdf",
            mimetype="application/pdf",
            charset="utf-8",
            filename="quote.pdf",
            url="https://supplier.example/quote.pdf",   # for context only
        ),
    )
```

`StreamInfo` is a frozen dataclass with all-optional fields: `mimetype`,
`extension`, `charset`, `filename`, `local_path`, `url`. Use
`info.copy_and_update(other_info, **overrides)` to layer hints. When hints are
absent, MarkItDown guesses the type with **magika** plus the filename, and
tries each candidate converter in priority order.

## Exceptions

```python
from markitdown import (
    MarkItDownException,        # base class
    MissingDependencyException, # optional extra not installed
    UnsupportedFormatException, # no converter accepted the stream
    FileConversionException,    # every accepting converter failed
    FailedConversionAttempt,    # one attempt's detail, carried by the above
)
```

```python
try:
    result = md.convert(path)
except MissingDependencyException as e:
    print(f"pip install the right extra: {e}")
except UnsupportedFormatException:
    print("no converter handles this file type")
except FileConversionException as e:
    for attempt in e.attempts:          # FailedConversionAttempt objects
        print(attempt.converter, attempt.exc_info)
```

## Custom converters and priority

```python
from markitdown import (
    MarkItDown, DocumentConverter, DocumentConverterResult, StreamInfo,
    PRIORITY_SPECIFIC_FILE_FORMAT,   # == 0.0
    PRIORITY_GENERIC_FILE_FORMAT,    # == 10.0
)

class RtfConverter(DocumentConverter):
    def accepts(self, file_stream, stream_info, **kwargs) -> bool:
        return (stream_info.extension or "").lower() == ".rtf"

    def convert(self, file_stream, stream_info, **kwargs) -> DocumentConverterResult:
        text = file_stream.read().decode("utf-8", errors="replace")
        return DocumentConverterResult(markdown=strip_rtf(text), title=None)

md = MarkItDown()
md.register_converter(RtfConverter(), priority=PRIORITY_SPECIFIC_FILE_FORMAT)
```

Rules:

- **Lower priority value = tried earlier.** Most converters sit at `0.0`.
  `PlainTextConverter`, `HtmlConverter` and `ZipConverter` sit at `10.0` so they
  act as fallbacks.
- Sorting is stable, and registration inserts at the front, so among equal
  priorities the **most recently registered wins**.
- Register at a negative priority (e.g. `-1.0`) to override a built-in — this is
  exactly how `markitdown-ocr` replaces the PDF/DOCX/PPTX/XLSX converters.
- If `accepts()` reads from the stream to decide, it **must** restore the
  position with `seek()` before returning — `convert()` may be called
  immediately afterwards and expects the original offset.

## Writing a plugin

A plugin is a normal Python package exposing a `markitdown.plugin` entry point:

```toml
# pyproject.toml
[project.entry-points."markitdown.plugin"]
my_plugin = "my_markitdown_plugin"
```

```python
# my_markitdown_plugin/__init__.py
__plugin_interface_version__ = 1

def register_converters(markitdown, **kwargs):
    """Called by MarkItDown when enable_plugins=True. kwargs carries
    llm_client, llm_model, and everything else passed to MarkItDown()."""
    markitdown.register_converter(MyConverter(**kwargs))
```

`packages/markitdown-sample-plugin` in the upstream repo is a complete working
template (an RTF converter with tests). Tag your repo `#markitdown-plugin` on
GitHub so others can find it.
