using CloudinaryDotNet.Actions;

namespace MinhTamHair.Services.UploadIMG
{
    public interface IUploadIMG
    {
        Task<ImageUploadResult> AddIMG(IFormFile file);

        Task<DeletionResult> DelIMG(string publicId);
    }
}
