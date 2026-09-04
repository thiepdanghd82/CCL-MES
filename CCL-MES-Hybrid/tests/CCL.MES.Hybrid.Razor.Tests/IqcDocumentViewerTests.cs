using Bunit;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P12 bước 4b — trình xem PDF hồ sơ HSF, kiểm như một đơn vị riêng
/// (<c>Chrome=false</c>, đúng cách WindowManager host nó).
///
/// <para>Điểm cốt lõi mà bộ fixture này khoá: <b>xem được PDF KHÔNG cần Adobe
/// và không cần bất cứ app nào cài trên máy</b> — pdf.js đóng gói sẵn trong
/// app. Ba nút "mở bằng app ngoài" / "lưu về máy" chỉ là đường phụ; nếu một
/// ngày ai đó đổi bản xem trước sang `<iframe>` hay sang Launcher thì máy xưởng
/// không cài Acrobat sẽ mất khả năng đọc hồ sơ, và fixture phải đỏ trước.</para>
/// </summary>
public sealed class IqcDocumentViewerTests : TestContext
{
    private readonly RecordingFileOpener _opener = new();
    private readonly RecordingFileSaver _saver = new();
    private string _pdf = "";

    private void Wire()
    {
        Services.AddSingleton<IFileOpener>(_opener);
        Services.AddSingleton<IFileSaver>(_saver);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("cclMesDrawings.toObjectUrl", _ => true).SetResult("blob:fake");
    }

