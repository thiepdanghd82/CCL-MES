using System.Text;
using CCL.MES.Application.Services;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P12 bước 4 — hồ sơ HSF theo MÃ nguyên liệu.
///
/// <para>Khoá năm điều: (a) mã nào cũng có sẵn 5 dòng mặc định; (b) số hiệu +
/// ngày cấp + hạn là BẮT BUỘC; (c) "người sửa cuối" do SERVER đóng dấu, client
/// không khai được; (d) tên file chuẩn hoá <c>&lt;mã&gt;_&lt;loại&gt;.pdf</c> và
/// an toàn với mã có dấu cách / dấu <c>/</c>; (e) xoá là xoá MỀM, file giữ nguyên.</para>
/// </summary>
public sealed class IqcMaterialDocumentTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;
    private readonly string _blobRoot;

    public IqcMaterialDocumentTests()
    {
        _fx = new IsolatedDbFixture();
        _blobRoot = Path.Combine(Path.GetTempPath(), "iqcdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_blobRoot);
    }

    public void Dispose()
    {
        _fx.Dispose();
        try { Directory.Delete(_blobRoot, recursive: true); } catch { /* best effort */ }
    }

    private const string Qc = "qc-user";
    private InMemoryAuditWriter _audit = new();

    private IqcMaterialDocumentService Svc(MesDbContext db)
    {
        _audit = new InMemoryAuditWriter();
        var blobs = new CCL.MES.Infrastructure.Storage.FilesystemBlobStore(
            new CCL.MES.Infrastructure.Storage.BlobStoreOptions { DataDir = _blobRoot });
        return new IqcMaterialDocumentService(db, blobs, _audit);
    }

    private static Stream Pdf(string body = "%PDF-1.4 fake") =>
        new MemoryStream(Encoding.UTF8.GetBytes(body));

    // ── (a) 5 dòng mặc định ──────────────────────────────────────────────

    [Fact]
    public async Task Ma_moi_co_san_5_dong_mac_dinh()
    {
        // "Để mặc định có các dòng như hiện tại" — người kiểm không phải tự nhớ
        // là cần những tờ gì.
        await using var db = _fx.NewContext();

        var rows = await Svc(db).ListAsync("336T-AT1");

        Assert.Equal(5, rows.Count);
        Assert.Equal(new[] { "TDS", "MSDS", "ROHS", "REACH", "ISO9001" },
            rows.Select(r => r.DocType));
        Assert.All(rows, r => Assert.Null(r.StorageKey));   // chưa đính file nào
    }

    [Fact]
    public async Task Goi_lai_KHONG_nhan_doi_dong_mac_dinh()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);

        await svc.ListAsync("336T-AT1");
        await svc.ListAsync("336T-AT1");

        Assert.Equal(5, await db.IqcMaterialDocuments.CountAsync(x => x.MaterialCode == "336T-AT1"));
    }

    [Fact]
    public async Task Hai_ma_KHAC_nhau_co_bo_ho_so_RIENG()
    {
        // Hồ sơ gắn theo MÃ — mã khác thì bộ giấy khác.
        await using var db = _fx.NewContext();
        var svc = Svc(db);

        await svc.ListAsync("336T-AT1");
        await svc.ListAsync("336-H1a");

        Assert.Equal(5, await db.IqcMaterialDocuments.CountAsync(x => x.MaterialCode == "336T-AT1"));
        Assert.Equal(5, await db.IqcMaterialDocuments.CountAsync(x => x.MaterialCode == "336-H1a"));
    }

    // ── (b) ba trường bắt buộc ───────────────────────────────────────────

    [Theory]
    [InlineData(null, "iqc.doc_number_required")]
    [InlineData("", "iqc.doc_number_required")]
    [InlineData("   ", "iqc.doc_number_required")]
    public async Task Thieu_SO_HIEU_thi_khong_luu_duoc(string? no, string code)
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        var r = await svc.SaveRowAsync(id, no, DateTime.Today, DateTime.Today.AddYears(1), Qc, UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal(code, r.ErrorCode);
        Assert.Null(await db.IqcMaterialDocuments.Where(x => x.Id == id).Select(x => x.DocNumber).FirstAsync());
    }

    [Fact]
    public async Task Thieu_NGAY_CAP_hoac_HAN_thi_khong_luu_duoc()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        Assert.Equal("iqc.doc_issue_required",
            (await svc.SaveRowAsync(id, "TDS-1", null, DateTime.Today, Qc, UserRole.Qc)).ErrorCode);
        Assert.Equal("iqc.doc_expiry_required",
            (await svc.SaveRowAsync(id, "TDS-1", DateTime.Today, null, Qc, UserRole.Qc)).ErrorCode);
    }

    [Fact]
    public async Task Han_truoc_ngay_cap_thi_bi_tu_choi()
    {
        // Hạn trước ngày cấp là dữ liệu vô nghĩa; để lọt thì cột "còn hạn"
        // nói dối ngay từ lúc nhập.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        var r = await svc.SaveRowAsync(id, "TDS-1",
            DateTime.Today, DateTime.Today.AddDays(-1), Qc, UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal("iqc.doc_expiry_before_issue", r.ErrorCode);
    }

    // ── (c) người sửa cuối do SERVER đóng dấu ────────────────────────────

    [Fact]
    public async Task Nguoi_sua_cuoi_do_SERVER_dong_dau_va_co_vet_audit()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        var r = await svc.SaveRowAsync(id, "TDS-AB200-R4",
            new DateTime(2026, 1, 12), new DateTime(2027, 1, 12), "tran.bich.ngoc", UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        var row = await db.IqcMaterialDocuments.FirstAsync(x => x.Id == id);
        Assert.Equal("tran.bich.ngoc", row.UpdatedBy);   // client KHÔNG khai được
        Assert.NotNull(row.UpdatedAt);
        Assert.Contains(_audit.Rows, x => x.Action == AuditAction.IqcDocSet);
    }

    // ── (d) tên file chuẩn hoá + an toàn ─────────────────────────────────

    [Fact]
    public async Task Dinh_file_thi_TEN_duoc_chuan_hoa_theo_ma_va_loai()
    {
        // NCC gửi "scan001.pdf" thì sáu tháng sau không ai tra được.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First(r => r.DocType == "TDS").Id;

        var r = await svc.AttachFileAsync(id, Pdf(), "scan001.pdf", "application/pdf", Qc, UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        // TÊN TẢI VỀ là thứ người dùng thấy; KHOÁ LƯU mang thêm sha8 để không
        // ai đoán được khoá (guard #2 của blob store) và để giữ bản cũ khi
        // upload đè.
        Assert.Equal("336T-AT1_TDS.pdf", r.FileName);
        Assert.StartsWith("IQC/Documents/336T-AT1/336T-AT1_TDS_", r.StorageKey);
        Assert.EndsWith(".pdf", r.StorageKey);

        var row = await db.IqcMaterialDocuments.FirstAsync(x => x.Id == id);
        Assert.Equal("336T-AT1_TDS.pdf", row.FileName);
        Assert.NotNull(row.FileSha256);
        Assert.True(row.FileSizeBytes > 0);
        Assert.Contains(_audit.Rows, x => x.Action == AuditAction.IqcDocFileAttached);
    }

    [Theory]
    [InlineData("3M SP7533 (3KG / CAN)", "3M-SP7533-3KG-CAN")]
    [InlineData("090 VARNISH", "090-VARNISH")]
    [InlineData("336T-AT1", "336T-AT1")]
    public void Ma_co_dau_cach_va_dau_gach_cheo_thanh_ten_thu_muc_AN_TOAN(string code, string expect)
    {
        // 623/946 mã có dấu cách, 56 có dấu '/'. Đưa thẳng vào đường dẫn là
        // tạo cây thư mục ngoài ý muốn hoặc lỗi ghi file.
        Assert.Equal(expect, IqcMaterialDocumentService.SafeSegment(code));
    }

    [Fact]
    public async Task Ma_co_dau_gach_cheo_van_dinh_duoc_file()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("3M SP7533 (3KG / CAN)")).First(r => r.DocType == "MSDS").Id;

        var r = await svc.AttachFileAsync(id, Pdf(), "x.pdf", "application/pdf", Qc, UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.Equal("3M-SP7533-3KG-CAN_MSDS.pdf", r.FileName);
        // Chỉ MỘT tầng thư mục dưới IQC/Documents — dấu '/' trong mã KHÔNG
        // được đẻ ra thư mục con.
        Assert.StartsWith("IQC/Documents/3M-SP7533-3KG-CAN/", r.StorageKey);
        Assert.Equal(4, r.StorageKey!.Split('/').Length);
    }

    [Fact]
    public async Task Mo_lai_file_da_dinh()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First(r => r.DocType == "ROHS").Id;
        await svc.AttachFileAsync(id, Pdf("%PDF nội dung thật"), "a.pdf", "application/pdf", Qc, UserRole.Qc);

        var (content, name) = await svc.OpenFileAsync(id);

        Assert.NotNull(content);
        Assert.Equal("336T-AT1_ROHS.pdf", name);
        using var sr = new StreamReader(content!);
        Assert.Contains("nội dung thật", await sr.ReadToEndAsync());
    }

    [Fact]
    public async Task Dong_chua_dinh_file_thi_mo_ra_RONG_chu_khong_no()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        var (content, name) = await svc.OpenFileAsync(id);

        Assert.Null(content);
        Assert.Null(name);
    }

    // ── (e) thêm / xoá dòng ──────────────────────────────────────────────

    [Fact]
    public async Task Them_loai_ho_so_moi()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        await svc.ListAsync("336T-AT1");

        var r = await svc.AddRowAsync("336T-AT1", "CoA", "Giấy phân tích", null, Qc, UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        var row = await db.IqcMaterialDocuments.FirstAsync(x => x.Id == r.Id);
        Assert.Equal("COA", row.DocType);            // chuẩn hoá HOA
        Assert.Equal("Giấy phân tích", row.LabelVi);
    }

    [Fact]
    public async Task Them_TRUNG_loai_thi_bao_409()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        await svc.ListAsync("336T-AT1");

        var r = await svc.AddRowAsync("336T-AT1", "TDS", null, null, Qc, UserRole.Qc);

        Assert.False(r.Ok);
        Assert.Equal(409, r.HttpStatus);
    }

    [Fact]
    public async Task Xoa_la_xoa_MEM_va_FILE_van_con()
    {
        // Hồ sơ chất lượng đã từng có mặt thì không được biến mất không dấu vết.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First(r => r.DocType == "TDS").Id;
        await svc.AttachFileAsync(id, Pdf(), "a.pdf", "application/pdf", Qc, UserRole.Qc);

        var r = await svc.DeactivateRowAsync(id, Qc, UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        var row = await db.IqcMaterialDocuments.FirstAsync(x => x.Id == id);
        Assert.False(row.Active);
        Assert.NotNull(row.StorageKey);                       // file KHÔNG bị xoá
        Assert.NotNull((await svc.OpenFileAsync(id)).Content); // vẫn mở được
        Assert.Equal(4, (await svc.ListAsync("336T-AT1")).Count);
        Assert.Equal(5, (await svc.ListAsync("336T-AT1", includeInactive: true)).Count);
    }

    [Fact]
    public async Task Them_lai_loai_DA_GO_thi_bat_lai_dong_cu_chu_khong_bao_trung()
    {
        // Gõ lại đúng loại vừa gỡ nghĩa là muốn nó quay lại — kèm file cũ.
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First(r => r.DocType == "TDS").Id;
        await svc.AttachFileAsync(id, Pdf(), "a.pdf", "application/pdf", Qc, UserRole.Qc);
        await svc.DeactivateRowAsync(id, Qc, UserRole.Qc);

        var r = await svc.AddRowAsync("336T-AT1", "TDS", null, null, Qc, UserRole.Qc);

        Assert.True(r.Ok, r.ErrorCode);
        Assert.Equal(id, r.Id);                               // đúng dòng cũ
        var row = await db.IqcMaterialDocuments.FirstAsync(x => x.Id == id);
        Assert.True(row.Active);
        Assert.Equal("336T-AT1_TDS.pdf", row.FileName);       // file cũ còn nguyên
    }

    // ── phân quyền ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Qc)]
    [InlineData(UserRole.Engineer)]
    [InlineData(UserRole.Supervisor)]
    [InlineData(UserRole.Admin)]
    public async Task QC_tro_len_sua_duoc(string role)
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        Assert.True((await svc.SaveRowAsync(id, "N-1",
            DateTime.Today, DateTime.Today.AddYears(1), "u", role)).Ok);
    }

    [Fact]
    public async Task Operator_KHONG_sua_duoc()
    {
        await using var db = _fx.NewContext();
        var svc = Svc(db);
        var id = (await svc.ListAsync("336T-AT1")).First().Id;

        var save = await svc.SaveRowAsync(id, "N-1", DateTime.Today, DateTime.Today.AddYears(1), "op", UserRole.Operator);
        var attach = await svc.AttachFileAsync(id, Pdf(), "a.pdf", "application/pdf", "op", UserRole.Operator);
        var del = await svc.DeactivateRowAsync(id, "op", UserRole.Operator);

        Assert.Equal(403, save.HttpStatus);
        Assert.Equal(403, attach.HttpStatus);
        Assert.Equal(403, del.HttpStatus);
    }
}
