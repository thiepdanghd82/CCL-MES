namespace CCL.MES.Hybrid.Client.Localization;

// gate-enum-integrity TẦNG 3 — chuỗi hiển thị cho tín hiệu tính toàn vẹn dữ
// liệu mà GET /api/v2/health/ready trả về (trường dataIntegrity.messageKey).
//
// API trả KEY chứ không trả câu chữ: server không biết người đang nhìn màn hình
// chọn ngôn ngữ nào, và một chuỗi tiếng Việt đúc cứng trong JSON sẽ hiện nguyên
// văn cho người dùng EN. Ba trạng thái, ba key — "unknown" tách khỏi "ok" là
// điểm mấu chốt: không kiểm được KHÔNG phải là sạch.
public sealed partial class TranslationCatalog
{
    private void RegisterHealth()
    {
        //     key                              vi                                                                       en
        Add("health.enumIntegrity.ok",
            "Dữ liệu lành — mọi cột trạng thái đều nằm trong danh mục hợp lệ",
            "Data is clean — every status column holds a defined value");
        Add("health.enumIntegrity.degraded",
            "Dữ liệu nhiễm — có dòng mang giá trị trạng thái không tồn tại; màn hình dùng dữ liệu đó sẽ lỗi",
            "Data is contaminated — some rows hold a status value that does not exist; screens reading them will fail");
        Add("health.enumIntegrity.unknown",
            "Chưa kiểm được tính toàn vẹn dữ liệu — đây không phải là kết quả đạt",
            "Data integrity could not be checked — this is not a pass");
    }
}
