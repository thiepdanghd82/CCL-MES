using System.Globalization;
using CCL.MES.Application.SpecDetail;
using CCL.MES.Application.SpecExport;
using CCL.MES.Domain;
using CCL.MES.Infrastructure.SpecExport;

// Phase 8 PR-B — Verify dispatch + render across SILK / FLEXO / GENERIC templates.
//
// Walks 5 synthetic SpecDetailDto inputs through PdfSpecSheetExporter:
//   1. SILK  — minimal silk spec (10 colors stub) → exercises silk template
//   2. FLEXO — minimal flexo spec (3 cut + 3 ink) → exercises flexo template
//   3. INDIGO empty — Planner=INDIGO, zero rows → generic + warning + no-data paragraph
//   4. LETTER w/ silk-shape data — Planner=LETTER, 3 PrintColors → generic + warning + silk-style table reuse
//   5. DIECUT w/ flexo-cut data — Planner=DIECUT, 2 FlexoCuttingRows → generic + warning + flexo-cut table reuse
//
// Pass criterion: PDF byte[] non-empty + no exception per case. Output writes
// to /tmp/pr-b-verify/ so caller can `open <file>.pdf` and eyeball.

var ctx = new SpecExportContext(
    Title:             "PR-B Verify",
    FilterDescription: null,
    GeneratedAt:       DateTime.Now,
    GeneratedBy:       "verify-pr-b",
    Culture:           CultureInfo.InvariantCulture);

var outDir = "/tmp/pr-b-verify";
Directory.CreateDirectory(outDir);

var exporter = new PdfSpecSheetExporter();
int pass = 0, fail = 0;

void Run(string label, SpecDetailDto dto, string filename)
{
    try
    {
        var bytes = exporter.Export(dto, ctx);
        if (bytes.Length == 0) throw new InvalidOperationException("PDF byte[] is empty");
        var path = Path.Combine(outDir, filename);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  PASS  {label,-32}  {bytes.Length,8} bytes  →  {path}");
        pass++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {label,-32}  {ex.GetType().Name}: {ex.Message}");
        fail++;
    }
}

Console.WriteLine("PR-B Verifier — SpecPdfDocumentBuilder template dispatch");
Console.WriteLine("──────────────────────────────────────────────────────────");

// 1. SILK
Run("SILK / silkscreen", BuildSilk(), "01_silk_silkscreen.pdf");

// 2. FLEXO
Run("FLEXO / flexo",     BuildFlexo(), "02_flexo_flexo.pdf");

// 3. INDIGO empty (generic + warning + no-data paragraph)
Run("GENERIC / indigo empty", BuildGenericEmpty("INDIGO"), "03_generic_indigo_empty.pdf");

// 4. LETTER with silk-shape data (generic + warning + silk colors table reuse)
Run("GENERIC / letter + silk rows", BuildGenericWithSilkRows("LETTER"), "04_generic_letter_silk_rows.pdf");

// 5. DIECUT with flexo-cut data (generic + warning + flexo cut table reuse)
Run("GENERIC / diecut + flexo cut", BuildGenericWithFlexoCut("DIECUT"), "05_generic_diecut_flexo_cut.pdf");

Console.WriteLine("──────────────────────────────────────────────────────────");
Console.WriteLine($"Result: {pass} pass / {fail} fail");
Environment.Exit(fail == 0 ? 0 : 1);


static SpecDetailDto BuildSilk() => new()
{
    Id = 1001,
    SpecCode = "VERIFY-SILK-001",
    Title = "Synthetic Silk Spec",
    RevisionCode = "A",
    Status = ProductRevisionStatus.Draft,
    RefNo = "REF-001",
    InspectionLevel = "A",
    Planner = "SILK",
    ProcessCode = "SILKSCREEN",
    IsSilkscreen = true,
    IsFlexo = false,
    ProductCode = "P-SILK-001",
    ProductName = "Silk Test Part",
    CustomerName = "CCL Vietnam",
    SubstrateType = "PET 100um",
    AdhesiveType = "Acrylic",
    PrintingCavity = 8,
    LengthPitchMm = 320.0,
    ProductSizeWmm = 60.0,
    ProductSizeHmm = 40.0,
    CreatedAt = DateTime.Now.AddDays(-7),
    CreatedBy = "verify",
    PrintColors = Enumerable.Range(1, 10).Select(i => new SpecPrintColorRow(
        Seq: i, Surface: "Top", Color: $"Color{i}", InkName: $"Ink {i}",
        InkCode: $"IC-{i:D3}", Maker: "Sakata", Retarder: "R-A", Viscosity: 22.0, Speed: 80.0,
        Squeegee: "YR", Dry: "OVEN", TemperatureC: 80.0, TimeMin: 5, Uv: "Y",
        EmulsionUm: 12.0, PlateSize: "300x300", Mesh: "180T", AngleDeg: 22.5,
        PlateCode: $"PL-{i:D3}", ControlNo: i, Remark: null)).ToList(),
};

