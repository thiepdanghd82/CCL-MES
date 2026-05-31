namespace CCL.MES.Application.Services.NpiImport;

/// <summary>
/// Phase 7 hạng mục 1 — generic contract cho replace-all CSV import của
/// các bảng NPI master (Structure / Routine / RawMaterials / Spec /
/// WorkCenter). Hạng mục 1 implement <see cref="StructureCsvTarget"/>
/// concrete; các hạng mục sau chỉ thêm target tương ứng + reuse
/// <see cref="NpiImportService"/> + <c>NpiImportModal</c> wizard.
///
/// Semantic merge: replace-all (DELETE + INSERT atomic) — phù hợp với
/// IFS export full-dump CSV. Idempotent (re-import cùng file → cùng
/// end state, không nhân đôi). KHÔNG upsert-by-key.
///
/// Header mapping: kế thừa pattern CMES <c>HEADER_ALIASES</c>
/// (case-insensitive lookup, priority order). Giữ flexibility nếu IFS
/// đổi thứ tự cột.
/// </summary>
public interface ICsvImportTarget<TEntity> where TEntity : class
{
    /// <summary>Tên bảng SQL để DELETE + audit detail JSON.</summary>
    string TableName { get; }

    /// <summary>Key i18n base cho UI (ví dụ "structure" → "npi.structure.import.*").</summary>
    string EntityKey { get; }

    /// <summary>Min số cột tối thiểu một dòng CSV cần có để map. Dưới mức này → skip "short_row".</summary>
    int MinColumnCount { get; }

    /// <summary>
    /// Bảng alias header CSV. Key = field semantic name (ví dụ "parent_part");
    /// value = list alias header acceptable (case-insensitive, priority order).
    /// Tham chiếu CMES service.ts:HEADER_ALIASES; reuse identical strings.
    /// </summary>
    IReadOnlyDictionary<string, string[]> HeaderAliases { get; }

    /// <summary>Field names bắt buộc — nếu không xác định được index trong CSV header → fail parse.</summary>
    IReadOnlyList<string> RequiredFields { get; }

    /// <summary>
    /// Map 1 dòng CSV → entity instance. Trả về null nếu row invalid
    /// (caller sẽ tính vào counter skipped).
    /// </summary>
    TEntity? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap);
}
