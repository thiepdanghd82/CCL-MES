# CCL-MES — hướng dẫn agent (Cursor)

Repo MES nhà máy CCL Design Vietnam. Skill/agent/lesson lấy từ Claude **không fork nội dung** — Cursor chỉ trỏ vào cùng file.

1. Đọc `.cursor/rules/` (luôn bật) rồi skill `cmes-loop`.
2. Chỉ nạp thêm skill của đúng một work-class (bảng trong rule `cmes-loop`).
3. Không đụng `src/CCL.MES.Web`. Schema đi `Domain` / `Application` / `Infrastructure`.
4. API nhà máy: `CCL-MES-Hybrid/src/CCL.MES.Api` cổng 5100. Live DB: `data/ccl_mes.db`.
5. Trước khi nói xong: `bash CCL-MES-Hybrid/scripts/gate-all.sh` + output chạy thật.

Chi tiết: `CLAUDE.md` §0, `CCL-MES-Hybrid/docs/AGENT-LOOP.md`.
