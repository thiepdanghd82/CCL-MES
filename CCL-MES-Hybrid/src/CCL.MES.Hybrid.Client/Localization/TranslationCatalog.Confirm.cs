namespace CCL.MES.Hybrid.Client.Localization;

// L52 — Shared ConfirmToggle.razor (confirm.*). The OK/NG confirmation
// control used across Prepress / IPQC / FQC / OQC. Short, reusable labels.
public sealed partial class TranslationCatalog
{
    private void RegisterConfirm()
    {
        //     key                vi              en
        Add("confirm.ok",         "OK",           "OK");
        Add("confirm.ng",         "NG",           "NG");
        Add("confirm.header",     "Xác nhận",     "Confirm");
    }
}
