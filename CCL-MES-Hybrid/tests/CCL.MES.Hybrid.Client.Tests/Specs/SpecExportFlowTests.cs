using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Specs;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5g — Pure-helper coverage for the Spec export orchestrator.
///
/// Two surfaces:
///   - <see cref="SpecExportFlow.StampedListFilename"/> +
///     <see cref="SpecExportFlow.StampedSheetFilename"/> filename
///     composition (deterministic on a frozen <see cref="DateTime"/>).
///   - End-to-end <see cref="SpecExportFlow.ExportListAsync"/> +
///     <see cref="SpecExportFlow.ExportSheetPdfAsync"/> orchestration
///     via fake API + fake saver + tmp-dir opener.
///
/// The fakes record every call so the test asserts both the
/// shape of the path handed to <see cref="IFileSaver.SaveAsync"/> and
/// the path returned in the outcome (sandbox vs saved).
/// </summary>
public sealed class SpecExportFlowTests
{
    // ── Filename helpers ─────────────────────────────────────────────

    [Theory]
    [InlineData("csv",  "NpiSpecLibrary_20260604-103015.csv")]
    [InlineData("xlsx", "NpiSpecLibrary_20260604-103015.xlsx")]
    [InlineData("pdf",  "NpiSpecLibrary_20260604-103015.pdf")]
    public void List_filename_carries_prefix_timestamp_and_extension(string fmt, string expected)
    {
        var frozen = new DateTime(2026, 6, 4, 10, 30, 15, DateTimeKind.Local);
        Assert.Equal(expected, SpecExportFlow.StampedListFilename(fmt, frozen));
    }

    [Fact]
    public void List_filename_lowercases_uppercase_format_token()
    {
        var frozen = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Assert.EndsWith(".xlsx", SpecExportFlow.StampedListFilename("XLSX", frozen));
    }

    [Fact]
    public void Sheet_filename_sanitises_slashes_and_spaces_to_underscore()
    {
        var frozen = new DateTime(2026, 6, 4, 10, 30, 15, DateTimeKind.Local);
        var name = SpecExportFlow.StampedSheetFilename("REF/2026 0042", "B", frozen);
        Assert.StartsWith("SpecSheet_REF_2026_0042_RevB_", name);
        Assert.EndsWith(".pdf", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain(" ", name);
    }

    [Fact]
    public void Sheet_filename_falls_back_to_spec_when_input_is_blank()
    {
        var frozen = new DateTime(2026, 6, 4, 10, 30, 15, DateTimeKind.Local);
        var name = SpecExportFlow.StampedSheetFilename("", "", frozen);
        Assert.Contains("SpecSheet_spec_Revspec_", name);
    }

    // ── End-to-end orchestration ─────────────────────────────────────

    [Fact]
    public async Task ExportListAsync_save_outcome_when_saver_returns_destination()
    {
        var (flow, api, saver, opener) = BuildFlow(SaveOutcome.Success("/tmp/operator/save.csv"));
        var outcome = await flow.ExportListAsync(
            format: "csv", search: null, view: "Active", planner: null,
            openAfterSave: true);

        Assert.True(outcome.DidSave);
        Assert.Equal("/tmp/operator/save.csv", outcome.DestinationPath);
        Assert.True(outcome.Opened);                     // opener stub returns true
        Assert.Equal(1, api.ListDownloadCalls);
        Assert.Equal(1, saver.SaveCalls);
        Assert.Single(opener.OpenedPaths);
        Assert.Equal("/tmp/operator/save.csv", opener.OpenedPaths[0]);
    }

    [Fact]
    public async Task ExportListAsync_keeps_sandbox_path_when_saver_cancels()
    {
        var (flow, api, _, opener) = BuildFlow(SaveOutcome.Cancelled);
        var outcome = await flow.ExportListAsync(
            format: "pdf", search: "abc", view: "Active", planner: "FLEXO",
            openAfterSave: false);

        Assert.False(outcome.DidSave);
        Assert.Null(outcome.DestinationPath);
        Assert.Contains("NpiSpecLibrary_", outcome.SandboxPath);
        Assert.False(outcome.Opened);                    // openAfterSave=false
        Assert.Empty(opener.OpenedPaths);
        Assert.Equal(1, api.ListDownloadCalls);
    }

    [Fact]
    public async Task ExportListAsync_opens_sandbox_path_when_save_cancelled_but_openAfterSave_true()
    {
        var (flow, _, _, opener) = BuildFlow(SaveOutcome.Cancelled);
        var outcome = await flow.ExportListAsync(
            format: "xlsx", search: null, view: "Trash", planner: null,
            openAfterSave: true);

        Assert.False(outcome.DidSave);
        Assert.True(outcome.Opened);
        Assert.Single(opener.OpenedPaths);
        // Sandbox path is what the opener saw (NOT a destination path).
        Assert.Equal(outcome.SandboxPath, opener.OpenedPaths[0]);
    }

    [Fact]
    public async Task ExportSheetPdfAsync_routes_revisionId_to_api()
    {
        var (flow, api, _, _) = BuildFlow(SaveOutcome.Success("/tmp/operator/sheet.pdf"));
        var outcome = await flow.ExportSheetPdfAsync(
            revisionId: 42, refNoOrSpecCode: "REF-42", revisionCode: "C",
            openAfterSave: false);

        Assert.True(outcome.DidSave);
        Assert.Equal(1, api.SheetDownloadCalls);
        Assert.Equal(42, api.LastSheetRevisionId);
        Assert.Contains("SpecSheet_REF-42_RevC_", outcome.SuggestedFileName);
    }

    [Fact]
    public async Task ExportListAsync_throws_on_unknown_format()
    {
        var (flow, _, _, _) = BuildFlow(SaveOutcome.Cancelled);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            flow.ExportListAsync("docx", null, "Active", null, false));
    }

