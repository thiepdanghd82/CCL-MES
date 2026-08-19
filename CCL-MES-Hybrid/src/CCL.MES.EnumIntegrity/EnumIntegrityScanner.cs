using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CCL.MES.EnumIntegrity;

/// <summary>
/// Quét tính toàn vẹn của MỌI cột enum-lưu-dạng-chuỗi trong EF model.
///
/// Sự cố 2026-08-19: <c>WorkOrders.CurrentStep='Done'</c> × 11 — giá trị không
/// tồn tại trong <c>ProcessStepCode</c>. <c>MesDbContext.cs:89</c> cấu hình
/// <c>HasConversion&lt;string&gt;()</c>; chiều ĐỌC ném
/// <c>InvalidOperationException</c> trong shaper của EF ⇒ mọi truy vấn
/// materialise entity <c>WorkOrder</c> đều chết, 10 route API hỏng, route DANH
/// SÁCH làm mất toàn bộ 27 WO cho mọi người dùng. Rác vào bằng SQL trực tiếp,
/// ngoài đường có audit của app — nên CI không bao giờ thấy.
///
/// Hai luật thiết kế, cả hai đều bắt buộc:
///
///   1. KHÔNG HARD-CODE danh sách enum. Cột được khám phá bằng reflection trên
///      <see cref="IModel"/> đã finalize, nên enum thêm về sau tự động được
///      canh mà không ai phải nhớ sửa gate.
///
///   2. KHÔNG TỰ MÔ PHỎNG ngữ nghĩa converter — mà GỌI CHÍNH converter EF đang
///      dùng lúc chạy (<see cref="ValueConverter.ConvertFromProvider"/>). Tự
///      viết lại luật parse là cách chắc chắn nhất để đẻ ra báo động giả: đo
///      thực tế cho thấy <c>'closed'</c>, <c>'CLOSED'</c>, <c>'8'</c> EF map
///      được và KHÔNG được coi là vi phạm. Gọi thẳng converter thì đúng theo
///      định nghĩa, không theo trí nhớ.
///
/// Bắt HAI hạng vi phạm:
///   • <see cref="EnumViolationKind.Throws"/>    — converter ném (hạng 'Done').
///   • <see cref="EnumViolationKind.Undefined"/> — converter KHÔNG ném nhưng ra
///     giá trị không định nghĩa (<c>''</c>, <c>'0'</c>). Hạng IM LẶNG này nguy
///     hiểm hơn vì không có 500 nào để ai đó đi điều tra.
/// </summary>
public static class EnumIntegrityScanner
{
    /// <summary>
    /// Khám phá mọi cột enum-lưu-chuỗi bằng reflection trên EF model.
    /// Dedupe theo (bảng, cột) — TPH/owned type dùng chung bảng.
    /// </summary>
    public static IReadOnlyList<EnumStringColumn> DiscoverColumns(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var found = new Dictionary<string, EnumStringColumn>(StringComparer.Ordinal);

        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;      // view/keyless/TPC-less
            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var converter = ResolveConverter(property);
                if (converter is null) continue;
                if (converter.ProviderClrType != typeof(string)) continue;

                var enumType = Nullable.GetUnderlyingType(converter.ModelClrType) ?? converter.ModelClrType;
                if (!enumType.IsEnum) continue;

                var column = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(column)) continue;

