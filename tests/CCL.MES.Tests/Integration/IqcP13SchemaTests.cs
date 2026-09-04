using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P13 bước 2 — hình dạng schema mở rộng cho IQC.
///
/// <para>Bộ này khoá đúng những chỗ đã suýt hỏng khi làm, chứ không khoá cho có:
/// giá trị enum của dòng CŨ sau migration, tính ba-trạng-thái của ô đếm lỗi, và
/// ràng buộc duy nhất của bảng đo lặp.</para>
/// </summary>
public sealed class IqcP13SchemaTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;
    public IqcP13SchemaTests(IsolatedDbFixture fx) => _fx = fx;

    private MesDbContext Db() => new(_fx.Options);

    // ── enum lưu dạng CHUỖI, không phải số ───────────────────────────────

    [Fact]
    public async Task Enum_luu_ra_DB_duoi_dang_CHUOI_doc_duoc_bang_sqlite3()
    {
        await using var db = Db();
        var item = new IqcCheckItemLibrary
        {
            ItemId = "P13-STR-1", GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
            ItemVi = "Xước", Category = IqcMaterialCategory.Roll,
            Kind = IqcCheckKind.DefectCount, MeasureCount = 0,
        };
        db.IqcCheckItemLibraries.Add(item);
        await db.SaveChangesAsync();

        // Đọc RAW: lúc điều tra sự cố lúc 2 giờ sáng, người ta mở sqlite3 chứ
        // không mở Visual Studio. "Roll" đọc được; "1" thì phải tra enum.
        var raw = await db.Database
            .SqlQuery<string>($"SELECT Category FROM IqcCheckItemLibraries WHERE ItemId = 'P13-STR-1'")
            .ToListAsync();
        Assert.Equal("Roll", Assert.Single(raw));
    }

    [Fact]
    public async Task Hang_muc_khong_khai_gi_thi_ve_Any_va_Verdict()
    {
        // Đây chính là giá trị mà 21 hạng mục CŨ nhận sau migration. EF sinh
        // defaultValue = "" (nó không đọc giá trị khởi tạo của property C#) —
        // migration phải tự sửa lại, nếu không cả 21 dòng mang giá trị NGOÀI
        // enum và gate enum-integrity sẽ đỏ.
        await using var db = Db();
        var item = new IqcCheckItemLibrary
        {
            ItemId = "P13-DEF-1", GroupCode = "KH", GroupLabelVi = "Khác", ItemVi = "Mặc định",
        };
        db.IqcCheckItemLibraries.Add(item);
        await db.SaveChangesAsync();

        var back = await db.IqcCheckItemLibraries.AsNoTracking()
            .SingleAsync(x => x.ItemId == "P13-DEF-1");
        Assert.Equal(IqcMaterialCategory.Any, back.Category);
        Assert.Equal(IqcCheckKind.Verdict, back.Kind);
        Assert.Equal(0, back.MeasureCount);
    }

    [Fact]
    public async Task Spec_tao_trong_app_mac_dinh_la_DA_DUYET()
    {
        // Chỉ hàng NHẬP TỪ FILE NGOÀI mới phải chờ duyệt. Nếu mặc định là
        // PendingQc thì mọi spec do QC tự tạo trong app cũng vào hàng chờ —
        // chờ chính người vừa tạo ra nó duyệt.
        await using var db = Db();
        db.IqcMaterialSpecs.Add(new IqcMaterialSpec
        { SpecNo = "P13-APP-1", MaterialCode = "MC-1" });
        await db.SaveChangesAsync();

        var back = await db.IqcMaterialSpecs.AsNoTracking()
            .SingleAsync(x => x.SpecNo == "P13-APP-1");
        Assert.Equal(IqcSpecApproval.Approved, back.Approval);
        Assert.Null(back.ImportSource);
    }

    // ── ba trạng thái: chưa làm ≠ làm rồi và bằng 0 ─────────────────────

    [Fact]
    public async Task Chua_dem_va_dem_duoc_0_la_HAI_chuyen_khac_nhau()
    {
        // Bài học L67 áp cho cột mới: bool/int không nullable nuốt mất trạng
        // thái "chưa làm", và bản ghi bằng chứng nói dối một cách im lặng.
        await using var db = Db();
        var insp = new IqcInspection { PartNo = "P13-Q1", ReceivedDate = DateTime.UtcNow };
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();

        db.IqcResultDetails.AddRange(
            new IqcResultDetail { IqcInspectionId = insp.Id, ItemName = "chưa đếm", DefectCount = null },
            new IqcResultDetail { IqcInspectionId = insp.Id, ItemName = "đếm rồi, sạch", DefectCount = 0 });
        await db.SaveChangesAsync();

        var rows = await db.IqcResultDetails.AsNoTracking()
            .Where(x => x.IqcInspectionId == insp.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Null(rows[0].DefectCount);
        Assert.Equal(0, rows[1].DefectCount);
    }

    [Fact]
    public async Task Chua_do_va_do_duoc_0_cung_la_hai_chuyen_khac_nhau()
    {
        await using var db = Db();
        var insp = new IqcInspection { PartNo = "P13-Q2", ReceivedDate = DateTime.UtcNow };
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();
        var d = new IqcResultDetail { IqcInspectionId = insp.Id, ItemName = "Độ dày" };
        db.IqcResultDetails.Add(d);
        await db.SaveChangesAsync();

        db.IqcResultMeasurements.AddRange(
            new IqcResultMeasurement { IqcResultDetailId = d.Id, Seq = 1, Value = 0.16 },
            new IqcResultMeasurement { IqcResultDetailId = d.Id, Seq = 2, Value = null });
        await db.SaveChangesAsync();

        var ms = await db.IqcResultMeasurements.AsNoTracking()
            .Where(x => x.IqcResultDetailId == d.Id).OrderBy(x => x.Seq).ToListAsync();
        Assert.Equal(0.16, ms[0].Value);
        Assert.Null(ms[1].Value);
    }

    [Fact]
    public async Task Hai_phep_do_cung_so_thu_tu_bi_DB_chan()
    {
        // Hai dòng cùng Seq trên một hạng mục là dữ liệu HỎNG, không phải hai
        // lần đo. Chặn ở tầng DB chứ đừng chỉ trông vào service — service nào
        // đó sẽ quên, và lúc đó không ai biết con số nào là thật.
        await using var db = Db();
        var insp = new IqcInspection { PartNo = "P13-Q3", ReceivedDate = DateTime.UtcNow };
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();
        var d = new IqcResultDetail { IqcInspectionId = insp.Id, ItemName = "Độ rộng" };
        db.IqcResultDetails.Add(d);
        await db.SaveChangesAsync();

        db.IqcResultMeasurements.Add(new IqcResultMeasurement { IqcResultDetailId = d.Id, Seq = 1, Value = 72 });
        await db.SaveChangesAsync();

        db.IqcResultMeasurements.Add(new IqcResultMeasurement { IqcResultDetailId = d.Id, Seq = 1, Value = 71 });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── dấu vết máy chấm / người đổi ─────────────────────────────────────

    [Fact]
    public async Task Ban_ghi_giu_CA_HAI_may_noi_gi_va_nguoi_doi_thanh_gi()
    {
        // Henry chốt 2026-09-04: máy chấm là RÀNG BUỘC, đổi phải ghi lý do, và
        // hồ sơ phải trả lời được "máy nói gì · ai đổi · vì sao".
        await using var db = Db();
        var insp = new IqcInspection { PartNo = "P13-OV", ReceivedDate = DateTime.UtcNow };
        db.IqcInspections.Add(insp);
        await db.SaveChangesAsync();

        db.IqcResultDetails.Add(new IqcResultDetail
        {
            IqcInspectionId = insp.Id, ItemName = "Ngoại quan",
            DefectCount = 2,
            AutoVerdict = "Fail", AutoVerdictReason = "iqc.judge.defect_found",
            AutoVerdictOffendingSeq = 3,
            Pass = true,                       // người kết luận NGƯỢC lại máy
            OverrideReason = "Lỗi nằm ngoài vùng dán, khách đã chấp nhận đặc cách",
            OverriddenBy = "qc-lead", OverriddenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var r = await db.IqcResultDetails.AsNoTracking()
            .SingleAsync(x => x.IqcInspectionId == insp.Id);
        Assert.Equal("Fail", r.AutoVerdict);          // máy nói trượt
        Assert.True(r.Pass);                          // người cho đạt
        Assert.Equal(3, r.AutoVerdictOffendingSeq);   // và chỉ rõ ô nào gây trượt
        Assert.NotNull(r.OverrideReason);
        Assert.Equal("qc-lead", r.OverriddenBy);
    }

    // ── cỡ mẫu: đề xuất đóng băng + lý do ghi đè ─────────────────────────

    [Fact]
    public async Task Phieu_giu_ca_co_mau_DE_XUAT_lan_co_mau_THUC_TE()
    {
        // Không tính lại lúc đọc: bảng lấy mẫu có thể đổi, và khi đó phiếu cũ
        // phải vẫn nói đúng điều đã xảy ra hôm đó.
        await using var db = Db();
        db.IqcInspections.Add(new IqcInspection
        {
            PartNo = "P13-SS", ReceivedDate = DateTime.UtcNow,
            LotQty = 60, SampleSizeSuggested = 13, SampleSize = 60,
            SampleSizeOverrideReason = "NCC mới, kiểm 100% ba lô đầu",
        });
        await db.SaveChangesAsync();

        var i = await db.IqcInspections.AsNoTracking().SingleAsync(x => x.PartNo == "P13-SS");
        Assert.Equal(60, i.LotQty);
        Assert.Equal(13, i.SampleSizeSuggested);
        Assert.Equal(60, i.SampleSize);
        Assert.NotNull(i.SampleSizeOverrideReason);
    }

    // ── ngưỡng số trên spec item ─────────────────────────────────────────

    [Fact]
    public async Task Spec_item_giu_nguyen_van_tieu_chuan_BEN_CANH_nguong_so()
    {
        // Ngưỡng số chỉ là bản ĐỌC ĐƯỢC của tiêu chuẩn. Người kiểm vẫn phải
        // đọc được nguyên văn để đối chiếu với tờ giấy của NCC.
        await using var db = Db();
        db.IqcSpecItems.Add(new IqcSpecItem
        {
            SpecNo = "P13-LIM", ItemId = "KT-04", Seq = 1,
            AcceptanceVi = "Adhesive 0.16±0.016",
            LimitLow = 0.144, LimitUp = 0.176, LimitNominal = 0.16,
            LimitUnit = null, LimitParsed = true,
        });
        db.IqcSpecItems.Add(new IqcSpecItem
        {
            SpecNo = "P13-LIM", ItemId = "KT-01", Seq = 1,
            AcceptanceVi = "Tham khảo báo cáo",
            LimitParsed = false,
        });
        await db.SaveChangesAsync();

        var rows = await db.IqcSpecItems.AsNoTracking()
            .Where(x => x.SpecNo == "P13-LIM").OrderBy(x => x.ItemId).ToListAsync();
        var noLimit = rows.Single(x => x.ItemId == "KT-01");
        var withLimit = rows.Single(x => x.ItemId == "KT-04");

        Assert.False(noLimit.LimitParsed);
        Assert.Null(noLimit.LimitLow);
        Assert.Equal("Tham khảo báo cáo", noLimit.AcceptanceVi);   // vẫn hiện được

        Assert.True(withLimit.LimitParsed);
        Assert.Equal(0.144, withLimit.LimitLow!.Value, 6);
        Assert.Equal("Adhesive 0.16±0.016", withLimit.AcceptanceVi);
    }
}
