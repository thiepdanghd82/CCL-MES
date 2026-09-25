# Runbook — trỏ app CCL MES trên máy xưởng về server

App đóng gói sẵn địa chỉ `http://127.0.0.1:5100` (server trên chính máy đó).
Máy xưởng nói chuyện với server CHUNG thì đặt địa chỉ theo **một trong hai**
cách dưới — **không cần build lại app**. Thứ tự đè: đóng gói → file máy →
biến môi trường. Mã: `CCL-MES-Hybrid/src/CCL.MES.Hybrid.Client/StationConfig.cs`.

## Cách 1 — file cấp máy (khuyến nghị)

Nội dung file `appsettings.local.json` (thay IP bằng IP server thật):

```json
{ "CclApi": { "BaseUrl": "http://10.102.3.87:5100" } }
```

| Máy | Đường dẫn file |
| --- | --- |
| Mac | `/Library/Application Support/CCL MES/appsettings.local.json` |
| Windows | `C:\ProgramData\CCL MES\appsettings.local.json` |

Cả hai thư mục **cần quyền quản trị** để ghi — cố ý: người dùng thường không
được trỏ app sang server lạ (server lạ nhận được mật khẩu của mọi người đăng
nhập trên máy đó).

Mac:

```bash
sudo mkdir -p "/Library/Application Support/CCL MES"
echo '{ "CclApi": { "BaseUrl": "http://10.102.3.87:5100" } }' | sudo tee "/Library/Application Support/CCL MES/appsettings.local.json"
```

Windows (PowerShell chạy Administrator):

```powershell
New-Item -ItemType Directory -Force "C:\ProgramData\CCL MES" | Out-Null
'{ "CclApi": { "BaseUrl": "http://10.102.3.87:5100" } }' | Set-Content -Encoding UTF8 "C:\ProgramData\CCL MES\appsettings.local.json"
```

## Cách 2 — biến môi trường (tiện cho IT đẩy qua chính sách Windows)

```
CCL_MES_CclApi__BaseUrl = http://10.102.3.87:5100
```

Đè lên cả file. Trên Mac, app mở từ Finder **không** nhận biến môi trường của
shell — dùng Cách 1.

## Kiểm tra máy đang dùng server nào

Mỗi lần khởi động app in một dòng (cả bản Release):

```
[boot] CclApi:BaseUrl => http://10.102.3.87:5100 (source: file /Library/Application Support/CCL MES/appsettings.local.json)
```

Gõ sai (thiếu `http://`, sai định dạng) ⇒ app **không sập**, rơi về địa chỉ đóng
gói và in:

```
[boot] ERROR CclApi:BaseUrl override invalid (...: not an absolute URL) — using bundled value.
```

## Lưu ý

- Mac: lần đầu gọi sang máy khác trong LAN, macOS hỏi quyền **Mạng cục bộ** — bấm
  Cho phép (lý do hiện trong hộp thoại lấy từ `NSLocalNetworkUsageDescription`).
- Đã đo 2026-09-25: ATS hiện tại (`NSAllowsLocalNetworking`) cho phép HTTP tới IP
  LAN `10.x` trên macOS máy dev. Hướng lâu dài vẫn là HTTPS (skill `cmes-macos-ship`).
- Server phải đang nghe trên LAN (`0.0.0.0:5100`), không chỉ `localhost` — xem bước 4
  của kế hoạch mở LAN; chỉ mở sau khi đã đổi hết mật khẩu mặc định.
