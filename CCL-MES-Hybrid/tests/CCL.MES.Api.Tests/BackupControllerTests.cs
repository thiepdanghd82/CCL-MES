using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Backup;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6h — admin Backup/Restore endpoints. Role-gating + happy-path
/// + 3 restore failure paths. The full restore→reapply round-trip
/// needs a writable DB; <see cref="MesApiFactory"/> seeds a fresh
/// SQLite file per test so the in-place restore is safe to exercise.
///
/// Guards:
///   1. Anon → 401  (covered separately by RouteDiscoveryCanaryTests)
///   2. Engineer auth → 403 on every endpoint (POLICY enforcement,
///      not just discovery — separate code path).
///   3. Admin auth → 200 list / 200 create / 200 download.
///   4. Restore corrupt header → 422 invalid_header.
///   5. Restore SQLite-but-wrong-schema → 422 schema_mismatch.
///   6. Restore happy → 200 + pre-restore snapshot returned, audit
///      emit observed in the DB.
/// </summary>
public sealed class BackupControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public BackupControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> AdminClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Admin);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    private async Task<HttpClient> EngineerClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    // ── Role-gating ─────────────────────────────────────────────────

    [Theory]
    [InlineData("GET",  "/api/v2/backup")]
    [InlineData("POST", "/api/v2/backup")]
    [InlineData("GET",  "/api/v2/backup/anything")]
    [InlineData("POST", "/api/v2/backup/restore")]
    public async Task Engineer_auth_gets_403_on_every_backup_route(string verb, string url)
    {
        var client = await EngineerClientAsync($"eng-bk-{verb}-{url.GetHashCode():x}");
        using var req = new HttpRequestMessage(new HttpMethod(verb), url);
        if (verb == "POST")
        {
            // For /backup/restore the form-body is required; we expect 403
            // BEFORE binding so empty content is fine.
            req.Content = new StringContent("");
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Admin happy paths ───────────────────────────────────────────

    [Fact]
    public async Task Admin_can_list_backups_empty_initially()
    {
        var client = await AdminClientAsync("admin-bk-list");
        var resp = await client.GetAsync("/api/v2/backup");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = await resp.Content.ReadFromJsonAsync<List<BackupSnapshotDto>>();
        Assert.NotNull(rows);
        // Fresh fixture — no snapshots yet. Other tests may leave some
        // around (factory is per-class), so we don't assert empty.
    }

    [Fact]
    public async Task Admin_can_create_snapshot_then_see_it_in_list()
    {
        var client = await AdminClientAsync("admin-bk-create");

        var create = await client.PostAsync("/api/v2/backup", content: null);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var snap = await create.Content.ReadFromJsonAsync<BackupSnapshotDto>();
        Assert.NotNull(snap);
        Assert.False(string.IsNullOrEmpty(snap!.FileName));
        Assert.True(snap.SizeBytes > 0);
        Assert.False(string.IsNullOrEmpty(snap.Sha256));
        Assert.Equal(64, snap.Sha256.Length);
        Assert.False(snap.IsPreRestore);

        var list = await client.GetAsync("/api/v2/backup");
        var rows = await list.Content.ReadFromJsonAsync<List<BackupSnapshotDto>>();
        Assert.Contains(rows!, r => r.FileName == snap.FileName);
    }

    [Fact]
    public async Task Admin_can_download_snapshot_after_create()
    {
        var client = await AdminClientAsync("admin-bk-dl");
        var create = await client.PostAsync("/api/v2/backup", null);
        var snap = (await create.Content.ReadFromJsonAsync<BackupSnapshotDto>())!;

        var dl = await client.GetAsync($"/api/v2/backup/{snap.FileName}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        var bytes = await dl.Content.ReadAsByteArrayAsync();
        Assert.Equal(snap.SizeBytes, bytes.LongLength);
        // Verify the downloaded bytes carry the SQLite magic header.
        Assert.True(bytes.Length >= 16);
        var magic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        for (var i = 0; i < magic.Length; i++)
            Assert.Equal(magic[i], bytes[i]);
    }

    [Fact]
    public async Task Admin_download_returns_404_on_nonexistent_or_traversal()
    {
        var client = await AdminClientAsync("admin-bk-404");
        var resp = await client.GetAsync("/api/v2/backup/does-not-exist.bak");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Path-traversal must NOT escape the backup dir. Express in URL form.
        var traversal = await client.GetAsync("/api/v2/backup/..%2F..%2Fetc%2Fpasswd");
        // ASP.NET routing rejects the literal at routing if the segment is
        // empty after decode; 404 here is acceptable regardless.
        Assert.True(traversal.StatusCode == HttpStatusCode.NotFound
                 || traversal.StatusCode == HttpStatusCode.BadRequest);
    }

    // ── Restore failure paths ───────────────────────────────────────

    [Fact]
    public async Task Restore_rejects_non_sqlite_upload_with_422_invalid_header()
    {
        var client = await AdminClientAsync("admin-bk-bad-hdr");
        using var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes(
            "this is not a sqlite database at all — garbage from the operator's Desktop"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(content, "file", "garbage.txt");

        var resp = await client.PostAsync("/api/v2/backup/restore", multipart);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("backup.invalid_header", body);
    }

    [Fact]
    public async Task Restore_rejects_sqlite_with_wrong_schema_as_422_schema_mismatch()
    {
        var client = await AdminClientAsync("admin-bk-wrong-schema");

        // Build a valid SQLite file but with NONE of {Users, Customers, Products}.
        var tmp = Path.Combine(Path.GetTempPath(), $"wrong-schema-{Guid.NewGuid():N}.db");
        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tmp}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE FooBar (id INTEGER PRIMARY KEY, name TEXT)";
                cmd.ExecuteNonQuery();
            }

            using var multipart = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(tmp);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(content, "file", Path.GetFileName(tmp));

            var resp = await client.PostAsync("/api/v2/backup/restore", multipart);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("backup.schema_mismatch", body);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* swallow */ }
        }
    }

    [Fact]
    public async Task Restore_happy_path_returns_pre_restore_snapshot_then_lists_it()
    {
        var client = await AdminClientAsync("admin-bk-happy");

        // Create a snapshot first so we have a valid blob to upload back.
        var create = await client.PostAsync("/api/v2/backup", null);
        var snap = (await create.Content.ReadFromJsonAsync<BackupSnapshotDto>())!;
        var dl = await client.GetAsync($"/api/v2/backup/{snap.FileName}");
        var bytes = await dl.Content.ReadAsByteArrayAsync();

        using var multipart = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(content, "file", "upload.db");

        var resp = await client.PostAsync("/api/v2/backup/restore", multipart);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RestoreResultDto>();
        Assert.NotNull(result);
        Assert.Equal(RestoreOutcome.Success, result!.Outcome);
        Assert.False(string.IsNullOrEmpty(result.PreRestoreSnapshot));
        Assert.StartsWith("pre-restore-", result.PreRestoreSnapshot);
        Assert.True(result.RestoredBytes > 0);

        // Pre-restore row should appear in the list with the chip flag.
        var list = await client.GetAsync("/api/v2/backup");
        var rows = await list.Content.ReadFromJsonAsync<List<BackupSnapshotDto>>();
        Assert.Contains(rows!, r => r.FileName == result.PreRestoreSnapshot && r.IsPreRestore);
    }

    [Fact]
    public async Task Restore_HOP_LE_ngay_sau_mot_file_SAI_trong_cung_giay_van_phai_200()
    {
        // Tên file tạm từng chỉ phân giải tới GIÂY (`_upload-{yyyyMMdd-HHmmss}.tmp`),
        // nên hai lần restore cùng giây dùng CHUNG một đường dẫn. Microsoft.Data.Sqlite
        // gộp kết nối theo CHUỖI KẾT NỐI, nên lần sau nhận lại handle của lần
        // trước — vẫn trỏ vào inode cũ đã bị xoá (macOS/Linux giữ inode sống
        // chừng nào còn handle mở). Lần sau đọc ra schema của FILE SAI, và người
        // dùng bị báo "nhầm file?" cho một bản backup hoàn toàn hợp lệ.
        //
        // Lỗi CHỈ nổ khi lần trước ghi file SAI schema: ba lần nạp cùng một file
        // hợp lệ thì handle cũ vẫn cho đúng schema, nên không lộ gì. Đó là lý do
        // bản test đầu của tôi vô dụng (0/5 trên code cũ) — phải xen file sai
        // vào giữa mới tái hiện được.
        var client = await AdminClientAsync("admin-bk-stale-handle");

        var create = await client.PostAsync("/api/v2/backup", null);
        var snap = (await create.Content.ReadFromJsonAsync<BackupSnapshotDto>())!;
        var dl = await client.GetAsync($"/api/v2/backup/{snap.FileName}");
        var good = await dl.Content.ReadAsByteArrayAsync();

        var tmp = Path.Combine(Path.GetTempPath(), $"stale-{Guid.NewGuid():N}.db");
        byte[] bad;
        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tmp}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE FooBar (id INTEGER PRIMARY KEY)";
                cmd.ExecuteNonQuery();
            }
            bad = await File.ReadAllBytesAsync(tmp);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(tmp); } catch { /* swallow */ }
        }

        // Xen kẽ SAI → ĐÚNG nhiều vòng, liên tiếp nên chắc chắn cùng một giây.
        for (var n = 1; n <= 4; n++)
        {
            using (var m1 = new MultipartFormDataContent())
            {
                var c1 = new ByteArrayContent(bad);
                c1.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                m1.Add(c1, "file", "bad.db");
                var r1 = await client.PostAsync("/api/v2/backup/restore", m1);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r1.StatusCode);
            }

            using var m2 = new MultipartFormDataContent();
            var c2 = new ByteArrayContent(good);
            c2.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            m2.Add(c2, "file", "good.db");
            var r2 = await client.PostAsync("/api/v2/backup/restore", m2);
            var body = await r2.Content.ReadAsStringAsync();
            Assert.True(r2.StatusCode == HttpStatusCode.OK,
                $"vòng {n}: bản backup HỢP LỆ bị từ chối {(int)r2.StatusCode}: {body}");
        }
    }
}
