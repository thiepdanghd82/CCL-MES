using System.Globalization;
using CCL.MES.EnumIntegrity;

// ─────────────────────────────────────────────────────────────────────────────
// enum-integrity-scan — quét một file SQLite tìm giá trị nằm ngoài enum.
//
//   Usage:  enum-integrity-scan <db-path> [--list-columns] [--quiet]
//
//   exit 0  sạch
//   exit 1  CÓ vi phạm
//   exit 2  không kết luận được (thiếu file, DB khoá, quét được 0/N cột)
//
// exit 2 tách khỏi exit 1 là cố ý: "không kiểm được" KHÔNG phải "đã kiểm và
// sạch". Gộp hai thứ đó chính là cách một gate trở thành đồ trang trí.
//
// Mở DB ở Mode=ReadOnly — công cụ này KHÔNG BAO GIỜ ghi.
// ─────────────────────────────────────────────────────────────────────────────

var argv = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
var listColumns = args.Contains("--list-columns");
var quiet = args.Contains("--quiet");

if (argv.Count != 1)
{
    Console.Error.WriteLine("usage: enum-integrity-scan <db-path> [--list-columns] [--quiet]");
    return 2;
}

var dbPath = Path.GetFullPath(argv[0]);
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"{EnumIntegrityReport.Tag} không thấy file DB: {dbPath}");
    return 2;
}

if (!quiet)
{
    // R7 — script tự pin DB của nó và IN RA. Verify trên DB khác với DB đang
    // nói tới là verify vô nghĩa.
    var size = new FileInfo(dbPath).Length;
    Console.WriteLine($"{EnumIntegrityReport.Tag} db={dbPath} ({size.ToString("N0", CultureInfo.InvariantCulture)} bytes, mode=ReadOnly)");
}

EnumIntegrityResult result;
try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    result = await EnumIntegrityScanner.ScanSqliteFileAsync(dbPath, cts.Token);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{EnumIntegrityReport.Tag} quét thất bại: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

if (listColumns)
{
    foreach (var col in EnumIntegrityScanner.DiscoverColumns())
        Console.WriteLine($"{EnumIntegrityReport.Tag} column {col}");
}

foreach (var line in EnumIntegrityReport.Lines(result)) Console.WriteLine(line);

if (EnumIntegrityReport.IsInconclusive(result))
{
    Console.Error.WriteLine(
        $"{EnumIntegrityReport.Tag} KHÔNG KẾT LUẬN ĐƯỢC — quét được 0/{result.ColumnsDiscovered} cột " +
        "(DB lạc hậu migration, sai file, hoặc đang bị khoá). Không tính là PASS.");
    return 2;
}

return result.IsClean ? 0 : 1;
