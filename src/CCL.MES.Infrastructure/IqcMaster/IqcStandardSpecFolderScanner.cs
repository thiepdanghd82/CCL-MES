using CCL.MES.Application.Services;
using CCL.MES.Infrastructure.IqcMaster;

namespace CCL.MES.Infrastructure.IqcMaster;

public sealed class IqcStandardSpecFolderScanner : IIqcStandardSpecFolderScanner
{
    public IReadOnlyList<IqcStandardSpecParsedRow> Scan(string folderPath)
    {
        var files = IqcStandardSpecFileReader.ScanFolder(folderPath);
        return files.Select(f => new IqcStandardSpecParsedRow
        {
            SpecNo = f.SpecNo,
            MaterialCode = f.MaterialCode,
            MaterialCodeIfs = f.MaterialCodeIfs,
            Revision = f.Revision,
            SupplierName = f.SupplierName,
            FileName = f.FileName,
        }).ToList();
    }
}