    /// <summary>Ghi ra một file thật — viewer đọc đĩa để dựng blob, nên không
    /// có file thì đang kiểm nhánh lỗi chứ không phải nhánh thường.</summary>
    private string WritePdf(long bytes = 2048)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccl-iqc-view-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _pdf = Path.Combine(dir, "336T-AT1_TDS.pdf");
        File.WriteAllBytes(_pdf, new byte[bytes]);
        return _pdf;
    }

    private IRenderedComponent<IqcDocumentViewer> Render(string? path = null) =>
        RenderComponent<IqcDocumentViewer>(p => p
            .Add(x => x.Chrome, false)
            .Add(x => x.LocalPath, path ?? _pdf)
            .Add(x => x.FileName, "336T-AT1_TDS.pdf")
            .Add(x => x.Subtitle, "336T-AT1"));

    // ── điểm cốt lõi: không cần Adobe ────────────────────────────────────

    [Fact]
    public void Dung_pdfjs_trong_app_KHONG_goi_app_ngoai_de_xem()
    {
        Wire(); WritePdf();
        var cut = Render();

        cut.WaitForElement("[data-testid=iqc-docview-host]");
        // Đây là cả luận điểm: máy trắng tinh, không Adobe, không cả mạng —
        // vẫn đọc được tờ giấy. Không một lời gọi nào ra app ngoài.
        Assert.Empty(_opener.Opened);
    }

    // ── công cụ xem ──────────────────────────────────────────────────────

    [Fact]
    public void Zoom_va_xoay_doi_trang_thai_hien_thi()
    {
        Wire(); WritePdf();
        var cut = Render();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        Assert.Equal("100%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);
        cut.Find("[data-testid=iqc-docview-zoomin]").Click();
        Assert.Equal("125%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);
        cut.Find("[data-testid=iqc-docview-zoomout]").Click();
        Assert.Equal("100%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);

        cut.Find("[data-testid=iqc-docview-zoomin]").Click();
        cut.Find("[data-testid=iqc-docview-reset]").Click();
        Assert.Equal("100%", cut.Find("[data-testid=iqc-docview-zoom]").TextContent);
    }

    // ── lưu về máy ───────────────────────────────────────────────────────

    [Fact]
    public void Luu_ve_may_goi_hop_thoai_he_dieu_hanh_va_bao_cho_da_luu()
    {
        Wire(); WritePdf();
        _saver.Next = () => SaveOutcome.Success("/Users/henry/Desktop/336T-AT1_TDS.pdf");
        var cut = Render();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        cut.Find("[data-testid=iqc-docview-save]").Click();

        var call = Assert.Single(_saver.Calls);
        Assert.Equal(_pdf, call.Source);
        // Tên gợi ý phải là tên NGƯỜI DÙNG thấy, không phải khoá lưu có sha8.
        Assert.Equal("336T-AT1_TDS.pdf", call.Suggested);
        Assert.Contains("Desktop", cut.Find("[data-testid=iqc-docview-notice]").TextContent);
    }

    [Fact]
    public void Bam_huy_hop_thoai_luu_thi_IM_LANG_chu_khong_bao_loi()
    {
        Wire(); WritePdf();
        _saver.Next = () => SaveOutcome.Cancelled;
        var cut = Render();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        cut.Find("[data-testid=iqc-docview-save]").Click();

        // Huỷ là lựa chọn hợp lệ của người dùng, không phải sự cố.
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-error]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-notice]"));
    }

    // ── mở bằng app ngoài ────────────────────────────────────────────────

    [Fact]
    public void Mo_bang_app_ngoai_giao_dung_duong_dan_sandbox()
    {
        Wire(); WritePdf();
        var cut = Render();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        cut.Find("[data-testid=iqc-docview-external]").Click();

        Assert.Equal(_pdf, _opener.Opened.Single());
    }

    [Fact]
    public void May_khong_co_app_doc_PDF_thi_chi_duong_sang_ban_xem_trong_app()
    {
        Wire(); WritePdf();
        _opener.CanOpen = false;
        var cut = Render();
        cut.WaitForElement("[data-testid=iqc-docview-host]");

        cut.Find("[data-testid=iqc-docview-external]").Click();

        // Không được im lặng, và cũng không được bỏ mặc người dùng: câu trả lời
        // phải chỉ ra rằng bản xem trong app vẫn dùng được.
        // WaitForElement: TryOpenAsync là async nên thông báo chỉ có sau vài
        // vòng render — Find() ngay chỉ xanh khi máy rảnh.
        var msg = cut.WaitForElement("[data-testid=iqc-docview-notice]").TextContent;
        Assert.Contains("Lưu về máy", msg);
        // Bản xem trước vẫn còn đó, không bị nhánh lỗi phá.
        Assert.NotNull(cut.Find("[data-testid=iqc-docview-host]"));
    }

    // ── biên ─────────────────────────────────────────────────────────────

    [Fact]
    public void File_qua_lon_thi_khong_dung_xem_truoc()
    {
        Wire(); WritePdf(13L * 1024 * 1024);   // trên ngưỡng 12 MB
        var cut = Render();

        var msg = cut.WaitForElement("[data-testid=iqc-docview-error]").TextContent;
        Assert.Contains("13", msg);
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-host]"));
    }

    [Fact]
    public void Ban_tai_ve_bi_mat_thi_bao_thang_chu_khong_treo_o_dang_mo()
    {
        Wire();
        var cut = Render("/khong/ton/tai/336T-AT1_TDS.pdf");

        var msg = cut.WaitForElement("[data-testid=iqc-docview-error]").TextContent;
        Assert.Contains("Không tìm thấy", msg);
    }

    [Fact]
    public void Helper_JS_tra_chuoi_rong_thi_KHONG_treo_mai_o_dang_mo()
    {
        // cclMesDrawings.toObjectUrl nuốt lỗi và trả "". Không kiểm thì cửa sổ
        // đứng vĩnh viễn ở "đang mở tài liệu…" mà không ai biết vì sao.
        Services.AddSingleton<IFileOpener>(_opener);
        Services.AddSingleton<IFileSaver>(_saver);
        Services.AddI18n();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("cclMesDrawings.toObjectUrl", _ => true).SetResult("");
        WritePdf();

        var cut = Render();

        Assert.NotNull(cut.WaitForElement("[data-testid=iqc-docview-error]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-docview-loading]"));
    }

    // ── test double ──────────────────────────────────────────────────────

    private sealed class RecordingFileOpener : IFileOpener
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "ccl-iqc-view-dl", Guid.NewGuid().ToString("N"));

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

    private sealed class RecordingFileSaver : IFileSaver
    {
        public Func<SaveOutcome>? Next { get; set; }
        public List<(string Source, string Suggested)> Calls { get; } = new();

        public Task<SaveOutcome> SaveAsync(
            string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
        {
            Calls.Add((sourceFilePath, suggestedFileName));
            return Task.FromResult(Next?.Invoke() ?? SaveOutcome.Cancelled);
        }
    }
}
