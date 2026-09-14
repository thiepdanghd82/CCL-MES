# Diagrams — sinh bằng Diagrify

Mỗi sơ đồ là một cặp file: `<tên>.<loại>.json` (nguồn, chỉnh ở đây) và `<tên>.html` (sản phẩm tự chứa, mở bằng trình duyệt, nhấn `?` để xem hướng dẫn).

Skill: `tools/diagrify` (link vào `.claude/skills/diagrify` bằng `sh tools/diagrify/install.sh`).

| Sơ đồ | Loại | Nguồn sự thật |
|---|---|---|
| `wo-state-machine` | lifecycle | `docs/P10.7-WO-STATE-CONTRACT.md` §3.1–3.4 (13 MesPhase, 7e-1) |

Tái tạo sau khi sửa JSON:

```bash
node tools/diagrify/bin/diagrify.mjs deliver lifecycle CCL-MES-Hybrid/docs/diagrams/wo-state-machine.lifecycle.json CCL-MES-Hybrid/docs/diagrams/wo-state-machine.html
```

Trong Claude Code, chỉ cần nói: `Dùng diagrify vẽ …` — agent tự đọc `SKILL.md`, soạn JSON, validate và deliver vào thư mục này.
