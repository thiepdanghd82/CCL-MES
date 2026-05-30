# tools/ — Script Python phụ trợ cho CCL-MES

Bộ script Python kết hợp với phần backend .NET, dùng cho phân tích dữ liệu, kiểm chứng và ETL. Không bắt buộc để chạy ứng dụng — chỉ là công cụ hỗ trợ.

## Yêu cầu
- Python 3.9+
- Cài thư viện (chỉ cần cho đọc Excel): `pip install -r tools/requirements.txt`

## Các script

### 1. `verify_oee.py` — Kiểm chứng công thức OEE
Chạy bộ test đối chiếu công thức OEE với ví dụ chuẩn ngành (Vorne). Dùng để bảo vệ công thức khỏi bị sửa sai (có thể đưa vào CI).

```bash
python3 tools/verify_oee.py
```

### 2. `oee_from_csv.py` — Tính OEE từ log CSV
Đọc file log sản xuất (xuất từ máy hoặc nhập tay), tính Availability / Performance / Quality / OEE theo từng máy. Công thức khớp 100% với `OeeService` bên .NET.

```bash
python3 tools/oee_from_csv.py tools/sample_production_log.csv
python3 tools/oee_from_csv.py my_log.csv --ideal-cycle 0.4 --json
```

CSV cần cột: `machine,event,start,end,good,reject` (event = Run/Stop/Setup/Idle).

### 3. `seed_from_excel.py` — Nạp master data từ Excel/CSV vào DB
ETL đơn giản: đọc Excel/CSV chứa Customer/Product/Spec rồi UPSERT vào SQLite `ccl_mes.db` (DB do EF Core tạo). Tiện khi cần khởi tạo nhanh dữ liệu thật thay vì gõ tay.

```bash
# chạy app .NET 1 lần để EF Core tạo schema trước, rồi:
python3 tools/seed_from_excel.py tools/sample_master.csv --db src/CCL.MES.Web/ccl_mes.db
```

Cột cần có: `customer_code, customer_name, product_code, product_name, spec_code, spec_title`.

## Vì sao kết hợp Python + .NET?
- **.NET** chạy hệ thống chính (API, nghiệp vụ, UI).
- **Python** mạnh cho phân tích nhanh, kiểm chứng số, ETL, đọc Excel — không phải dựng project .NET riêng cho mấy việc lặt vặt này.
- Khi sang SQL Server, chỉ cần đổi phần kết nối trong `seed_from_excel.py` từ `sqlite3` sang `pyodbc`.