    [Fact]
    public async Task ExportListAsync_forwards_filter_args_to_api()
    {
        var (flow, api, _, _) = BuildFlow(SaveOutcome.Cancelled);
        await flow.ExportListAsync(
            format: "csv", search: "ARB", view: "All", planner: "SILK",
            openAfterSave: false);

        Assert.Equal("csv", api.LastListFormat);
        Assert.Equal("ARB", api.LastListSearch);
        Assert.Equal("All", api.LastListView);
        Assert.Equal("SILK", api.LastListPlanner);
    }

    // ── Fake plumbing ────────────────────────────────────────────────

    private static (SpecExportFlow flow, FakeApi api, FakeSaver saver, FakeOpener opener) BuildFlow(SaveOutcome saveOutcome)
    {
        var opener = new FakeOpener();
        var saver = new FakeSaver(saveOutcome);
        var api = new FakeApi();
        return (new SpecExportFlow(api, opener, saver), api, saver, opener);
    }

    private sealed class FakeOpener : IFileOpener
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"spec-export-tests-{Guid.NewGuid():N}");
        public List<string> OpenedPaths { get; } = new();

        public FakeOpener() => Directory.CreateDirectory(_tmp);

        public string GetSafeDownloadDirectory() => _tmp;

        public Task<bool> TryOpenAsync(string absolutePath)
        {
            OpenedPaths.Add(absolutePath);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSaver : IFileSaver
    {
        private readonly SaveOutcome _outcome;
        public int SaveCalls { get; private set; }
        public string? LastSourcePath { get; private set; }
        public string? LastSuggestedName { get; private set; }

        public FakeSaver(SaveOutcome outcome) => _outcome = outcome;

        public Task<SaveOutcome> SaveAsync(string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
        {
            SaveCalls++;
            LastSourcePath = sourceFilePath;
            LastSuggestedName = suggestedFileName;
            return Task.FromResult(_outcome);
        }
    }

    private sealed class FakeApi : ISpecExportDownloads
    {
        public int ListDownloadCalls { get; private set; }
        public int SheetDownloadCalls { get; private set; }
        public string? LastListFormat { get; private set; }
        public string? LastListSearch { get; private set; }
        public string? LastListView { get; private set; }
        public string? LastListPlanner { get; private set; }
        public long? LastSheetRevisionId { get; private set; }

        public Task<long> DownloadSpecListExportAsync(
            string format, string? search, string view, string? planner,
            string destinationFilePath, CancellationToken ct = default)
        {
            ListDownloadCalls++;
            LastListFormat = format;
            LastListSearch = search;
            LastListView = view;
            LastListPlanner = planner;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.WriteAllBytes(destinationFilePath, new byte[] { 1, 2, 3 });
            return Task.FromResult<long>(3);
        }

        public Task<long> DownloadSpecSheetPdfAsync(
            long revisionId, string destinationFilePath, CancellationToken ct = default)
        {
            SheetDownloadCalls++;
            LastSheetRevisionId = revisionId;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.WriteAllBytes(destinationFilePath, new byte[] { 1, 2, 3, 4 });
            return Task.FromResult<long>(4);
        }

        public int SheetXlsxDownloadCalls { get; private set; }

        public Task<long> DownloadSpecSheetXlsxAsync(
            long revisionId, string destinationFilePath, CancellationToken ct = default)
        {
            SheetXlsxDownloadCalls++;
            LastSheetRevisionId = revisionId;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.WriteAllBytes(destinationFilePath, new byte[] { 1, 2, 3, 4, 5 });
            return Task.FromResult<long>(5);
        }
    }
}
