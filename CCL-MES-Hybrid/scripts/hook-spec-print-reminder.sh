#!/usr/bin/env bash
# UserPromptSubmit hook — the "spec-print loop". When a prompt asks to work on
# printing / PDF export / @media print / the Spec sheet layout, remind (as added
# context) to follow the L39 WYSIWYG workflow BEFORE coding. Always exits 0
# (never blocks a prompt); prints ONLY when the prompt matches — else silent.
python3 -c "
import sys, json, re
raw = sys.stdin.read()
try:
    prompt = json.loads(raw).get('prompt', '')
except Exception:
    prompt = raw
pat = (r'in pdf|print pdf|xuất pdf|@media print|window\.print|migradoc|wysiwyg|'
       r'tờ spec|spec sheet|print process|print-css|print css|bản in|hộp in|'
       r'in tờ|xuất tờ|pdf tờ')
if re.search(pat, prompt, re.IGNORECASE):
    print('[L39 spec-print loop] Yêu cầu liên quan in/xuất PDF tờ Spec. BẮT BUỘC trước khi code: '
          '(1) ĐỌC .claude/skills/cmes-spec-print/SKILL.md; '
          '(2) In trên maccatalyst = native IPrintService (UIPrintInteractionController + '
          'WKWebView.ViewPrintFormatter), KHÔNG window.print(); '
          '(3) print-CSS trong GLOBAL wwwroot/css/app.css (scoped .razor.css chết trên maccatalyst); '
          '(4) bảng rộng = table-layout:auto + white-space:nowrap + 1 token --spec-print-table-fs '
          '(mỗi hàng 1 dòng, font đều), KHÔNG fixed+wrap; on-screen == bản in (WYSIWYG); '
          '(5) MigraDoc = fallback A4 landscape đủ-cột + auto-fit PageCount ≤2 + hairline 0.25. '
          'Chạy scripts/gate-spec-print.sh (PASS mới xong); cập nhật SKILL.md nếu lộ pattern mới.')
"