static SpecDetailDto BuildFlexo() => new()
{
    Id = 1002,
    SpecCode = "VERIFY-FLEXO-001",
    Title = "Synthetic Flexo Spec",
    RevisionCode = "A",
    Status = ProductRevisionStatus.Approved,
    RefNo = "REF-002",
    InspectionLevel = "B",
    Planner = "FLEXO",
    ProcessCode = "FLEXO",
    IsSilkscreen = false,
    IsFlexo = true,
    ProductCode = "P-FLEXO-001",
    ProductName = "Flexo Test Part",
    CustomerName = "CCL Vietnam",
    SubstrateType = "PP 80um",
    ProductSizeWmm = 100.0,
    ProductSizeHmm = 50.0,
    CreatedAt = DateTime.Now.AddDays(-14),
    ApprovedAt = DateTime.Now.AddDays(-7),
    ApprovedBy = "verify",
    FlexoPrintRows = Enumerable.Range(1, 2).Select(i => new FlexoPrintRow(
        Seq: i, Process: $"Process-{i}", Material: "PP", Thickness: "80um",
        Size: "100x50", Cylinders: "Z=85", PitchMm: "320", Speed: "60",
        TensionHead: "30", TensionEnd: "28", TensionRoll: "32",
        PlateCavity: "8", Tension: "30")).ToList(),
    FlexoCuttingRows = Enumerable.Range(1, 3).Select(i => new FlexoCuttingRow(
        Seq: i, Process: $"Cut-{i}", Lamination: "Tape-A", Size: "100x50",
        CutterLot: "L-A", CutterName: $"Cutter-{i}", PcsPerSheet: 8,
        CuttingCavity: 8, PitchMm: 320.0, Packing: "Roll",
        PaperSpeed: 60.0, CuttingSpeed: 60.0, CuttingPressure: 3.0,
        HeadTension: 30.0, RollTension: 32.0)).ToList(),
    FlexoInkRows = Enumerable.Range(1, 3).Select(i => new FlexoInkRow(
        Seq: i, Color: $"Color-{i}", InkCode: $"IC-F-{i:D3}",
        InkDescription: $"Flexo Ink {i}", Brand: "Sakata",
        Anilox: "600/4.0", PlateCode: $"PL-F-{i:D3}",
        Pressure: 3.0, UvPowerW: 240.0, IrPowerW: 180.0)).ToList(),
};

static SpecDetailDto BuildGenericEmpty(string planner) => new()
{
    Id = 1003,
    SpecCode = $"VERIFY-{planner}-EMPTY",
    Title = $"Synthetic {planner} (no data)",
    RevisionCode = "A",
    Status = ProductRevisionStatus.Draft,
    RefNo = "REF-003",
    InspectionLevel = "A",
    Planner = planner,
    ProcessCode = planner,
    IsSilkscreen = false,
    IsFlexo = false,
    ProductCode = "P-GEN-001",
    ProductName = "Generic Test Part",
    CustomerName = "CCL Vietnam",
    SubstrateType = "Unknown",
    CreatedAt = DateTime.Now.AddDays(-1),
    CreatedBy = "verify",
};

static SpecDetailDto BuildGenericWithSilkRows(string planner) => new()
{
    Id = 1004,
    SpecCode = $"VERIFY-{planner}-SILK-ROWS",
    Title = $"Synthetic {planner} (silk-shape rows)",
    RevisionCode = "A",
    Status = ProductRevisionStatus.InReview,
    RefNo = "REF-004",
    InspectionLevel = "A",
    Planner = planner,
    ProcessCode = planner == "LETTER" ? "LETTERPRESS" : planner,
    IsSilkscreen = false,
    IsFlexo = false,
    ProductCode = "P-LETTER-001",
    ProductName = "Letterpress Test Part",
    CustomerName = "CCL Vietnam",
    SubstrateType = "Paper",
    CreatedAt = DateTime.Now.AddDays(-3),
    CreatedBy = "verify",
    PrintColors = Enumerable.Range(1, 3).Select(i => new SpecPrintColorRow(
        Seq: i, Surface: "Top", Color: $"Color-{i}", InkName: $"Letter Ink {i}",
        InkCode: $"LI-{i:D3}", Maker: "TOYO", Retarder: null, Viscosity: null, Speed: null,
        Squeegee: null, Dry: "ND", TemperatureC: null, TimeMin: null, Uv: null,
        EmulsionUm: null, PlateSize: "200x200", Mesh: null, AngleDeg: null,
        PlateCode: $"LP-{i:D3}", ControlNo: i, Remark: "Letterpress run")).ToList(),
};

static SpecDetailDto BuildGenericWithFlexoCut(string planner) => new()
{
    Id = 1005,
    SpecCode = $"VERIFY-{planner}-CUT-ROWS",
    Title = $"Synthetic {planner} (flexo-cut rows)",
    RevisionCode = "A",
    Status = ProductRevisionStatus.Released,
    RefNo = "REF-005",
    InspectionLevel = "A",
    Planner = planner,
    ProcessCode = planner == "DIECUT" ? "ROTARY_CUT" : planner,
    IsSilkscreen = false,
    IsFlexo = false,
    ProductCode = "P-DIECUT-001",
    ProductName = "Die-cut Test Part",
    CustomerName = "CCL Vietnam",
    SubstrateType = "Cardstock",
    CreatedAt = DateTime.Now.AddDays(-2),
    CreatedBy = "verify",
    FlexoCuttingRows = Enumerable.Range(1, 2).Select(i => new FlexoCuttingRow(
        Seq: i, Process: $"DieCut-{i}", Lamination: null, Size: "100x50",
        CutterLot: "L-DC", CutterName: $"DC-{i}", PcsPerSheet: 16,
        CuttingCavity: 16, PitchMm: 200.0, Packing: "Sheet",
        PaperSpeed: 30.0, CuttingSpeed: 30.0, CuttingPressure: 4.5,
        HeadTension: null, RollTension: null)).ToList(),
};
