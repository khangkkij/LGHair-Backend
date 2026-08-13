using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using MinhTamHair.Services;

namespace MinhTamHair.Services.UploadIMG
{
    public class UploadDVSP : IUploadDVSP
    {
        private readonly Cloudinary _cloudinary;
        public UploadDVSP(IOptions<CloudinaryDVSPSettings> config)
        {
            var ACC = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(ACC);
        }

        public async Task<ImageUploadResult> AddIMG(IFormFile file)
        {
            var uploadResult = new ImageUploadResult();

            if (file.Length > 0)
            {
                using var uploadStream = file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, uploadStream),
                    Folder = "DVSP",
                };
                uploadResult = await _cloudinary.UploadAsync(uploadParams);
            }
            return uploadResult;
        }

        public async Task<DeletionResult> DelIMG(string publicId)
        {
            var delParams = new DeletionParams(publicId);
            return await _cloudinary.DestroyAsync(delParams);
        }
    }
}
