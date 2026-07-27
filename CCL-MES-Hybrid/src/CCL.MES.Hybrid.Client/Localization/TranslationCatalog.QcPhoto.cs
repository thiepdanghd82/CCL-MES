namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — QcPhotoStrip.razor (qcphoto.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQcPhoto()
    {
        //     key                            vi                                           en
        Add("qcphoto.empty",              "Chưa có ảnh",                                "No photos yet");
        Add("qcphoto.img.alt",            "Ảnh QC",                                     "QC photo");
        Add("qcphoto.delete.title",       "Xóa ảnh",                                    "Delete photo");
        Add("qcphoto.uploading",          "Đang tải lên…",                              "Uploading…");
        Add("qcphoto.add",                "+ Thêm ảnh",                                 "+ Add photo");
        Add("qcphoto.error.too_large",    "Ảnh quá lớn — tối đa 5 MB.",                 "Photo too large — maximum 5 MB.");
        Add("qcphoto.error.bad_type",     "Ảnh phải là JPG hoặc PNG.",                  "Photo must be JPG or PNG.");
        Add("qcphoto.error.upload_failed", "Tải ảnh lên thất bại: {0}",                 "Photo upload failed: {0}");
        Add("qcphoto.error.delete_failed", "Xóa ảnh thất bại: {0}",                     "Photo delete failed: {0}");
    }
}
