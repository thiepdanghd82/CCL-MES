using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Localization;

/// <summary>
/// In-code translation table. Entries live in per-surface partial files
/// (<c>TranslationCatalog.Nav.cs</c>, <c>.TopBar.cs</c>, …) so each
/// migration batch (audit §5) touches one file and the parity test keeps
/// every batch honest (EN+VI required).
///
/// Immutable after construction → registered as a singleton. Keys are
/// ordinal (case-sensitive) — always author them lower.dotted.
/// </summary>
public sealed partial class TranslationCatalog : ITranslationCatalog
{
    private readonly Dictionary<string, IReadOnlyDictionary<LanguageCode, string>> _map =
        new(StringComparer.Ordinal);

    public TranslationCatalog()
    {
        // One Register* call per partial file / migration batch.
        RegisterCommon();   // batch 1
        RegisterNav();      // batch 1
        RegisterTopBar();   // batch 1
        RegisterAppearance();// batch 1
        RegisterLogin();    // batch 2B

        // batch 2C — WorkOrders + operational dashboards (P10.7/P11).
        RegisterWorkOrders();
        RegisterPrepress();
        RegisterIpqc();
        RegisterQaApproval();
        RegisterRunning();
        RegisterSetting();
        RegisterLegs();
        RegisterSemi();
        RegisterFqc();
        RegisterOqc();
        RegisterShipped();
        RegisterWoMaterials();
        RegisterWoPlate();
        RegisterWoCutter();
        RegisterWoPause();
        RegisterWoQty();
        RegisterWoFinish();
        RegisterQcPhoto();
        RegisterHardwareRow();
        RegisterTraceDetail();

        // batch 2D — Settings pages.
        RegisterSettings();
        RegisterAbout();
        RegisterAccounts();
        RegisterAuditLog();
        RegisterBackup();
        RegisterConnection();
        RegisterPassword();
        RegisterProfile();

        // batch 2E — SpecHub-port surface (Home / ShopOrders / Spec* / etc.).
        RegisterHome();
        RegisterShopOrders();
        RegisterSpecDetail();
        RegisterSpecs();
        RegisterGridSearch();
        RegisterNpiImport();
        RegisterRecentScans();
        RegisterSpecConfirm();
        RegisterSpecMenu();
        RegisterSpecDiff();
        RegisterSpecDrawCard();
        RegisterSpecDrawTab();
        RegisterSpecQcCap();
        RegisterSpecQcPlan();
        RegisterSpecCompact();
        RegisterSpecEdit();
        RegisterSpecFull();
        // batch 2F — NPI grids + monitoring + spec modals + misc.
        RegisterMachine();
        RegisterQcHistory();
        RegisterNpiWc();
        RegisterQualTrace();
        RegisterQcLib();
        RegisterMode();
        RegisterQms();
        RegisterQmsUi();
        RegisterIqcTicket();
        RegisterNpiStruct();
        RegisterNpiRoute();
        RegisterNpiRaw();
        RegisterHardware();
        RegisterLock();
        RegisterSemiWh();
        RegisterImportSpec();
        RegisterCreateSpec();
        RegisterEditSpec();
        RegisterCopySpec();
        RegisterReviseSpec();
        RegisterSupersede();
        RegisterQcCapture();
        RegisterDrawingDecide();
        RegisterScanner();
        RegisterGridCols();
        RegisterCrash();
        RegisterMaterialLot();
        RegisterHealth();
    }

    /// <summary>Register one key with both language slots. Called from the
    /// partial Register* files.</summary>
    private void Add(string key, string vi, string en) =>
        _map[key] = new Dictionary<LanguageCode, string>
        {
            [LanguageCode.Vietnamese] = vi,
            [LanguageCode.English] = en,
        };

    public string? Lookup(string key, LanguageCode lang) =>
        _map.TryGetValue(key, out var perLang) && perLang.TryGetValue(lang, out var s) ? s : null;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<LanguageCode, string>> All => _map;
}
