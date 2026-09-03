# P12 — nhật ký migration lên DB thật

> **Vì sao có file này.** Nguyên tắc IV của hiến pháp (NON-NEGOTIABLE) đòi mọi
> thay đổi schema trên live DB đi theo **Phase A→B→C** và gọi đó là STOP-gate.
> Ba migration của P12 đã được áp lên `data/ccl_mes.db` ngày **2026-08-28**,
> nhưng `plan.md` vẫn ghi "⚠ MỞ" và bằng chứng Phase A khi đó nằm ở `/tmp` —
> tới 2026-09-03 thì `/tmp` đã bị dọn, **không còn file backup nào**.
>
> Hồ sơ sai trên một điều khoản bằng chứng còn nguy hiểm hơn hồ sơ thiếu: người
> đọc `plan.md` sẽ tưởng gate chưa qua trong khi nó đã qua rồi. File này ghi lại
> đúng thứ đã xảy ra, và từ đây trở đi là nơi ghi tiếp.

---

## 1. Ba migration đã áp

| Thứ tự | MigrationId | Nội dung |
|---|---|---|
| 1 | `20260828091742_AddIqcCheckStandardLibrary` | 3 bảng mới: `IqcCheckItemLibraries` · `IqcMaterialSpecs` · `IqcSpecItems` + unique `(SpecNo, ItemId, Seq)` |
| 2 | `20260828095900_AddIqcDefaultMatrixColumns` | `InDefaultMatrix` + `DefaultAcceptanceVi/En` + `DefaultMethodVi/En` trên `IqcCheckItemLibraries` |
| 3 | `20260828100725_AddIqcResultDetailFrozenColumns` | `Pass` → **nullable** + 14 cột đóng băng trên `IqcResultDetails` |

Xác nhận trên DB thật:

```
sqlite3 data/ccl_mes.db "SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '202608280%';"
20260828091742_AddIqcCheckStandardLibrary
20260828095900_AddIqcDefaultMatrixColumns
20260828100725_AddIqcResultDetailFrozenColumns
```

Cả ba đã **strip type-affinity** (`type: "TEXT|INTEGER|REAL"` và `.HasColumnType(...)`)
theo §4.5 — grep 2026-09-03 cho 0 hit trên cả ba file `.cs` (`Designer.cs` không tính).

---

## 2. Phase A→B→C của migration thứ 3 (có bằng chứng đầy đủ)

Migration `AddIqcResultDetailFrozenColumns` là cái rủi ro nhất trong ba cái: SQLite
không `ALTER COLUMN` được, nên đổi `Pass` sang nullable buộc EF **dựng lại bảng**
(`PRAGMA foreign_keys = 0` + copy + rename). EF cảnh báo rõ thao tác này không chạy
được trong transaction — hỏng giữa chừng là bảng ở trạng thái nửa vời.

**Phase A — chốt mốc gốc**

```
backup   : /tmp/ccl_mes.db.before-p12-frozen.20260828T171445Z
sha256 live   = 5355dca7758c04978c43fbc33ad6f3986061719a0981ae5f67169938a832a756
sha256 backup = 5355dca7758c04978c43fbc33ad6f3986061719a0981ae5f67169938a832a756   ← byte-identical
rowcount : IqcInspections=25 · IqcResultDetails=7 · RawMaterials=2967 · WorkOrders=27
migration cuối trước khi áp: 20260828095900_AddIqcDefaultMatrixColumns
```

**Phase B — thử trên BẢN SAO CỦA DB THẬT, không phải DB rỗng**

Lần đầu chạy nhầm: `rm -f /tmp/...db*` với glob không khớp làm zsh huỷ cả chuỗi
lệnh, nên `cp` không chạy và EF dựng một DB rỗng rồi áp lên đó — vô nghĩa. Chạy lại
đúng trên bản sao:

```
/tmp/p12-phaseB.db ← cp data/ccl_mes.db   (2967 RawMaterials)
rowcount SAU migration : IqcInspections=25 · IqcResultDetails=7 · RawMaterials=2967 · WorkOrders=27
Pass notnull = 0        ← đã nullable
cột mới      = 15/15
7 dòng cũ    : Pass = 1,1,1,1,1,0,1  ← KHÔNG dòng nào thành NULL
integrity_check = ok
foreign_key_check: 3 vi phạm  (WoQcCheckItems → WoQcChecks)
```