                var key = table + "." + column;
                if (found.ContainsKey(key)) continue;
                found[key] = new EnumStringColumn(table, column, enumType, entityType.ShortName());
            }
        }

        return found.Values
            .OrderBy(c => c.Table, StringComparer.Ordinal)
            .ThenBy(c => c.Column, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Khám phá cột mà KHÔNG cần file DB nào — model EF được dựng từ mã, không
    /// từ dữ liệu. Dùng để in danh sách cột đang được canh.
    /// </summary>
    public static IReadOnlyList<EnumStringColumn> DiscoverColumns()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.MesDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var ctx = new Infrastructure.MesDbContext(options);
        return DiscoverColumns(ctx.Model);
    }

    /// <summary>
    /// Lấy converter EF thật sự dùng cho property. Ưu tiên converter cấu hình
    /// tường minh (<c>HasConversion&lt;string&gt;()</c>), sau đó tới converter
    /// của type mapping (đường quy ước).
    /// </summary>
    private static ValueConverter? ResolveConverter(IProperty property)
    {
        var explicitConverter = property.GetValueConverter();
        if (explicitConverter is not null) return explicitConverter;

        try { return property.GetTypeMapping().Converter; }
        catch (InvalidOperationException) { return null; }   // property chưa có mapping
    }

    /// <summary>
    /// Quét dùng EF model + converter của một <see cref="DbContext"/> có sẵn
    /// (tầng 1 test và tầng 3 preflight).
    ///
    /// CHỈ ĐỌC, và với SQLite là chỉ đọc ở TẦNG DRIVER: phép quét mở KẾT NỐI
    /// RIÊNG <c>Mode=ReadOnly;Pooling=False</c> tới cùng file, chứ KHÔNG mượn
    /// kết nối của <paramref name="context"/>. Ba lý do, cả ba đều đã trả giá:
    ///
    ///   • DB sản xuất chạy <c>journal_mode=wal</c> và có API thật đang phục vụ
    ///     trên chính file đó. Kết nối chỉ-đọc không thể lấy khoá ghi, nên
    ///     preflight không bao giờ chen vào đường ghi của nhà máy.
    ///   • Mượn kết nối của DbContext là chen vào bộ đếm mở/đóng của
    ///     RelationalConnection và vào pool dùng chung. Đo được: bản đầu tiên
    ///     của file này làm 5 test soak concurrency (N=10/N=50) đổ với
    ///     "SQLite Error 5: database is locked".
    ///   • Kết nối riêng thì không kế thừa transaction đang mở của context —
    ///     phép quét không bao giờ đọc dữ liệu chưa commit.
    ///
    /// Provider khác (SqlServer) quay về dùng kết nối của context qua đúng API
    /// của EF; ở đó không có ràng buộc một-người-ghi của SQLite.
    /// </summary>
    public static async Task<EnumIntegrityResult> ScanAsync(
        DbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var columns = DiscoverColumns(context.Model);
        var provider = context.Database.ProviderName;

        var readOnlyPath = TryGetSqliteFilePath(context);
        if (readOnlyPath is not null)
        {
            await using var ro = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={readOnlyPath};Mode=ReadOnly;Pooling=False");
            await ro.OpenAsync(ct).ConfigureAwait(false);
            var existingRo = await ReadCatalogAsync(ro, provider, ct).ConfigureAwait(false);
            return await ScanCoreAsync(context.Model, ro, columns, existingRo, ct)
                .ConfigureAwait(false);
        }

        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var connection = context.Database.GetDbConnection();
            var existing = await ReadCatalogAsync(connection, provider, ct).ConfigureAwait(false);
            return await ScanCoreAsync(context.Model, connection, columns, existing, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Đường dẫn file SQLite của context, hoặc null nếu không phải SQLite trên
    /// đĩa (in-memory, provider khác, chuỗi kết nối không đọc được). Null ⇒
    /// phía gọi quay về dùng kết nối của context.
    /// </summary>
    private static string? TryGetSqliteFilePath(DbContext context)
    {
        var provider = context.Database.ProviderName;
        if (provider is null || !provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var raw = context.Database.GetDbConnection().ConnectionString;
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(raw);
            var source = builder.DataSource;
            if (string.IsNullOrEmpty(source)) return null;
            if (source.Equals(":memory:", StringComparison.OrdinalIgnoreCase)) return null;
            if (builder.Mode == Microsoft.Data.Sqlite.SqliteOpenMode.Memory) return null;
            var full = Path.GetFullPath(source);
            return File.Exists(full) ? full : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Danh mục (bảng, cột) THẬT SỰ tồn tại, lấy bằng MỘT truy vấn.
    ///
    /// Vì sao không cứ bắn 37 câu rồi bắt ngoại lệ: trên một DB chưa migrate
    /// (fixture test lúc host vừa dựng, DB trắng của bản clone mới) cả 37 câu
    /// đều hỏng, và cơn bão ngoại lệ đó để lại kết nối ở trạng thái xấu. Trả
    /// giá đo được: 5 test soak concurrency (N=10 / N=50) đổ với
    /// "SQLite Error 5: database is locked" — chuỗi lỗi do preflight lúc boot
    /// gây ra, chứ không phải do code nghiệp vụ. Một câu tra danh mục vừa đúng
    /// vừa nhanh hơn, và biến "bảng chưa có" từ NGOẠI LỆ thành DỮ KIỆN.
    ///
    /// Trả null nếu không đọc được danh mục ⇒ phía sau quay về lối cũ (thử rồi
    /// bắt), để một provider lạ không làm gate câm.
    /// </summary>
    private static async Task<HashSet<string>?> ReadCatalogAsync(
        DbConnection connection, string? providerName, CancellationToken ct)
    {
        var sql = providerName switch
        {
            not null when providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) =>
                "SELECT m.name, p.name FROM sqlite_master m JOIN pragma_table_info(m.name) p "
                + "WHERE m.type IN ('table','view')",
            not null when providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) =>
                "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS",
            _ => null,
        };
        if (sql is null) return null;

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                set.Add(reader.GetString(0) + "." + reader.GetString(1));
            return set;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Quét một file SQLite bất kỳ, mở CHẾ ĐỘ CHỈ ĐỌC (dùng cho tầng 2 gate:
    /// DB fixture, snapshot live, backup tiền-sửa). <c>Mode=ReadOnly</c> là lớp
    /// bảo hiểm ở tầng driver cho luật "KHÔNG sửa dữ liệu".
    /// </summary>
    public static async Task<EnumIntegrityResult> ScanSqliteFileAsync(
        string dbPath, CancellationToken ct = default)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Không thấy file DB: {dbPath}", dbPath);

        var full = Path.GetFullPath(dbPath);
        var options = new DbContextOptionsBuilder<Infrastructure.MesDbContext>()
            .UseSqlite($"Data Source={full};Mode=ReadOnly;Pooling=False")
            .Options;

        await using var ctx = new Infrastructure.MesDbContext(options);
        return await ScanAsync(ctx, ct).ConfigureAwait(false);
    }

    private static async Task<EnumIntegrityResult> ScanCoreAsync(
        IModel model,
        DbConnection connection,
        IReadOnlyList<EnumStringColumn> columns,
        HashSet<string>? existingColumns,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var violations = new List<EnumViolation>();
        var skipped = new List<EnumColumnSkip>();
        var scanned = 0;
        long distinctChecked = 0;

        // Converter tra theo (bảng, cột) — lấy đúng instance EF dùng lúc chạy.
        var converters = BuildConverterMap(model);

        foreach (var col in columns)
        {
            ct.ThrowIfCancellationRequested();

            if (!converters.TryGetValue(col.Key, out var converter))
            {
                skipped.Add(new EnumColumnSkip(col.Table, col.Column, "không tra được converter"));
                continue;
            }

            // Bảng/cột chưa tồn tại (DB lạc hậu migration, fixture chưa dựng)
            // là DỮ KIỆN, không phải ngoại lệ. Bỏ qua tại đây thì không câu SQL
            // nào phải hỏng, và kết nối không bị bỏ lại ở trạng thái xấu.
            if (existingColumns is not null && !existingColumns.Contains(col.Key))
            {
                skipped.Add(new EnumColumnSkip(col.Table, col.Column, "bảng/cột chưa tồn tại"));
                continue;
            }

            List<(string Value, long Count)> distinct;
            try
            {
                distinct = await ReadDistinctAsync(connection, col, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bảng/cột chưa tồn tại (DB lạc hậu migration, fixture rỗng, DB
                // test chưa migrate) — KHÔNG phải vi phạm. Ghi nhận để phía gọi
                // thấy "scanned 0/N" là điều kiện KHÔNG KẾT LUẬN ĐƯỢC, chứ
                // không phải PASS.
                skipped.Add(new EnumColumnSkip(col.Table, col.Column, Shorten(ex.Message)));
                continue;
            }

            scanned++;
            distinctChecked += distinct.Count;

            foreach (var (value, count) in distinct)
            {
                var verdict = Classify(converter, col.EnumType, value);
                if (verdict is null) continue;
                violations.Add(new EnumViolation(
                    col.Table, col.Column, col.EnumType.Name,
                    value, count, verdict.Value.Kind, verdict.Value.Detail));
            }
        }

        violations = violations
            .OrderByDescending(v => v.RowCount)
            .ThenBy(v => v.Table, StringComparer.Ordinal)
            .ThenBy(v => v.Column, StringComparer.Ordinal)
            .ToList();

        return new EnumIntegrityResult(
            columns.Count, scanned, violations, skipped, distinctChecked, sw.Elapsed);
    }

    private static Dictionary<string, ValueConverter> BuildConverterMap(IModel model)
    {
        var map = new Dictionary<string, ValueConverter>(StringComparer.Ordinal);
        foreach (var entityType in model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;
            var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                var converter = ResolveConverter(property);
                if (converter is null || converter.ProviderClrType != typeof(string)) continue;
                var column = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(column)) continue;
                map.TryAdd(table + "." + column, converter);
            }
        }
        return map;
    }

    /// <summary>
    /// Chạy CHÍNH converter của EF trên giá trị thô. Trả <c>null</c> nếu giá trị
    /// hợp lệ (KHÔNG báo vi phạm — đây là chỗ giữ lời hứa 0 báo động giả).
    /// </summary>
    private static (EnumViolationKind Kind, string Detail)? Classify(
        ValueConverter converter, Type enumType, string value)
    {
        object? converted;
        try
        {
            converted = converter.ConvertFromProvider(value);
        }
        catch (Exception ex)
        {
            // Hạng 'Done': đúng ngoại lệ EF ném trong shaper lúc materialise.
            return (EnumViolationKind.Throws, $"ném {ex.GetType().Name}");
        }

        if (converted is null)
            return (EnumViolationKind.Undefined, "converter trả null");

        if (IsDefinedValue(enumType, converted)) return null;

        return (EnumViolationKind.Undefined,
            $"map thành {enumType.Name}({Convert.ToInt64(converted, CultureInfo.InvariantCulture)}) không có trong enum");
    }

    /// <summary>
    /// <c>Enum.IsDefined</c> cho enum thường; với <c>[Flags]</c> thì kiểm theo
    /// bit vì tổ hợp hợp lệ không phải là thành viên khai báo.
    /// </summary>
    private static bool IsDefinedValue(Type enumType, object value)
    {
        if (Enum.IsDefined(enumType, value)) return true;
        if (enumType.GetCustomAttributes(typeof(FlagsAttribute), inherit: false).Length == 0)
            return false;

        var raw = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        long mask = 0;
        foreach (var member in Enum.GetValues(enumType))
            mask |= Convert.ToInt64(member, CultureInfo.InvariantCulture);
        return raw != 0 && (raw & ~mask) == 0;
    }

    private static async Task<List<(string Value, long Count)>> ReadDistinctAsync(
        DbConnection connection, EnumStringColumn col, CancellationToken ct)
    {
        var table = Quote(col.Table);
        var column = Quote(col.Column);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            $"SELECT {column} AS v, COUNT(*) AS n FROM {table} WHERE {column} IS NOT NULL GROUP BY {column}";

        var rows = new List<(string, long)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var raw = reader.GetValue(0);
            if (raw is DBNull) continue;
            var text = raw as string
                ?? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            var count = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            rows.Add((text, count));
        }
        return rows;
    }

    // Định danh có dấu nháy kép — hợp lệ trên cả SQLite và SQL Server.
    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Shorten(string message)
    {
        var line = message.Split('\n')[0].Trim();
        return line.Length <= 120 ? line : line[..120] + "…";
    }
}
