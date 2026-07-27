#!/usr/bin/env bash
# UserPromptSubmit hook — the "showcard loop". When a prompt asks to build a
# showcard / detail dialog / inspector window, remind (as added context) to
# follow the L34 workflow BEFORE coding. Always exits 0 (never blocks a prompt);
# prints the reminder to stdout ONLY when the prompt matches — otherwise silent.
python3 -c "
import sys, json, re
raw = sys.stdin.read()
try:
    prompt = json.loads(raw).get('prompt', '')
except Exception:
    prompt = raw
pat = r'showcard|detail dialog|cửa sổ chi tiết|popup chi tiết|modal chi tiết|inspector window'
if re.search(pat, prompt, re.IGNORECASE):
    print('[L34 showcard loop] Yêu cầu liên quan showcard/detail-dialog/inspector. BẮT BUỘC trước khi code: '
          '(1) ĐỌC .claude/skills/cmes-floating-showcard/SKILL.md; '
          '(2) CẬP NHẬT SKILL.md nếu lộ pattern/lỗ hổng mới; '
          '(3) BỌC body trong <FloatingWindow> (KHÔNG tự vẽ chrome/role=\"dialog\") + parent giữ IFloatingWindowStore, '
          'rồi chạy scripts/gate-floating-showcard.sh (PASS mới xong). '
          'Surface transactional (form/confirm) thì dùng <Modal> căn giữa, KHÔNG ép Float=true.')
"
