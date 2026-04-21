using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace MinhTamHair.Services.UploadIMG
{
    public class UploadIMG : IUploadIMG
    {
        private readonly Cloudinary _cloudinary;
        public UploadIMG(IOptions<CloudinarySetings> config)
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
                using var uploadStream =   file.OpenReadStream();
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(file.FileName, uploadStream),
                    Folder = "LUAGA",
                    //Tùy chỉnh ảnh khi upload 
                    //Transformation = new Transformation().Height(500).Width(500).Crop("fill").Gravity("face"),
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
        //public string getPublicId(string url)
        //{
        //    if(string.IsNullOrEmpty(url))
        //        return string.Empty;


        //    return url;
        //}
    }
}
