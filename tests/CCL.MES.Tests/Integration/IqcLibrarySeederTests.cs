using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P12 — seeder thư viện tiêu chuẩn kiểm tra NVL (IQC), chạy trên <b>ba file
/// CSV THẬT</b> trong repo để khoá số lượng và chứng minh idempotent.
///
/// <para>Nguồn: <c>IQC_Master_Tieu_chuan_kiem_tra_NVL.xlsx</c> — tổng hợp
/// 19/08/2026 từ 809 file spec gốc, đã lọc bỏ 1 dòng template rỗng và 13 dòng
/// chi tiết của nó.</para>
/// </summary>
public sealed class IqcLibrarySeederTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    public IqcLibrarySeederTests() => _fx = new IsolatedDbFixture();
    public void Dispose() => _fx.Dispose();

    /// <summary>Số liệu khoá từ file thật. Đổi file master mà quên sửa đây ⇒
    /// test ĐỎ, buộc người sửa nhìn lại con số thay vì để nó trôi.</summary>
    /// <summary>P13: 21 hạng mục chung + 30 hạng mục theo NHÓM vật liệu
    /// (Roll 13 · Pcs 9 · Tool 5 · Chem 3), tên lấy nguyên văn từ file master
    /// "IQC report 2026".</summary>
    private const int ExpectedItems = 51;
    private const int ExpectedSpecs = 459;
    private const int ExpectedSpecItems = 5961;

    /// <summary>Đi ngược từ thư mục test lên tới khi thấy <c>CCL-MES-Hybrid/docs/iqc-library</c>.
    /// Không hardcode đường dẫn tuyệt đối — CI chạy ở chỗ khác.</summary>
    private static string CsvDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, "CCL-MES-Hybrid", "docs", "iqc-library");
            if (Directory.Exists(p)) return p;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException(
            "Không thấy CCL-MES-Hybrid/docs/iqc-library — ba file CSV master của P12 phải nằm trong repo.");
    }

    private static (string Items, string Specs, string Details) Csv()
    {
        var d = CsvDir();
        return (File.ReadAllText(Path.Combine(d, "iqc_check_items.csv")),
                File.ReadAllText(Path.Combine(d, "iqc_material_specs.csv")),
                File.ReadAllText(Path.Combine(d, "iqc_spec_items.csv")));
    }

    private static async Task<DbSeeder.IqcLibrarySeedResult> SeedAsync(MesDbContext db)
    {
        var (i, s, d) = Csv();
        return await DbSeeder.SeedIqcLibraryAsync(db, i, s, d);
    }

    // ── số lượng từ file thật ────────────────────────────────────────────

    [Fact]
    public async Task Seed_lan_dau_nap_dung_so_luong_tu_file_that()
    {
        await using var db = _fx.NewContext();
        var r = await SeedAsync(db);

        Assert.Equal(ExpectedItems, r.Items);
        Assert.Equal(ExpectedSpecs, r.Specs);
        Assert.Equal(ExpectedSpecItems, r.SpecItems);
        Assert.Equal(0, r.Updated);

        Assert.Equal(ExpectedItems, await db.IqcCheckItemLibraries.CountAsync());
        Assert.Equal(ExpectedSpecs, await db.IqcMaterialSpecs.CountAsync());
        Assert.Equal(ExpectedSpecItems, await db.IqcSpecItems.CountAsync());
    }

    [Fact]
    public async Task Seed_lan_hai_la_NOOP_khong_nhan_doi()
    {
        await using var db = _fx.NewContext();
        await SeedAsync(db);
        var r2 = await SeedAsync(db);

        Assert.Equal(0, r2.Items);
        Assert.Equal(0, r2.Specs);
        Assert.Equal(0, r2.SpecItems);
        Assert.Equal(0, r2.Updated);
        Assert.Equal(ExpectedSpecItems, await db.IqcSpecItems.CountAsync());
    }

    [Fact]
    public async Task Sua_mot_truong_thi_lan_seed_sau_cap_nhat_dung_MOT_dong()
    {
        await using var db = _fx.NewContext();
        await SeedAsync(db);

        var row = await db.IqcSpecItems.FirstAsync();
        row.AcceptanceVi = "GIÁ TRỊ BỊ SỬA TAY";
        await db.SaveChangesAsync();

        var r = await SeedAsync(db);
        Assert.Equal(1, r.Updated);
        Assert.Equal(0, r.SpecItems);
    }

    // ── bất biến của dữ liệu ─────────────────────────────────────────────

    [Fact]
    public async Task Tieu_chuan_KHAC_NHAU_theo_tung_nguyen_lieu()
    {
        // Đây là bất biến quan trọng nhất của P12. Nếu ai đó "đơn giản hoá"
        // bằng cách lấy tiêu chuẩn từ bảng 21 hạng mục, test này ĐỎ.
        await using var db = _fx.NewContext();
        await SeedAsync(db);

        var distinct = await db.IqcSpecItems
            .Where(x => x.ItemId == "BD-01" && x.AcceptanceVi != null)
            .Select(x => x.AcceptanceVi!)
            .Distinct()
            .CountAsync();

        Assert.True(distinct > 20,
            $"BD-01 (độ bám dính) chỉ có {distinct} tiêu chuẩn khác nhau — trên dữ liệu " +
            "thật phải là ~60. Ít hơn nhiều nghĩa là tiêu chuẩn đang bị lấy từ bảng " +
            "hạng mục dùng chung thay vì bảng chi tiết theo nguyên liệu.");
    }

    [Fact]
    public async Task Ma_IFS_duoc_tach_ra_khoi_ten_nguyen_lieu()
    {
        await using var db = _fx.NewContext();
        await SeedAsync(db);

        var withIfs = await db.IqcMaterialSpecs.CountAsync(x => x.MaterialCodeIfs != null);
        Assert.Equal(46, withIfs);

        // Hai mã lệch độ dài chuẩn 8 số — CCL-SPEC-QC416 (7 số) và
        // CCL-SPEC-QC635 (9 số). Vẫn trích vì rõ ràng là mã gõ nhầm độ dài;
        // bỏ chúng đi thì mất khoá nối mà không ai biết. Đã ghi vào scope
        // proposal §6 để Ops xác nhận.
        var odd = await db.IqcMaterialSpecs
            .Where(x => x.MaterialCodeIfs != null && x.MaterialCodeIfs.Length != 8)
            .CountAsync();
        Assert.Equal(2, odd);

        // Tên đã sạch phần trong ngoặc — nếu không, resolve theo tên sẽ trượt.
        Assert.False(await db.IqcMaterialSpecs.AnyAsync(x => x.MaterialCode.Contains("(7")));
    }

    [Fact]
    public async Task Tan_suat_goc_duoc_luu_nguyen_van_ke_ca_dong_theo_thang()
    {
        // Quyết định D1 (kiểm mọi lô) là CHÍNH SÁCH, không được xoá dấu vết
        // spec gốc. Khi audit hỏi phải trả lời được "spec ghi tháng".
        await using var db = _fx.NewContext();
        await SeedAsync(db);

        var monthly = await db.IqcSpecItems
            .CountAsync(x => x.SourceFrequency != null && x.SourceFrequency.Contains("tháng"));

        Assert.True(monthly > 1000,
            $"chỉ còn {monthly} dòng giữ tần suất theo tháng — trên dữ liệu thật phải ~1334.");
    }

    [Fact]
    public async Task Moi_dong_tieu_chuan_deu_tro_ve_mot_spec_va_mot_hang_muc_co_that()
    {
        await using var db = _fx.NewContext();
        await SeedAsync(db);

        var specs = await db.IqcMaterialSpecs.Select(x => x.SpecNo).ToListAsync();
        var items = await db.IqcCheckItemLibraries.Select(x => x.ItemId).ToListAsync();

        var orphanSpec = await db.IqcSpecItems.CountAsync(x => !specs.Contains(x.SpecNo));
        var orphanItem = await db.IqcSpecItems.CountAsync(x => !items.Contains(x.ItemId));

        Assert.Equal(0, orphanSpec);
        Assert.Equal(0, orphanItem);
    }

    // ── P13: nhóm vật liệu · kiểu ghi nhận · số lần đo ───────────────────

    [Fact]
    public async Task So_lan_do_dung_5_cho_kich_thuoc_va_do_day()
    {
        // Henry chốt 2026-09-04: Roll đo rộng ×5 + dày ×5; PCS đo dài ×5 +
        // rộng ×5 + dày ×5. CỐ ĐỊNH bất kể cỡ lô — khác hẳn cỡ mẫu ngoại quan
        // vốn tra theo bảng AQL.
        await using var db = _fx.NewContext();
        await SeedAsync(db);
        foreach (var id in new[] { "KT-02", "KT-03", "KT-04" })
        {
            var e = await db.IqcCheckItemLibraries.AsNoTracking().SingleAsync(x => x.ItemId == id);
            Assert.Equal(IqcCheckKind.Measure, e.Kind);
            Assert.Equal(5, e.MeasureCount);
        }
    }

    [Fact]
    public async Task Hang_muc_dem_loi_duoc_chia_dung_theo_nhom_vat_lieu()
    {
        await using var db = _fx.NewContext();
        await SeedAsync(db);
        var byCat = await db.IqcCheckItemLibraries.AsNoTracking()
            .Where(x => x.Kind == IqcCheckKind.DefectCount)
            .GroupBy(x => x.Category)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

        // Con số đếm được trên chính file master — nếu ai thêm/bớt ô đếm lỗi
        // thì phải sửa cả đây, không để trôi.
        Assert.Equal(13, byCat[IqcMaterialCategory.Roll]);
        Assert.Equal(9, byCat[IqcMaterialCategory.Pcs]);
        Assert.Equal(5, byCat[IqcMaterialCategory.Tool]);
        // Chem là ba KHẲNG ĐỊNH đóng gói ("Không rách…"), không phải đếm lỗi.
        Assert.False(byCat.ContainsKey(IqcMaterialCategory.Chem));
    }

    [Fact]
    public async Task Hai_muc_ho_so_giay_KHONG_bi_coi_la_phep_do()
    {
        // RoHS/HSF là giấy tờ: không đo, không đếm. Xếp nhầm thành Measure thì
        // UI sẽ hiện 5 ô nhập số cho một tờ chứng chỉ.
        await using var db = _fx.NewContext();
        await SeedAsync(db);
        foreach (var id in new[] { "MT-01", "MT-02", "MT-03" })
        {
            var e = await db.IqcCheckItemLibraries.AsNoTracking().SingleAsync(x => x.ItemId == id);
            Assert.Equal(IqcCheckKind.Document, e.Kind);
            Assert.Equal(0, e.MeasureCount);
        }
    }

    [Fact]
    public async Task Hang_muc_CU_giu_nguyen_nhom_Any_de_khong_lam_mo_coi_spec_da_nhap()
    {
        // Import P13 bước 3 đã ghi 1231 hạng mục tiêu chuẩn vào KT-03/KT-04/
        // BD-01. Đổi Category của chúng sang một nhóm cụ thể là làm mồ côi
        // toàn bộ số đó.
        await using var db = _fx.NewContext();
        await SeedAsync(db);
        foreach (var id in new[] { "KT-03", "KT-04", "BD-01" })
        {
            var e = await db.IqcCheckItemLibraries.AsNoTracking().SingleAsync(x => x.ItemId == id);
            Assert.Equal(IqcMaterialCategory.Any, e.Category);
        }
    }
}
