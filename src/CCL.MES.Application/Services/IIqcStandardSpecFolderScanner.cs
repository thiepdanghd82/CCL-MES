namespace CCL.MES.Application.Services;

/// <summary>Quét folder file Form tiêu chuẩn → hàng parse (Infrastructure).</summary>
public interface IIqcStandardSpecFolderScanner
{
    IReadOnlyList<IqcStandardSpecParsedRow> Scan(string folderPath);
}
