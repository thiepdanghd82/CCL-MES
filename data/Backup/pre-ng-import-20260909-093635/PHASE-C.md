# Phase C — nạp sheet NG Material vào IqcNgRecords
thời điểm : 2026-09-09 09:38:23
nguồn     : Copy of IQC report 2026 (version 1).xlsx · sheet 'NG Material '
đọc 152 dòng → nạp 139 · bỏ 13 (dòng mẫu trống, không NCC/mã/lỗi/ngày)

## rowcount
IqcNgRecords 0 → 139 · AuditLogs 3254 → 3255 (1 dòng audit import)
IqcInspections 5334 (không đổi) · RawMaterials 2967 (không đổi)

## đối chứng với số đo của người thiết kế schema
Stage IQC 70 / SX 64      — khớp
Đền bù 84 / 39            — khớp
Vòng đời 123 / 8 / 6 / 2  — khớp
PartNo khớp RawMaterials 122/139 (87,8%) — khớp

## idempotent
chạy lần 2: thêm=0 sửa=0 không-đổi=139

## hoàn tác
DELETE FROM IqcNgRecords WHERE ImportSource LIKE 'xlsx:NG Material:%';
