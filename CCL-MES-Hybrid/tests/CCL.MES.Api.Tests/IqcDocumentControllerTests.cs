using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Quality;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P12 bước 4 — nghiệm thu <c>/api/v2/iqc/documents</c> qua WIRE.
///
/// <para>Tầng thứ BA của phân quyền: service đã chặn
/// (<c>IqcMaterialDocumentTests</c>), UI đã ẩn affordance
/// (<c>IqcDocumentGridTests</c>) — ở đây khoá policy trên đường HTTP.</para>
///
/// <para>Hai điều mà chỉ test wire mới bắt được, và cả hai đều đã cắn dự án
/// này rồi: (a) mã nguyên liệu phải đi qua QUERY chứ không phải path segment —
/// 623/946 mã có dấu cách, Kestrel trả 400 TRƯỚC routing nên log server im
/// lặng; (b) tên part multipart phải đúng chữ <c>file</c>, sai một chữ là
/// <c>IFormFile</c> null và controller trả 422 chứ không phải lỗi bạn đang
/// tìm.</para>
/// </summary>
public sealed class IqcDocumentControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public IqcDocumentControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> ClientAsync(
        string user, string role, string? displayName = null)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", role, displayName);
        var c = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(c, user, "P@ss!1");
        return c;
    }

    private static HttpRequestMessage Mk(
        HttpMethod m, string path, object? body = null, bool idem = true)
    {
        var r = new HttpRequestMessage(m, path);
        if (body is not null)
            r.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return r;
    }

    private static string Url(string code, bool includeInactive = false) =>
        $"/api/v2/iqc/documents?materialCode={Uri.EscapeDataString(code)}"
        + (includeInactive ? "&includeInactive=true" : "");

    private static HttpRequestMessage Upload(
        long id, byte[] bytes, string fileName = "ncc-gui.pdf",
        string partName = "file", bool idem = true)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(content, partName, fileName);

        var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/iqc/documents/{id}/file")
        {
            Content = form,
        };
        if (idem) r.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return r;
    }

    private static async Task<IqcDocumentListResponse> ListAsync(HttpClient c, string code)
    {
        var resp = await c.GetAsync(Url(code));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IqcDocumentListResponse>())!;
    }

    // ── đọc ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Chua_dang_nhap_thi_401()
    {
        var c = _fx.CreateClient();
        var resp = await c.GetAsync(Url("ANON-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Lan_dau_cham_mot_ma_thi_server_dung_san_bo_ho_so_mac_dinh()
    {
        var c = await ClientAsync("iqcdoc-seed", UserRole.Qc);
        var list = await ListAsync(c, "DOC-SEED-1");

        Assert.Equal("DOC-SEED-1", list.MaterialCode);
        Assert.Equal(5, list.Items.Count);
        Assert.Equal(
            new[] { "TDS", "MSDS", "ROHS", "REACH", "ISO9001" },
            list.Items.Select(x => x.DocType).ToArray());
        // Dựng sẵn nhưng CHƯA khai — cả 5 dòng phải rỗng, không bịa số hiệu.
        Assert.All(list.Items, x => Assert.False(x.IsComplete));
    }

    [Fact]
    public async Task Cham_lai_lan_hai_khong_de_them_dong_nao()
    {
        var c = await ClientAsync("iqcdoc-idem", UserRole.Qc);
        var first = await ListAsync(c, "DOC-IDEM-1");
        var again = await ListAsync(c, "DOC-IDEM-1");

        Assert.Equal(first.Items.Count, again.Items.Count);
        Assert.Equal(
            first.Items.Select(x => x.Id).OrderBy(x => x),
            again.Items.Select(x => x.Id).OrderBy(x => x));
    }

    [Theory]
    [InlineData("MÃ CÓ DẤU CÁCH", "space")]
    [InlineData("A/B-01", "slash")]
    [InlineData("336T-AT1", "plain")]
    public async Task Ma_nguyen_lieu_hinh_dang_nao_cung_qua_duoc_vi_di_o_QUERY(
        string code, string tag)
    {
        // Bài học đã trả tiền: để mã trong path segment thì Kestrel từ chối
        // TRƯỚC routing → 400, và log server không ghi gì cả.
        // Tên user phải KHÁC nhau từng case — dùng chung một tên thì case thứ
        // hai chết ở UNIQUE constraint Users, không phải ở thứ đang kiểm.
        var c = await ClientAsync("iqcdoc-shape-" + tag, UserRole.Qc);
        var resp = await c.GetAsync(Url(code));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<IqcDocumentListResponse>();
        Assert.Equal(code, list!.MaterialCode);
    }

    [Fact]
    public async Task Khoa_luu_tren_server_KHONG_lo_ra_client()
    {
        var c = await ClientAsync("iqcdoc-nokey", UserRole.Qc);
        var resp = await c.GetAsync(Url("DOC-NOKEY-1"));
        var raw = await resp.Content.ReadAsStringAsync();

        // StorageKey là chi tiết bố trí đĩa của server. Lộ ra là mời người ta
        // đoán đường dẫn của mã khác.
        Assert.DoesNotContain("storageKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IQC/Documents", raw, StringComparison.Ordinal);
    }

    // ── phân quyền GHI ───────────────────────────────────────────────────

    [Fact]
    public async Task Operator_khong_duoc_sua_ho_so()
    {
        var qc = await ClientAsync("iqcdoc-rbac-qc", UserRole.Qc);
        var id = (await ListAsync(qc, "DOC-RBAC-1")).Items[0].Id;

        var op = await ClientAsync("iqcdoc-rbac-op", UserRole.Operator);
        var resp = await op.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "X-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" }));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task QC_duoc_sua_vi_QC_la_nguoi_cam_giay_cua_NCC()
    {
        var c = await ClientAsync("iqcdoc-rbac-qc2", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-RBAC-2")).Items[0].Id;

        var resp = await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "X-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" }));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Thieu_Idempotency_Key_thi_400()
    {
        var c = await ClientAsync("iqcdoc-noidem", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-NOIDEM-1")).Items[0].Id;

        var resp = await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "X-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" },
            idem: false));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── ba trường bắt buộc, kiểm ở SERVER ────────────────────────────────

    [Theory]
    [InlineData(null, "2026-01-01", "2027-01-01", "iqc.doc_number_required")]
    [InlineData("X-1", null, "2027-01-01", "iqc.doc_issue_required")]
    [InlineData("X-1", "2026-01-01", null, "iqc.doc_expiry_required")]
    [InlineData("X-1", "2027-01-01", "2026-01-01", "iqc.doc_expiry_before_issue")]
    public async Task Thieu_bat_ky_truong_nao_deu_422_kem_ma_loi(
        string? no, string? issue, string? expiry, string expectedCode)
    {
        // Client đã chặn trước, nhưng UI không phải là bảo mật — curl vẫn tới
        // thẳng được endpoint này.
        var c = await ClientAsync(
            "iqcdoc-req-" + expectedCode.Replace("iqc.doc_", "").Replace("_", "-"), UserRole.Qc);
        var id = (await ListAsync(c, "DOC-REQ-" + expectedCode)).Items[0].Id;

        var resp = await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = no, IssueDate = issue, ExpiryDate = expiry }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(expectedCode, err!.Code);
    }

    [Fact]
    public async Task Luu_xong_thi_server_dong_dau_nguoi_sua_theo_TOKEN()
    {
        var c = await ClientAsync("iqcdoc-stamp", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-STAMP-1")).Items[0].Id;

        await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "S-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" }));

        var row = (await ListAsync(c, "DOC-STAMP-1")).Items.Single(x => x.Id == id);
        // Không có đường nào cho client tự khai tên mình — đây là bằng chứng,
        // không phải lời khai.
        Assert.Equal("iqcdoc-stamp", row.LastModifiedBy);
        Assert.NotNull(row.LastModifiedAt);
    }

    [Fact]
    public async Task Cot_nguoi_sua_cuoi_tra_TEN_NGUOI_chu_khong_tra_ten_dang_nhap()
    {
        var c = await ClientAsync("iqcdoc-name", UserRole.Qc, "Đặng Thế Thiệp");
        var id = (await ListAsync(c, "DOC-NAME-1")).Items[0].Id;

        await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "N-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" }));

        var row = (await ListAsync(c, "DOC-NAME-1")).Items.Single(x => x.Id == id);

        // Bảng vẫn lưu USERNAME — định danh ổn định, đối chiếu được AuditLogs.
        Assert.Equal("iqcdoc-name", row.LastModifiedBy);
        // Nhưng thứ ĐEM HIỆN phải là tên người.
        Assert.Equal("Đặng Thế Thiệp", row.LastModifiedByDisplay);
        Assert.Equal("Đặng Thế Thiệp", row.LastModifiedByLabel);
    }

    [Fact]
    public async Task Tai_khoan_khong_co_ten_hien_thi_thi_lui_ve_username_chu_khong_bo_trong()
    {
        var c = await ClientAsync("iqcdoc-noname", UserRole.Qc, displayName: "");
        var id = (await ListAsync(c, "DOC-NONAME-1")).Items[0].Id;

        await c.SendAsync(Mk(HttpMethod.Put, $"/api/v2/iqc/documents/{id}",
            new { DocNumber = "N-1", IssueDate = "2026-01-01", ExpiryDate = "2027-01-01" }));

        var row = (await ListAsync(c, "DOC-NONAME-1")).Items.Single(x => x.Id == id);

        Assert.Null(row.LastModifiedByDisplay);
        // Mất dấu người làm còn tệ hơn hiện một cái tên xấu.
        Assert.Equal("iqcdoc-noname", row.LastModifiedByLabel);
    }

    // ── thêm / xoá ───────────────────────────────────────────────────────

    [Fact]
    public async Task Them_loai_moi_roi_go_di_thi_bien_khoi_danh_sach_nhung_van_con_trong_DB()
    {
        var c = await ClientAsync("iqcdoc-addrm", UserRole.Qc);
        await ListAsync(c, "DOC-ADDRM-1");

        var add = await c.SendAsync(Mk(HttpMethod.Post, "/api/v2/iqc/documents",
            new { MaterialCode = "DOC-ADDRM-1", DocType = "ISO14001", LabelVi = "ISO 14001" }));
        Assert.True(add.IsSuccessStatusCode);

        var afterAdd = await ListAsync(c, "DOC-ADDRM-1");
        var row = afterAdd.Items.Single(x => x.DocType == "ISO14001");

        var del = await c.SendAsync(Mk(HttpMethod.Delete, $"/api/v2/iqc/documents/{row.Id}"));
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        Assert.DoesNotContain((await ListAsync(c, "DOC-ADDRM-1")).Items, x => x.Id == row.Id);
        // Xoá MỀM: hỏi kèm cờ thì vẫn thấy — hồ sơ chất lượng không bốc hơi.
        var resp = await c.GetAsync(Url("DOC-ADDRM-1", includeInactive: true));
        var all = await resp.Content.ReadFromJsonAsync<IqcDocumentListResponse>();
        Assert.Contains(all!.Items, x => x.Id == row.Id && !x.Active);
    }

    [Fact]
    public async Task Them_trung_loai_thi_422()
    {
        var c = await ClientAsync("iqcdoc-dup", UserRole.Qc);
        await ListAsync(c, "DOC-DUP-1");

        var resp = await c.SendAsync(Mk(HttpMethod.Post, "/api/v2/iqc/documents",
            new { MaterialCode = "DOC-DUP-1", DocType = "TDS" }));

        // 409 chứ không 422: dòng đó ĐANG TỒN TẠI, đây là xung đột tài nguyên
        // chứ không phải thân yêu cầu sai.
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.doc_type_duplicate", err!.Code);
    }

    [Fact]
    public async Task Them_lai_loai_da_go_thi_BAT_lai_dong_cu_chu_khong_de_dong_moi()
    {
        var c = await ClientAsync("iqcdoc-revive", UserRole.Qc);
        var seeded = await ListAsync(c, "DOC-REVIVE-1");
        var tds = seeded.Items.Single(x => x.DocType == "TDS");

        await c.SendAsync(Mk(HttpMethod.Delete, $"/api/v2/iqc/documents/{tds.Id}"));
        await c.SendAsync(Mk(HttpMethod.Post, "/api/v2/iqc/documents",
            new { MaterialCode = "DOC-REVIVE-1", DocType = "TDS" }));

        var back = await ListAsync(c, "DOC-REVIVE-1");
        var revived = back.Items.Single(x => x.DocType == "TDS");
        // Cùng Id ⇒ file PDF đã đính trước đó vẫn còn treo trên dòng này.
        Assert.Equal(tds.Id, revived.Id);
    }

    // ── file ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tai_len_PDF_thi_server_DAT_LAI_TEN_theo_ma_va_loai()
    {
        var c = await ClientAsync("iqcdoc-upload", UserRole.Qc);
        var id = (await ListAsync(c, "336T-AT1")).Items.Single(x => x.DocType == "TDS").Id;

        var resp = await c.SendAsync(Upload(id, Encoding.ASCII.GetBytes("%PDF-1.4 fake"),
            fileName: "scan tu NCC (ban 3).pdf"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var row = (await ListAsync(c, "336T-AT1")).Items.Single(x => x.Id == id);
        // Đây chính là yêu cầu của Henry: 336T-AT1_TDS.pdf, bất kể NCC gửi tên gì.
        Assert.Equal("336T-AT1_TDS.pdf", row.FileName);
        Assert.True(row.FileSizeBytes > 0);
    }

    [Fact]
    public async Task Tai_ve_tra_dung_byte_da_gui_va_dung_ten_nguoi_dung_thay()
    {
        var c = await ClientAsync("iqcdoc-download", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-DL-1")).Items.Single(x => x.DocType == "MSDS").Id;
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4 round-trip payload");

        await c.SendAsync(Upload(id, bytes));
        var resp = await c.GetAsync($"/api/v2/iqc/documents/{id}/file");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("DOC-DL-1_MSDS.pdf",
            resp.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(bytes, await resp.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Dong_chua_dinh_file_thi_tra_404_chu_khong_tra_file_rong()
    {
        var c = await ClientAsync("iqcdoc-nofile", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-NOFILE-1")).Items[0].Id;

        var resp = await c.GetAsync($"/api/v2/iqc/documents/{id}/file");

        // Client dựa đúng vào 404 này để không mở một cửa sổ trống.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.doc_file_missing", err!.Code);
    }

    [Fact]
    public async Task Sai_ten_part_multipart_thi_422_chu_khong_500()
    {
        var c = await ClientAsync("iqcdoc-partname", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-PART-1")).Items[0].Id;

        // Tên part phải đúng chữ "file" — khớp tham số IFormFile file.
        var resp = await c.SendAsync(Upload(id, new byte[8], partName: "document"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.doc_empty_upload", err!.Code);
    }

    [Fact]
    public async Task File_rong_thi_422()
    {
        var c = await ClientAsync("iqcdoc-empty", UserRole.Qc);
        var id = (await ListAsync(c, "DOC-EMPTY-1")).Items[0].Id;

        var resp = await c.SendAsync(Upload(id, Array.Empty<byte>()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("iqc.doc_empty_upload", err!.Code);
    }

    [Fact]
    public async Task Operator_khong_duoc_tai_file_len()
    {
        var qc = await ClientAsync("iqcdoc-upload-qc", UserRole.Qc);
        var id = (await ListAsync(qc, "DOC-UPRBAC-1")).Items[0].Id;

        var op = await ClientAsync("iqcdoc-upload-op", UserRole.Operator);
        var resp = await op.SendAsync(Upload(id, Encoding.ASCII.GetBytes("%PDF")));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Operator_khong_doc_duoc_ho_so_dung_nhu_quyet_dinh_QC_tro_len()
    {
        // Henry chốt 2026-09-03: hồ sơ HSF là "QC trở lên" — cả ĐỌC lẫn GHI.
        // Nếu sau này muốn cho operator xem để biết lô hàng có giấy hay không
        // thì đó là một quyết định MỚI, phải sửa policy chứ không sửa test.
        var op = await ClientAsync("iqcdoc-read-op", UserRole.Operator);
        var resp = await op.GetAsync(Url("DOC-READ-OP-1"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
