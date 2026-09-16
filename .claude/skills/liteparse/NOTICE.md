# Attribution

This skill documents **LiteParse**, © LlamaIndex / run-llama contributors,
released under the Apache License 2.0:
<https://github.com/run-llama/liteparse>

The reference material is derived from that repository at **v2.14.2**
(commit `e233fa1`, 27 Aug 2026) — its README files, `OCR_API_SPEC.md`, and the
Python binding source — plus the companion server repo
<https://github.com/run-llama/liteparse-server>. The LiteParse software itself
is **not** bundled here; install it from PyPI, npm or crates.io as described in
`references/install.md`.

The helper scripts in `scripts/` (`lp_batch.py`, `lp_check.py`) are original
work written for this skill.

An official upstream agent skill also exists and is a good lighter-weight
alternative:

```bash
npx skills add run-llama/llamaparse-agent-skills --skill liteparse
```

LiteParse builds on PDFium (Google), Tesseract, napi-rs, PyO3 and wasm-bindgen.
LlamaIndex, LlamaParse and Azure are trademarks of their respective owners;
their use here is descriptive.
