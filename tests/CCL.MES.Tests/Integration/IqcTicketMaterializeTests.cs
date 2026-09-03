using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Domain.Entities;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P12 bước 2a — mở ticket IQC thì hạng mục kiểm được dựng từ thư viện và
/// <b>đóng băng</b> vào bản ghi.
///
/// <para>Khoá ba điều mà chỉ chạy thật mới thấy: (a) khoá nối đi qua
/// <c>MotherCode</c> của <c>RawMaterial</c>; (b) hạng mục mới mở ở trạng thái
/// <b>CHƯA KIỂM</b> chứ không phải NG; (c) đường nhập tay cũ không bị đè.</para>
/// </summary>
public sealed class IqcTicketMaterializeTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcTicketMaterializeTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    private const string Actor = "qc-user";
    private const string Role = UserRole.Qc;

    private static async Task SeedLibraryAsync(MesDbContext db)
    {
        db.IqcCheckItemLibraries.AddRange(
            new IqcCheckItemLibrary { ItemId = "NL-01", GroupCode = "NL", GroupLabelVi = "Nguyên liệu",
                ItemVi = "Nhận dạng", Sort = 10, Active = true,
                InDefaultMatrix = true, DefaultAcceptanceVi = "Theo mẫu chuẩn" },
            new IqcCheckItemLibrary { ItemId = "NQ-01", GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
                ItemVi = "Tem nhãn", Sort = 20, Active = true,
                InDefaultMatrix = true, DefaultAcceptanceVi = "Đúng thông tin" },
            new IqcCheckItemLibrary { ItemId = "KT-02", GroupCode = "KT", GroupLabelVi = "Kích thước",
                ItemVi = "Chiều dài", Sort = 30, Active = true, InDefaultMatrix = false });

        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        {
            SpecNo = "CCL-SPEC-QC229", MaterialCode = "336-H1a", Active = true,
        });
        db.IqcSpecItems.AddRange(
            new IqcSpecItem { SpecNo = "CCL-SPEC-QC229", ItemId = "NL-01", Seq = 1,
                AcceptanceVi = "TIÊU CHUẨN RIÊNG 336-H1a", SourceFrequency = "All lot", Active = true },
            new IqcSpecItem { SpecNo = "CCL-SPEC-QC229", ItemId = "KT-02", Seq = 1,
                AcceptanceVi = "dài 500M ± 5", Active = true });
        await db.SaveChangesAsync();
    }

    private static async Task<long> SeedMaterialAsync(MesDbContext db, string partNo, string? motherCode)
    {
        var rm = new RawMaterial { PartNo = partNo, MotherCode = motherCode, SupplierName = "NCC" };
        db.RawMaterials.Add(rm);
        await db.SaveChangesAsync();
        return rm.Id;
    }

    /// <summary>IqcService cần MaterialLotScanService (mở lô Quarantine lúc tạo
    /// ticket) — dùng chung MỘT context như RbacServiceTests.</summary>
    private static IqcService Svc(MesDbContext db)
    {
        var audit = new InMemoryAuditWriter();
        var lots = new MaterialLotScanService(
            db, audit, Microsoft.Extensions.Options.Options.Create(new MaterialLotOptions()));
        return new IqcService(db, audit, lots);
    }

    private static CreateIqcRequest Req(string partNo) => new(
        PartNo: partNo, BatchNumber: "LOT-1", LotNumber: null,
        ReceivedDate: new DateTime(2026, 8, 28), SupplierName: "NCC",
        Quantity: 100, UomQty: null, InspectorId: null, SampleSize: 5,
        Details: new List<CreateIqcDetail>());

    // ── (a) khoá nối qua MotherCode ──────────────────────────────────────

    [Fact]
    public async Task Mo_ticket_cho_ma_CO_spec_thi_dung_tieu_chuan_RIENG()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030146", "336-H1a");

        var insp = await Svc(db).CreateAsync(Req("30030146"), Actor, Role);
        var items = await db.IqcResultDetails.Where(d => d.IqcInspectionId == insp.Id).ToListAsync();

        Assert.Equal(2, items.Count);                       // đúng bộ của spec
        Assert.All(items, i => Assert.Equal("CCL-SPEC-QC229", i.SpecNo));
        Assert.All(items, i => Assert.False(i.FromDefaultMatrix));

        var nl = items.Single(i => i.ItemKey == "NL-01");
        Assert.Equal("TIÊU CHUẨN RIÊNG 336-H1a", nl.AcceptanceVi);
        Assert.NotEqual("Theo mẫu chuẩn", nl.AcceptanceVi);  // KHÔNG lấy giá trị chung
    }

    [Fact]
    public async Task Mo_ticket_cho_ma_CHUA_co_spec_thi_dung_ma_tran_va_danh_dau()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030999", "TWP5050");   // 1 trong 590 mã chưa có spec

        var insp = await Svc(db).CreateAsync(Req("30030999"), Actor, Role);
        var items = await db.IqcResultDetails.Where(d => d.IqcInspectionId == insp.Id).ToListAsync();

        Assert.Equal(2, items.Count);                          // chỉ hạng mục trong ma trận
        Assert.All(items, i => Assert.True(i.FromDefaultMatrix));
        Assert.All(items, i => Assert.Null(i.SpecNo));
        Assert.DoesNotContain("KT-02", items.Select(i => i.ItemKey));
    }

    [Fact]
    public async Task Nguyen_lieu_khong_co_MotherCode_thi_KHONG_doan_bua()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030888", motherCode: null);

        var insp = await Svc(db).CreateAsync(Req("30030888"), Actor, Role);

        // Ticket vẫn tạo được, chỉ là không dựng hạng mục — người kiểm nhập tay
        // như trước. Dựng sai bộ hạng mục còn tệ hơn không dựng.
        Assert.Empty(await db.IqcResultDetails.Where(d => d.IqcInspectionId == insp.Id).ToListAsync());
    }

    // ── (b) trạng thái mở ticket ─────────────────────────────────────────

    [Fact]
    public async Task Hang_muc_moi_dung_o_CHUA_KIEM_chu_KHONG_phai_NG()
    {
        // Trước P12, Pass là bool không nullable ⇒ mọi hạng mục mới dựng sẽ
        // hiện NG, tuyên bố cả lô không đạt mà không ai bấm gì.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030146", "336-H1a");

        var insp = await Svc(db).CreateAsync(Req("30030146"), Actor, Role);
        var items = await db.IqcResultDetails.Where(d => d.IqcInspectionId == insp.Id).ToListAsync();

        Assert.All(items, i => Assert.Null(i.Pass));
        Assert.DoesNotContain(items, i => i.Pass == false);
    }

    [Fact]
    public async Task Dong_bang_ca_hai_ngon_ngu_va_nhom()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030999", "TWP5050");

        var insp = await Svc(db).CreateAsync(Req("30030999"), Actor, Role);
        var nl = await db.IqcResultDetails.FirstAsync(d => d.IqcInspectionId == insp.Id && d.ItemKey == "NL-01");

        Assert.Equal("Nhận dạng", nl.LabelVi);
        Assert.Equal("NL", nl.GroupCode);
        Assert.Equal("Nguyên liệu", nl.GroupLabelVi);
        Assert.Equal("Theo mẫu chuẩn", nl.AcceptanceVi);
        // ItemName giữ bản VI để công cụ cũ vẫn đọc được bản ghi.
        Assert.Equal("Nhận dạng", nl.ItemName);
    }

    // ── (c) đường nhập tay cũ không bị đè ────────────────────────────────

    [Fact]
    public async Task Client_gui_hang_muc_thi_TON_TRONG_khong_de_bang_thu_vien()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030146", "336-H1a");

        var req = Req("30030146");
        req.Details.Add(new CreateIqcDetail(
            ItemName: "Hạng mục nhập tay", MeasuredValue: null, Pass: true, DefectCode: null, Qty: 3));

        var insp = await Svc(db).CreateAsync(req, Actor, Role);
        var items = await db.IqcResultDetails.Where(d => d.IqcInspectionId == insp.Id).ToListAsync();

        var only = Assert.Single(items);
        Assert.Equal("Hạng mục nhập tay", only.ItemName);
        Assert.True(only.Pass);
        Assert.Null(only.ItemKey);          // không phải hạng mục thư viện
    }

    // ── (d) ĐƯỜNG UI THẬT: CreateTicketAsync ─────────────────────────────
    //
    // Màn "Khai báo mới" gọi POST /api/v2/iqc → CreateTicketAsync, KHÔNG phải
    // CreateAsync. Nối thư viện vào một nhánh rồi tưởng xong là cách bộ hạng
    // mục vẫn trống trên máy Henry dù test xanh.

    private static CreateIqcTicketRequest Ticket(string codeIfs, string lot) => new()
    {
        CodeIfs = codeIfs, LotBatchNo = lot, Quantity = 100, SampleSize = 5,
        SupplierName = "NCC", Uom = "M",
    };

    [Fact]
    public async Task Duong_UI_CreateTicket_CO_spec_thi_dung_hang_muc_RIENG()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030146", "336-H1a");

        var r = await Svc(db).CreateTicketAsync(Ticket("30030146", "LOT-UI-1"), Actor, Role);
        Assert.True(r.Ok, $"tạo phiếu hỏng: {r.ErrorCode}");

        var items = await db.IqcResultDetails
            .Where(d => d.IqcInspectionId == r.IqcInspectionId).ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("CCL-SPEC-QC229", i.SpecNo));
        Assert.All(items, i => Assert.Null(i.Pass));        // CHƯA KIỂM, không phải NG
        Assert.Equal("TIÊU CHUẨN RIÊNG 336-H1a",
            items.Single(i => i.ItemKey == "NL-01").AcceptanceVi);
    }

    [Fact]
    public async Task Duong_UI_CreateTicket_CHUA_co_spec_thi_dung_ma_tran()
    {
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);
        await SeedMaterialAsync(db, "30030999", "TWP5050");

        var r = await Svc(db).CreateTicketAsync(Ticket("30030999", "LOT-UI-2"), Actor, Role);
        Assert.True(r.Ok, $"tạo phiếu hỏng: {r.ErrorCode}");

        var items = await db.IqcResultDetails
            .Where(d => d.IqcInspectionId == r.IqcInspectionId).ToListAsync();

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.FromDefaultMatrix));
    }

    [Fact]
    public async Task Duong_UI_ma_KHONG_khop_catalog_thi_van_tao_duoc_phieu_nhung_KHONG_dung_bua()
    {
        // matchStatus=unmatched ⇒ rawMaterialId null. Phiếu vẫn phải lưu được
        // (quyết định #2), chỉ là không có bộ hạng mục nào được suy ra.
        await using var db = _fx.NewContext();
        await SeedLibraryAsync(db);

        var r = await Svc(db).CreateTicketAsync(Ticket("MA-LA-HOAC", "LOT-UI-3"), Actor, Role);

        Assert.True(r.Ok, $"tạo phiếu hỏng: {r.ErrorCode}");
        Assert.Empty(await db.IqcResultDetails
            .Where(d => d.IqcInspectionId == r.IqcInspectionId).ToListAsync());
    }
}
