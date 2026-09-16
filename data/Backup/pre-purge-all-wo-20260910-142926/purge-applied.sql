PRAGMA foreign_keys = ON;   -- per-connection; CLI mặc định TẮT (đã đo)
BEGIN;

-- ① CHẮT trước — WoQcPhotos treo dưới WoQcCheckItems, KHÔNG phải dưới
--    WoQcChecks. Xoá sai thứ tự là để lại một tầng mồ côi mới.
DELETE FROM WoQcPhotos       WHERE WoQcCheckItemId IN (SELECT Id FROM WoQcCheckItems);

-- ② CHÁU (con của WoIpqcChecks / WoQcChecks)
DELETE FROM WoIpqcCheckItems WHERE WoIpqcCheckId IN (SELECT Id FROM WoIpqcChecks);
DELETE FROM WoQcCheckItems   WHERE WoQcCheckId   IN (SELECT Id FROM WoQcChecks);

-- ③ CON trực tiếp — 13 bảng KHÔNG có khoá ngoại, phải quét tay
DELETE FROM WoMaterials      WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM WoIpqcChecks     WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM WoQcChecks       WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM WoPlateChecks    WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM WoCutterChecks   WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM WoLegDependencies WHERE WorkOrderId      IN (SELECT Id FROM WorkOrders);
DELETE FROM WoLegs           WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM SemiAllocations  WHERE WorkOrderId       IN (SELECT Id FROM WorkOrders);
DELETE FROM SemiLots         WHERE SourceWorkOrderId IN (SELECT Id FROM WorkOrders);
DELETE FROM WoPauseEvents    WHERE WoId              IN (SELECT Id FROM WorkOrders);
DELETE FROM WoQtyEntries     WHERE WoId              IN (SELECT Id FROM WorkOrders);
DELETE FROM WoRunSessions    WHERE WoId              IN (SELECT Id FROM WorkOrders);
DELETE FROM WoTraceIndexes   WHERE WoId              IN (SELECT Id FROM WorkOrders);
DELETE FROM WoTraceSnapshots WHERE WoId              IN (SELECT Id FROM WorkOrders);

-- ④ CHA. Bốn bảng có khoá ngoại (StatusHistories · SettingCheckItems ·
--    IpqcMaterialChecks · MaterialConsumptions) tự dọn bằng cascade — đã đo
--    khoá ngoại ĐANG được thực thi qua đường EF, và pragma trên đã bật cho
--    phiên CLI này.
DELETE FROM WorkOrders;

COMMIT;
