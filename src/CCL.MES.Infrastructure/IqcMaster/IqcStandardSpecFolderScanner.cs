using CCL.MES.Application.Services;
using CCL.MES.Infrastructure.IqcMaster;

namespace CCL.MES.Infrastructure.IqcMaster;

public sealed class IqcStandardSpecFolderScanner : IIqcStandardSpecFolderScanner
{
    public IReadOnlyList<IqcStandardSpecParsedRow> Scan(string folderPath)
    {
        // Mặc định đọc đủ Form (header + hạng mục) — nút Import UI yêu cầu
        // toàn bộ nội dung spec, không chỉ tên file.
        var files = IqcStandardSpecFileReader.ScanFolder(folderPath, readFormContent: true);
        return files.Select(f => new IqcStandardSpecParsedRow
        {
            SpecNo = f.SpecNo,
            MaterialCode = f.MaterialCode,
            MaterialCodeIfs = f.MaterialCodeIfs,
            Revision = f.Revision,
            SupplierName = f.SupplierName,
            FileName = f.FileName,
            Items = f.Items.Select(i => new IqcStandardSpecParsedItem
            {
                ItemId = i.ItemId,
                Seq = i.Seq,
                AcceptanceVi = i.AcceptanceVi,
                MethodVi = i.MethodVi,
            }).ToList(),
        }).ToList();
    }
}