Ba vi phạm FK đó **có sẵn từ trước**, đã đối chiếu trên chính bản backup Phase A:
trước migration 3, sau migration 3 — cùng 3, cùng một bảng. `IqcResultDetails`
(bảng migration đụng vào) có **0** vi phạm. Đây là nợ cũ của bề mặt 7e, không phải
do P12 — xem §4.

**Phase C — áp thật**

```
Applying migration '20260828100725_AddIqcResultDetailFrozenColumns'.
Done.

rowcount : IqcInspections=25 · IqcResultDetails=7 · RawMaterials=2967 · WorkOrders=27   ← khớp Phase A
Pass notnull = 0 · cột mới = 15/15
integrity_check = ok · FK vi phạm = 3 (bằng trước)
7 dòng cũ giữ nguyên giá trị Pass
```

Rowcount trước = sau trên cả bốn bảng. Không dòng nào mất, không giá trị nào đổi.

**Migration 1 và 2** được áp trong phiên làm việc trước đó cùng ngày; bằng chứng
Phase A của chúng nằm ở `/tmp` và đã mất. Chúng đều là **thêm bảng / thêm cột
nullable** — không dựng lại bảng, không đụng dữ liệu cũ — nên rủi ro thấp hơn hẳn
migration 3. Trạng thái hiện tại của DB (§3) là bằng chứng thay thế: không bảng nào
mất, `__EFMigrationsHistory` liên tục, `integrity_check` sạch.

---

## 3. Mốc gốc mới (2026-09-03) — thay cho bằng chứng đã mất

Vì backup Phase A không còn, đã chụp một bản gốc mới vào **thư mục lưu bền của
app**, không phải `/tmp`:

```
data/Backup/SQLite/ccl_mes.db.p12-post-migration.20260903-134522   (19 MB)
sha256 backup = a12cfc1d020f71bb4d4a45474b20cddb8a372727abea2910b435c8f43a305296
sha256 live   = a12cfc1d020f71bb4d4a45474b20cddb8a372727abea2910b435c8f43a305296

IqcInspections = 26 · IqcResultDetails = 20 · RawMaterials = 2967 · WorkOrders = 27
IqcCheckItemLibraries = 21 · IqcMaterialSpecs = 459 · IqcSpecItems = 5961
integrity_check = ok · foreign_key_check = 3 vi phạm (nợ cũ, §4)
```

> Chênh so với Phase A (25 phiếu / 7 dòng chi tiết) là **một phiếu thật do Henry tạo
> ngày 28/08** (`IQC-260828-0001`, mã 30030146) mang 13 hạng mục đóng băng — đúng
> hành vi mới, không phải dữ liệu rác. Mọi phiếu thử của quá trình verify đã được
> dọn và rowcount đã trả về mốc trước khi thử.

---

## 4. Nợ cũ ghi nhận, KHÔNG do P12

`PRAGMA foreign_key_check` trả **3 vi phạm** trên `WoQcCheckItems → WoQcChecks`.
Đã đo trên bản backup **trước** migration: cũng đúng 3, cùng bảng. Đây là di sản
của bề mặt 7e (FQC/OQC), cần Ops quyết định dọn hay bỏ qua. Ghi ở đây để lần sau
ai chạy `foreign_key_check` thấy con số 3 thì biết nó không phải mới.

---

## 5. Luật rút ra

**Backup Phase A không được để ở `/tmp`.** macOS dọn `/tmp`; bằng chứng thì phải
sống lâu hơn phiên làm việc. Từ nay backup Phase A đi vào
`data/Backup/SQLite/` (đã `.gitignore`, nên không nặng repo nhưng vẫn nằm cạnh DB),
và **số liệu** (sha256 + rowcount trước/sau) chép vào file nhật ký này — file nhật
ký được commit, backup thì không.

Đã ghi thành lesson card trong `LESSONS-LEARNED.md`.
