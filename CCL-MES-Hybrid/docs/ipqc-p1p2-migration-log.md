# Nhật ký migration — IPQC P1 + P2 (đóng băng tiêu chí · cỡ mẫu theo cavity)

Migration: `20260911060118_AddIpqcFrozenAcceptanceCriteria`
Ngày: 2026-09-11 · Tác giả: Claude, theo yêu cầu Thiệp
Trạng thái: **Phase A + B XONG — Phase C CHƯA ÁP, chờ Henry duyệt (STOP-gate §0)**

## Vì sao

Thiệp chốt 2026-09-11 bốn tiêu chí IPQC. Hai trong số đó cần cột mới:

1. **Đóng băng tiêu chí đã áp.** Thư viện có `Aql` + `Sampling` ở **59/59**
   hạng mục IPQC, nhưng `IpqcLibraryMaterializer` nhắc tới chúng **0 lần** và
   `WoIpqcCheckItems` **không có cột** chứa. Hồ sơ ghi được "Đạt" mà không ghi
   được đạt theo tiêu chí nào — ISO 9001:2015 §8.6 đòi đúng điều đó.

2. **Cỡ mẫu FAI theo cavity.** Lô IPQC là số cavity của một shot in hoặc cắt,
   FAI kiểm 100% số ấy. KHÔNG dùng bảng AQL theo cỡ lô (ISO 2859-1) như IQC —
   khuôn nhiều cavity sinh lỗi lặp theo vị trí, lấy mẫu ngẫu nhiên dễ trượt.

## Thay đổi schema — additive, 4 cột nullable trên `WoIpqcCheckItems`

| Cột | Kiểu | Ý nghĩa |
|---|---|---|
| `Aql` | TEXT(32) null | mức AQL đóng băng từ thư viện, giữ nguyên chuỗi |
| `Sampling` | TEXT(128) null | cách lấy mẫu đóng băng từ thư viện |
| `CavityCount` | INTEGER null | số cavity phải kiểm ở FAI; null = chưa giải được |
| `CavitySource` | TEXT(16) null | Print · Cut · Ambiguous · Missing · Manual · N/A |

Không đổi nghĩa cột cũ. Không index mới. Không trigger. Không rebuild bảng.

## Phase A — baseline (2026-09-11 12:59:28)

```
backup   : data/Backup/SQLite/ccl_mes.db.before-ipqc-p1p2.20260911-125928
sha live : 19cd2789686f347ea8b5ea999a3a491c7a8df16f9788f1a15b28fd7c39db8b05
sha bkup : edc032185da4dfba55fcb5e604b16c2a6019d39fa2679e9e426716b210e37a84
```

> **Hai sha KHÁC nhau là ĐÚNG, không phải lỗi.** `.backup` sinh bản đã
> checkpoint, còn file live mang WAL riêng. Kiểm bản sao bằng **nội dung**:

| Bảng | live | backup |
|---|---|---|
| WorkOrders | 2 | 2 ✓ |
| WoMaterials | 8 | 8 ✓ |
| CheckItemLibraries | 87 | 87 ✓ |
| SpecPrints | 330 | 330 ✓ |
| IqcInspections | 5.334 | 5.334 ✓ |
| MaterialLots | 4.850 | 4.850 ✓ |
| AuditLogs | 3.414 | 3.414 ✓ |

`integrity_check` = ok · `foreign_key_check` = 0 vi phạm (cả hai bên).
Snapshot code: `/tmp/snapshot-pre-ipqc-p1p2.cs`.

**Rowcount đáng chú ý:** `WoIpqcChecks` = 0 và `WoIpqcCheckItems` = **0**.
Bảng đang RỖNG, nên migration không chạm dòng dữ liệu nào.

## Phase B — DB cô lập `/tmp/ipqc-p1p2-design.db`

- Sinh migration với `MES_CONNSTR` trỏ `/tmp`, **không** trỏ live.
- Đã đọc nội dung migration trước khi áp: 4 `AddColumn`, 4 `DropColumn` ở Down.
- Type-affinity: **đã strip** 4 dòng `type:` → còn 0 (khớp quy ước 5 migration gần nhất).
- Áp lên DB cô lập: thành công, ghi `__EFMigrationsHistory`.
- `.schema` sau khi áp: 4 cột, cả 4 `notnull=0`.

### Hai lỗi gặp ở Phase B, đều là binary cũ

1. Lần sinh đầu ra **migration RỖNG**. Nguyên nhân: `dotnet ef` dùng startup
   project `CCL.MES.Web`, mà `bin/` của nó giữ `Domain.dll` từ hôm trước
   (0 lần `CavityCount`). Gỡ bằng `rm` + `git checkout` snapshot — **không**
   dùng `ef migrations remove` (§4.1).
2. `database update --no-build` báo `PendingModelChangesWarning`: migration đã
   có trong nguồn nhưng chưa được biên dịch vào assembly. Build lại rồi áp.

## Phase C — CHƯA CHẠY

STOP-gate theo CLAUDE.md §0: migration lên DB live phải qua Henry.

**Rollback nếu cần:**
```bash
rm -f src/CCL.MES.Infrastructure/Migrations/*AddIpqcFrozenAcceptanceCriteria*
cp /tmp/snapshot-pre-ipqc-p1p2.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
```
Nếu đã áp live rồi mới muốn lùi: restore từ `data/Backup/SQLite/ccl_mes.db.before-ipqc-p1p2.20260911-125928`.

**Rủi ro đánh giá THẤP:** 4 cột nullable, bảng đích rỗng, không rebuild bảng,
không đụng index/trigger. So với `AddMaterialLotGenealogy` (19/08, có rebuild)
thì lần này nhẹ hơn hẳn.
