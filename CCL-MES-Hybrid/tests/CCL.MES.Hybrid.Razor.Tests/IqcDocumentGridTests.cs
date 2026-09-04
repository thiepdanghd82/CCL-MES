using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P12 bước 4 — bảng hồ sơ HSF theo mã mẹ nguyên liệu.
///
/// <para>Bốn luật Henry chốt 2026-09-03, mỗi luật ít nhất một fixture:
/// (1) ba trường bắt buộc mới lưu được · (2) "người sửa cuối" do SERVER đóng
/// dấu, client không có ô nhập · (3) thêm/xoá được dòng · (4) nháy đúp ô "Tài
/// liệu" là mở file.</para>
///
/// <para>Bài học L64 còn nguyên giá trị ở đây: test xanh không chứng minh màn
/// hình đúng. Nên mỗi fixture bám <c>data-testid</c> mà người dùng thật sự
/// chạm vào, chứ không bám trạng thái nội bộ.</para>
/// </summary>
public sealed class IqcDocumentGridTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();
    private readonly RecordingFileOpener _opener = new();
    private readonly ScriptedFilePicker _picker = new();

    private const string Code = "336T-AT1";

    private void Wire(string role = "QC")
    {
        _session.SetUser(role.ToLowerInvariant() + "-user", role);
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddSingleton<IFilePickerService>(_picker);
        Services.AddSingleton<IFileOpener>(_opener);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized(role.ToLowerInvariant() + "-user");
    }

    private static IqcDocumentDto Doc(
        long id, string type, string? no = null,
        DateTime? issue = null, DateTime? expiry = null,
        string? file = null, string? by = null) => new()
    {
        Id = id,
        MaterialCode = Code,
        DocType = type,
        LabelVi = type + " — hồ sơ",
        LabelEn = type + " — document",
        DocNumber = no,
        IssueDate = issue,
        ExpiryDate = expiry,
        FileName = file,
        LastModifiedBy = by,
        LastModifiedAt = by is null ? null : new DateTime(2026, 8, 1, 9, 30, 0),
        Active = true,
    };

    private void Seed(params IqcDocumentDto[] docs) =>
        _api.IqcDocumentsImpl = (code, _) => Task.FromResult(
            new IqcDocumentListResponse { MaterialCode = code, Items = docs.ToList() });

    private IRenderedComponent<IqcDocumentGrid> Render(string? code = Code, bool editable = true) =>
        RenderComponent<IqcDocumentGrid>(p => p
            .Add(x => x.MaterialCode, code)
            .Add(x => x.Editable, editable));

    // ── không có mã thì không có thư mục ─────────────────────────────────

    [Fact]
    public void Chua_chon_nguyen_lieu_thi_bao_thang_chu_khong_ve_bang_rong()
    {
        Wire();
        var cut = Render(code: null);

        Assert.NotNull(cut.Find("[data-testid=iqc-doc-nomaterial]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-doc-table]"));
        // Quan trọng hơn cả: KHÔNG được gọi API. GET có tác dụng phụ ghi DB
        // (dựng 5 dòng mặc định) nên gọi bừa là đẻ rác cho mã rỗng.
        Assert.Empty(_api.IqcDocumentsCalls);
    }

    [Fact]
    public void Co_ma_thi_nap_dung_mot_lan_va_hien_duong_dan_thu_muc()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        Assert.Single(_api.IqcDocumentsCalls);
        Assert.Equal(Code, _api.IqcDocumentsCalls[0].MaterialCode);
        Assert.Contains(Code, cut.Find("[data-testid=iqc-doc-folder]").TextContent);
    }

    // ── LUẬT 1: ba trường bắt buộc ───────────────────────────────────────

    [Fact]
    public void Chua_sua_gi_thi_nut_luu_tat()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        Assert.True(cut.Find("[data-testid=iqc-doc-save]").HasAttribute("disabled"));
    }

    [Fact]
    public void Chi_nhap_so_hieu_ma_thieu_ngay_thi_van_KHONG_luu_duoc()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("TDS-AB200-R4");

        Assert.True(cut.Find("[data-testid=iqc-doc-save]").HasAttribute("disabled"));
        // Và phải NÓI ra thiếu gì — chặn im lặng là cách để người dùng bấm mãi.
        Assert.NotNull(cut.Find("[data-testid=iqc-doc-req-1]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-doc-savehint]"));
    }

    [Fact]
    public void Du_ca_ba_truong_thi_luu_duoc_va_gui_dung_than_yeu_cau()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("TDS-AB200-R4");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-01-12");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2027-01-12");

        var save = cut.Find("[data-testid=iqc-doc-save]");
        Assert.False(save.HasAttribute("disabled"));
        save.Click();

        var call = Assert.Single(_api.SaveIqcDocumentCalls);
        Assert.Equal(1, call.Id);
        Assert.Equal("TDS-AB200-R4", call.Body.DocNumber);
        Assert.Equal(new DateTime(2026, 1, 12), call.Body.IssueDate);
        Assert.Equal(new DateTime(2027, 1, 12), call.Body.ExpiryDate);
    }

    [Fact]
    public void Han_khong_duoc_truoc_hoac_bang_ngay_cap()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("X-1");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-05-05");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2026-05-05");

        // Client chặn TRƯỚC để khớp luật 422 của server — nếu chỉ dựa vào
        // server thì người dùng phải bấm mới biết mình sai.
        Assert.True(cut.Find("[data-testid=iqc-doc-save]").HasAttribute("disabled"));
        Assert.Empty(_api.SaveIqcDocumentCalls);
    }

    [Fact]
    public void Chi_luu_nhung_dong_da_sua_va_du_du_lieu()
    {
        Wire();
        Seed(Doc(1, "TDS"), Doc(2, "MSDS"), Doc(3, "ROHS", "R-9",
            new DateTime(2026, 2, 20), new DateTime(2027, 2, 20)));
        var cut = Render();

        // dòng 1 khai đủ · dòng 2 khai thiếu · dòng 3 không đụng tới
        cut.Find("[data-testid=iqc-doc-no-1]").Change("T-1");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2027-01-01");
        cut.Find("[data-testid=iqc-doc-no-2]").Change("M-1");

        cut.Find("[data-testid=iqc-doc-save]").Click();

        var call = Assert.Single(_api.SaveIqcDocumentCalls);
        Assert.Equal(1, call.Id);
    }

    // ── LUẬT 2: người sửa cuối do SERVER đóng dấu ────────────────────────

    [Fact]
    public void Nguoi_sua_cuoi_la_o_CHI_DOC_lay_tu_server()
    {
        Wire();
        Seed(Doc(1, "TDS", by: "tran.bich.ngoc"));
        var cut = Render();

        var cell = cut.Find("[data-testid=iqc-doc-by-1]");
        Assert.Contains("tran.bich.ngoc", cell.TextContent);
        // Không được có ô nhập trong ô này: client tự khai người sửa thì cột
        // đó thành lời khai, không còn là bằng chứng.
        Assert.Empty(cell.QuerySelectorAll("input"));
    }

    [Fact]
    public void Luu_xong_thi_nap_lai_de_lay_dau_cua_server()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("T-1");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2027-01-01");
        cut.Find("[data-testid=iqc-doc-save]").Click();

        // 1 lần lúc mount + 1 lần sau khi lưu.
        Assert.Equal(2, _api.IqcDocumentsCalls.Count);
    }

    // ── LUẬT 3: thêm / xoá dòng ──────────────────────────────────────────

    [Fact]
    public void Them_dong_gui_ma_nguyen_lieu_va_ma_loai()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-addrow]").Click();
        cut.Find("[data-testid=iqc-doc-addnew-type]").Change("ISO14001");
        cut.Find("[data-testid=iqc-doc-addnew-label]").Change("ISO 14001 — NCC");
        cut.Find("[data-testid=iqc-doc-addnew-save]").Click();

        var body = Assert.Single(_api.AddIqcDocumentCalls);
        Assert.Equal(Code, body.MaterialCode);
        Assert.Equal("ISO14001", body.DocType);
        Assert.Equal("ISO 14001 — NCC", body.LabelVi);
    }

    [Fact]
    public void Khong_go_ma_loai_thi_khong_them_duoc()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-addrow]").Click();
        Assert.True(cut.Find("[data-testid=iqc-doc-addnew-save]").HasAttribute("disabled"));
    }

    [Fact]
    public void Xoa_dong_qua_menu_chuot_phai_chu_khong_qua_cot_Actions()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        // L35: không có cột "Actions" với nút inline.
        Assert.Empty(cut.FindAll(".qms-remove"));

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        var remove = cut.FindAll(".row-ctx-menu button")
            .Single(b => b.TextContent.Contains("Gỡ dòng"));
        remove.Click();

        Assert.Equal(new[] { 1L }, _api.RemoveIqcDocumentCalls);
    }

    // ── LUẬT 4: nháy đúp ô Tài liệu = mở file ────────────────────────────

    [Fact]
    public void Nhay_dup_mo_CUA_SO_XEM_trong_app_chu_khong_bung_ra_Acrobat()
    {
        Wire();
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        JSInterop.Setup<string>("cclMesDrawings.toObjectUrl", _ => true).SetResult("blob:fake");
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-name-1]").DoubleClick();

        var dl = Assert.Single(_api.DownloadIqcDocumentCalls);
        Assert.Equal(1, dl.Id);
        // Phải nằm trong sandbox — Launcher của Catalyst từ chối IM LẶNG mọi
        // đường dẫn ngoài container, và bản xem trước cũng đọc từ đó.
        Assert.StartsWith(_opener.GetSafeDownloadDirectory(), dl.Destination);
        Assert.EndsWith(".pdf", dl.Destination);

        // Cửa sổ xem hiện lên. WaitForElement chứ không Find: viewer đọc file
        // rồi mới dựng blob nên host chỉ có sau vài vòng render — Find() ngay
        // là đọc DOM giữa chừng, và nó chỉ đỏ khi máy bận (chạy cả bộ test).
        cut.WaitForElement("[data-testid=iqc-docview-host]");
        // ...và KHÔNG bung ra app ngoài. Người dùng đang đọc số hiệu để gõ vào
        // bảng phía sau; nhảy sang Acrobat là che mất chính chỗ họ đang gõ.
        Assert.Empty(_opener.Opened);
    }

    [Fact]
    public void Nhay_dup_dong_chua_co_file_thi_khong_tai_gi_ca()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-name-1]").DoubleClick();

        Assert.Empty(_api.DownloadIqcDocumentCalls);
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-host]"));
        Assert.NotNull(cut.Find("[data-testid=iqc-doc-notice]"));
    }

    [Fact]
    public void Cua_so_xem_co_nut_zoom_xoay_va_mo_bang_app_ngoai()
    {
        Wire();
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        JSInterop.Setup<string>("cclMesDrawings.toObjectUrl", _ => true).SetResult("blob:fake");
        var cut = Render();
        cut.Find("[data-testid=iqc-doc-name-1]").DoubleClick();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        Assert.Equal("100%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);
        cut.Find("[data-testid=iqc-docview-zoomin]").Click();
        Assert.Equal("125%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);

        cut.Find("[data-testid=iqc-docview-external]").Click();
        Assert.Single(_opener.Opened);
    }

    [Fact]
    public void File_qua_lon_thi_khong_dung_xem_truoc_ma_chi_duong_sang_app_ngoai()
    {
        Wire();
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        // 13 MB — trên ngưỡng 12 MB. base64 qua cầu JS phình ~4/3 và nằm trọn
        // trong RAM webview; quá ngưỡng thì Acrobat mở nhanh hơn nhiều.
        _api.IqcDocumentDownloadSize = 13L * 1024 * 1024;
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-name-1]").DoubleClick();

        var msg = cut.WaitForElement("[data-testid=iqc-docview-error]").TextContent;
        Assert.Contains("13", msg);
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-host]"));
    }

    [Fact]
    public void Menu_co_duong_di_THANG_sang_Acrobat_cho_ai_quen_dung()
    {
        Wire();
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        cut.FindAll(".row-ctx-menu button")
            .Single(b => b.TextContent.Contains("Mở bằng app ngoài")).Click();

        Assert.Single(_api.DownloadIqcDocumentCalls);
        Assert.Single(_opener.Opened);
        // Đi thẳng ⇒ KHÔNG dựng cửa sổ xem trước.
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-host]"));
    }

    [Fact]
    public void Mo_bang_app_ngoai_that_bai_thi_bao_cho_luu_chu_khong_im_lang()
    {
        Wire();
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        _opener.CanOpen = false;   // Windows / host test: không có app xử lý
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        cut.FindAll(".row-ctx-menu button")
            .Single(b => b.TextContent.Contains("Mở bằng app ngoài")).Click();

        Assert.Contains(_opener.GetSafeDownloadDirectory(),
            cut.Find("[data-testid=iqc-doc-notice]").TextContent);
    }

    [Fact]
    public void Nhap_PDF_qua_menu_gui_nguyen_ban_len_server()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        _picker.Next = () => new PickedFile(
            "scan tu NCC (ban 3).pdf", 12, new MemoryStream(new byte[12]));
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        cut.FindAll(".row-ctx-menu button")
            .Single(b => b.TextContent.Contains("Nhập file PDF")).Click();

        var up = Assert.Single(_api.UploadIqcDocumentCalls);
        Assert.Equal(1, up.Id);
        // Tên NCC gửi đi NGUYÊN BẢN — server mới là nơi đổi thành
        // <mã>_<LOẠI>.pdf. Client tự đặt tên là hai nơi cùng quyết định một thứ.
        Assert.Equal("scan tu NCC (ban 3).pdf", up.FileName);
        Assert.Equal("application/pdf", up.ContentType);
    }

    [Fact]
    public void Nguoi_dung_bam_huy_chon_file_thi_khong_upload()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        _picker.Next = () => null;    // cancel ≠ lỗi
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        cut.FindAll(".row-ctx-menu button")
            .Single(b => b.TextContent.Contains("Nhập file PDF")).Click();

        Assert.Empty(_api.UploadIqcDocumentCalls);
        Assert.Empty(cut.FindAll("[data-testid=iqc-doc-error]"));
    }

    // ── RBAC-by-omission ─────────────────────────────────────────────────

    [Fact]
    public void Chi_duoc_xem_thi_khong_thay_them_khong_thay_xoa_khong_thay_nhap()
    {
        Wire("Operator");
        Seed(Doc(1, "TDS", file: "336T-AT1_TDS.pdf"));
        var cut = Render(editable: false);

        Assert.Empty(cut.FindAll("[data-testid=iqc-doc-addrow]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-doc-save]"));
        Assert.True(cut.Find("[data-testid=iqc-doc-no-1]").HasAttribute("disabled"));

        cut.Find("[data-testid=iqc-doc-row-1]").ContextMenu();
        var labels = cut.FindAll(".row-ctx-menu button").Select(b => b.TextContent).ToList();
        Assert.Contains(labels, l => l.Contains("Mở file"));
        Assert.DoesNotContain(labels, l => l.Contains("Nhập file PDF"));
        Assert.DoesNotContain(labels, l => l.Contains("Gỡ dòng"));
    }

    // ── trạng thái ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "qms-status-missing")]      // chưa khai đủ
    [InlineData("2020-01-01", "qms-status-expired")]
    [InlineData("2099-01-01", "qms-status-valid")]
    public void Trang_thai_phan_biet_du_thieu_va_het_han(string? expiry, string expected)
    {
        Wire();
        Seed(expiry is null
            ? Doc(1, "TDS")
            : Doc(1, "TDS", "N-1", new DateTime(2019, 1, 1), DateTime.Parse(expiry)));
        var cut = Render();

        Assert.Contains(expected, cut.Find("[data-testid=iqc-doc-status-1]").ClassName);
    }

    // ── lỗi server ───────────────────────────────────────────────────────

    [Fact]
    public void Server_tu_choi_thi_hien_cau_tieng_Viet_chu_khong_hien_ma_tho()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        _api.IqcDocumentWriteThrows = new ApiException(403,
            new ApiError { Code = "iqc.doc_edit_forbidden", MessageEn = "Forbidden." });
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("T-1");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2027-01-01");
        cut.Find("[data-testid=iqc-doc-save]").Click();

        var msg = cut.Find("[data-testid=iqc-doc-error]").TextContent;
        Assert.Contains("không có quyền", msg);
        Assert.DoesNotContain("iqc.doc_edit_forbidden", msg);
    }

    [Fact]
    public void Ma_loi_la_thi_hien_NGUYEN_TRANG_de_con_debug_duoc()
    {
        Wire();
        Seed(Doc(1, "TDS"));
        _api.IqcDocumentWriteThrows = new ApiException(500,
            new ApiError { Code = "iqc.doc_unknown_boom", MessageEn = "Boom." });
        var cut = Render();

        cut.Find("[data-testid=iqc-doc-no-1]").Change("T-1");
        cut.Find("[data-testid=iqc-doc-issue-1]").Change("2026-01-01");
        cut.Find("[data-testid=iqc-doc-expiry-1]").Change("2027-01-01");
        cut.Find("[data-testid=iqc-doc-save]").Click();

        var msg = cut.Find("[data-testid=iqc-doc-error]").TextContent;
        Assert.Contains("500", msg);
        Assert.Contains("iqc.doc_unknown_boom", msg);
    }

    // ── test double ──────────────────────────────────────────────────────

    private sealed class RecordingFileOpener : IFileOpener
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "ccl-iqc-doc-tests", Guid.NewGuid().ToString("N"));

        public List<string> Opened { get; } = new();
        public bool CanOpen { get; set; } = true;

        public Task<bool> TryOpenAsync(string absolutePath)
        {
            if (CanOpen) Opened.Add(absolutePath);
            return Task.FromResult(CanOpen);
        }

        public string GetSafeDownloadDirectory()
        {
            Directory.CreateDirectory(_dir);
            return _dir;
        }
    }

    /// <summary>Picker kịch bản — <c>StubFilePickerService</c> dùng chung luôn
    /// trả null (huỷ), nên không diễn được nhánh chọn được file.</summary>
    private sealed class ScriptedFilePicker : IFilePickerService
    {
        public Func<PickedFile?>? Next { get; set; }
        public List<IReadOnlyList<string>> AskedFor { get; } = new();

        public Task<PickedFile?> PickXlsxAsync(CancellationToken ct = default)
            => Task.FromResult(Next?.Invoke());

        public Task<PickedFile?> PickFileAsync(
            IReadOnlyList<string> allowedExtensions, CancellationToken ct = default)
        {
            AskedFor.Add(allowedExtensions);
            return Task.FromResult(Next?.Invoke());
        }
    }
}
