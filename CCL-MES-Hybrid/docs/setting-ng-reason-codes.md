# SETTING NG reason codes — Ops confirm sheet

> Chuẩn bị cho Q5 của `setting-checks-persist-scope-proposal.md`. Khi NG một
> hạng mục setting, operator chọn 1 mã lỗi (chống free-text, L17). Đề xuất
> tái dùng `ReasonCodeKind.Scrap` (KHÔNG sinh kind mới) + bổ sung bộ `SET-*`
> dưới đây. **Ops xác nhận / sửa danh mục này trước khi seed.**
>
> Mã trùng cột `DefectCode` trong `setting-library-seed.csv` — mỗi hạng mục
> có 1 mã lỗi mặc định, nhưng operator vẫn chọn được mã khác trong bộ cùng
> process khi cần.

## Print process (makeready máy in)

| Code | VI | EN |
|---|---|---|
| SET-PLATE-WRONG | Bản in / khuôn sai mã hoặc phiên bản | Wrong plate/die code or revision |
| SET-SUBSTRATE-WRONG | Vật tư in sai loại / khổ / mặt xử lý | Wrong substrate type / width / treatment |
| SET-INK-COLOR | Màu / mực không đạt (ΔE, độ nhớt, pH) | Ink/colour out of spec (ΔE, viscosity, pH) |
| SET-REGISTER | Chồng màu lệch ngoài dung sai | Registration out of tolerance |
| SET-ANILOX | Anilox / cấp mực sai thông số | Anilox / ink feed wrong spec |
| SET-IMPRESSION | Áp lực in / dao gạt sai (lem, thiếu nét) | Impression / doctor blade wrong (smear, missing detail) |
| SET-CURING | Sấy / UV không đạt (mực chưa khô-bám) | Drying / UV curing insufficient |
| SET-FIRSTOFF | Mẫu đầu không đạt (nội dung / barcode / màu) | First article fail (content / barcode / colour) |
| SET-BACKREG | Đăng ký mặt sau lệch | Back-side registration off |

## Cut / die-cut process

| Code | VI | EN |
|---|---|---|
| SET-DIE-WRONG | Khuôn cắt / dao sai mã hoặc mẻ lưỡi | Wrong cutting die / knife or nicked blade |
| SET-DIE-SIZE | Lắp dao sai khổ / sai kiểu cắt | Die wrong size / wrong cut type |
| SET-CREASE | Nhấn / gấp sai vị trí hoặc độ sâu | Crease / perf wrong position or depth |
| SET-CUT-DEPTH | Áp lực / độ sâu cắt sai (đứt liner / cắt không đều) | Cut pressure/depth wrong (severed liner / uneven) |
| SET-DIE-REGISTER | Canh cắt lệch so với hình in | Die-to-print registration off |
| SET-UPLAYOUT | Layout con / tờ hoặc gap/pitch sai | Up layout or gap/pitch wrong |
| SET-REWIND | Hướng cuộn / lõi / tension sai | Wrong rewind direction / core / tension |
| SET-MATRIX | Bóc lưới thải lỗi (đứt / dính / rách biên) | Matrix stripping fault (break / stick / torn edge) |
| SET-COUNT | Đếm số lượng con/nhãn sai | Ups/label count wrong |

## Dùng chung

| Code | VI | EN |
|---|---|---|
| SET-HOUSEKEEP | Vệ sinh / an toàn khu vực chưa đạt | Housekeeping / area safety not met |

## Ghi chú kỹ thuật (cho người code 7g-1)

- Seed qua `DbSeeder` **idempotent + non-deleting (DR-1)**, `ReasonCodeKind.Scrap`.
- Nếu chọn Q3 = library-promote: `CheckItemLibrary` **hiện KHÔNG có cột stage
  `Setting`** (chỉ `Ipqc/Fqc/Oqc`, cột P/Q/R). Cần thêm 1 cột bool `Setting`
  (hoặc cột `Stage`) → đây là migration, nằm trong STOP-gate — ghi rõ ở scope Q3.
- 20 hạng mục + mã lỗi ở đây khớp 1-1 với `_printItems`/`_cutItems` đã ship
  trong `SettingDashboard.razor` (PR #211) để không lệch nội dung khi persist.
