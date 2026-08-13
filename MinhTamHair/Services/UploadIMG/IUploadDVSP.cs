using CloudinaryDotNet.Actions;

namespace MinhTamHair.Services.UploadIMG
{
    // Service upload ảnh riêng cho module Dịch Vụ / Sản Phẩm (DVSP).
    // Dùng chung logic với IUploadIMG nhưng tách cấu hình (CloudinaryDVSPSettings)
    // để có thể trỏ sang 1 account/folder Cloudinary khác trong tương lai mà
    // không ảnh hưởng các module khác đang dùng IUploadIMG.
    public interface IUploadDVSP
    {
        Task<ImageUploadResult> AddIMG(IFormFile file);

        Task<DeletionResult> DelIMG(string publicId);
    }
}
