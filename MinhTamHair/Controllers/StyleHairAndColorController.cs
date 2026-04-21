using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.DTO.Style_Hair_Color;
using MinhTamHair.Models;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StyleHairAndColorController : ControllerBase
    {
        private readonly IWebHostEnvironment _environment;
        private readonly MinhTamHairContext _context;
       private readonly IUploadIMG _uploadIMG;
        public StyleHairAndColorController(IWebHostEnvironment environment, MinhTamHairContext context, IUploadIMG upload)
        {
            _environment = environment;
            _context = context;
            _uploadIMG = upload;
        }

        // QUẢN LÝ KIỂU TÓC
        [HttpGet]
        public async Task<IActionResult> GetStyleHair()
        {
            var styleHair = await _context.Kifutocs.AsNoTracking().Select(k => new Style_HairResponse
            {
              MaKt=  k.MaKt,
               TenKieuToc= k.TenKieuToc,
             HinhAnh=   k.HinhAnh,
                MoTa = k.MoTa,
                TrangThai =  k.TrangThai
            }).ToListAsync();
           
            return Ok(new
            {
                status = 200,
                message = "Lấy kiểu tóc thành công",
                data = styleHair
            });
        }
        [HttpGet("{id}")]
        public async Task<IActionResult> GetStyleHairById(string id)
        {
            var styleHairId = await _context.Kifutocs.FindAsync(id);
            if (styleHairId == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Không tìm thấy kiểu tóc"
                });
            }

            var result = new Style_HairResponse
            {
                MaKt = styleHairId.MaKt,
                TenKieuToc = styleHairId.TenKieuToc,
                HinhAnh = styleHairId.HinhAnh,
                TrangThai = styleHairId.TrangThai
            };
            return Ok(new
            {
                status = 200,
                message = "Lấy kiểu tóc thành công",
                data = result
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateStyleHair([FromForm] Style_HairRequest request)
        {
            try
            {
                string imageUrl = "";

                if (request.HinhAnh != null && request.HinhAnh.Length > 0)
                {
                    var uploadResult = await _uploadIMG.AddIMG(request.HinhAnh);

                    if (uploadResult.Error != null)
                    {
                        return BadRequest(new { status = 400, message = uploadResult.Error.Message });
                    }
                   
                    imageUrl = uploadResult.SecureUrl.ToString();
                }
                var newStyleHair = new Kifutoc
                {
                    MaKt = createStyleHair(),
                    TenKieuToc = request.TenKieuToc,
                    HinhAnh = imageUrl,
                    MoTa = request.MoTa,
                    TrangThai = request.TrangThai = 1
                };
                _context.Kifutocs.Add(newStyleHair);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    status = 200,
                    message = "Thêm kiểu tóc thành công",
                    data = newStyleHair
                });



            } catch(Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Thêm kiểu tóc thất bại",
                    error = ex.Message
                });
            }
            
        }
        private string createStyleHair()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var styleHairId = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[styleHairId.Next(s.Length)]).ToArray());
        }


        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateStyleHair( string id,[FromForm]  Style_HairRequest request)
        {


            var styleHairId = await _context.Kifutocs.FindAsync(id);
            if (styleHairId == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Không tìm thấy kiểu tóc"
                });
            }

            if(request.HinhAnh is not null && request.HinhAnh.Length > 0)
            {
                var uploadResult = await _uploadIMG.AddIMG(request.HinhAnh);
                if (uploadResult.Error != null)
                {
                    return BadRequest(new { status = 400, message = uploadResult.Error.Message });
                }
               
                styleHairId.HinhAnh = uploadResult.SecureUrl.ToString();
            }



            if (!string.IsNullOrEmpty(request.TenKieuToc))
                styleHairId.TenKieuToc = request.TenKieuToc;

            if (!string.IsNullOrEmpty(request.MoTa))
                styleHairId.MoTa = request.MoTa;

            if (request.TrangThai.HasValue)
                styleHairId.TrangThai = request.TrangThai.Value;
            await _context.SaveChangesAsync();
            return Ok(new
            {
                status = 200,
                message = "Cập nhật kiểu tóc thành công",
                data = styleHairId
            });

        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> deleteHairStyle([FromForm] string id)
        {
            try
            {
                var delHair = await _context.Kifutocs.FindAsync(id);
                if (delHair == null)
                {
                    return NotFound(new
                    {
                        status = 404,
                        message = "Không tìm thấy kiểu tóc"
                    });
                }
                var lichSuLienQuan = await _context.Lichsuthutocs.Where(x => x.MaKt == id).ToListAsync();
                if (lichSuLienQuan.Any())
                {
                    _context.Lichsuthutocs.RemoveRange(lichSuLienQuan);
                }
                if(!string.IsNullOrEmpty(delHair.HinhAnh))
                {
                   
                        await _uploadIMG.DelIMG(delHair.HinhAnh);
                    
                }
                _context.Kifutocs.Remove(delHair);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    status = 200,
                    message = "Xóa kiểu tóc thành công",
                    data = id
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Xóa kiểu tóc thất bại",
                    error = ex.Message
                });
            }
        }
        // QUẢN LÝ KIỂU TÓC


        //QUẢN LÝ MÀU TÓC
        [HttpGet("color")]
        public async Task<IActionResult> GetColor()
        {
            try
            {
                var color = await _context.Bangmaus.Select(m => new ColorResponse
                {
                   MaMau = m.MaMau,
                   TenMau =   m.TenMau,
                    TrangThai  = m.TrangThai
                }).ToListAsync();
               
                return Ok(new
                {
                    status = 200,
                    message = "Lấy màu tóc thành công",
                    data = color

                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Lấy màu tóc thất bại",
                    error = ex.Message
                });
            }
        }

        [HttpGet("color/{id}")]
        public async  Task<IActionResult> GetColorById(string id)
        {
            var colorId = await _context.Bangmaus.FindAsync(id);
            if (colorId == null)
            {
                return NotFound(new
                {
                    status = 404,
                    message = "Không tìm thấy màu tóc"
                });
            }
            var result = new ColorResponse
            {
                MaMau = colorId.MaMau,
                TenMau = colorId.TenMau,
                TrangThai = colorId.TrangThai ?? 0
            };
            return Ok(new
            {
                status = 200,
                message = "Lấy màu tóc thành công",
                data = result
            });
        }

        [HttpPost("color")]
        public async Task<IActionResult> CreateColor([FromForm] ColorRequest request)
        {
            try
            {
                var newColor = new Bangmau
                {
                    MaMau = request.MaMau!,
                    TenMau = request.TenMau,
                    TrangThai = request.TrangThai = 1
                };
                _context.Bangmaus.Add(newColor);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    status = 200,
                    message = "Thêm màu tóc thành công",
                    data = newColor
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Thêm màu tóc thất bại",
                    error = ex.Message
                });
            }
        }

        [HttpPut("color/{id}")]
        public async Task<IActionResult> UpdateColor(string id,[FromForm]  ColorRequest request)
        {
            try
            {
                var colorId = await _context.Bangmaus.FindAsync(id);
                if (colorId == null)
                {
                    return NotFound(new
                    {
                        status = 404,
                        message = "Không tìm thấy màu tóc"
                    });
                }
                colorId.TenMau = request.TenMau;
                colorId.TrangThai = request.TrangThai;
                await _context.SaveChangesAsync();

                var result = new ColorResponse
                {
                    MaMau = colorId.MaMau,
                    TenMau = colorId.TenMau,
                    TrangThai = colorId.TrangThai
                };
                return Ok(new
                {
                    status = 200,
                    message = "Cập nhật màu tóc thành công",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Cập nhật màu tóc thất bại",
                    error = ex.Message
                });
            }

        }
        [HttpDelete("color/{id}")]
        public async Task<IActionResult> DeleteColor(string id)
        {
            try
            {
                var delColor = await _context.Bangmaus.FindAsync(id);
                if (delColor == null)
                {
                    return NotFound(new
                    {
                        status = 404,
                        message = "Không tìm thấy màu tóc"
                    });
                }
                var lichSuLienQuan = await _context.Lichsuthutocs.Where(x => x.MaMau == id).ToListAsync();
                if (lichSuLienQuan.Any())
                {
                    _context.Lichsuthutocs.RemoveRange(lichSuLienQuan);
                }

                _context.Bangmaus.Remove(delColor);
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    status = 200,
                    message = "Xóa màu tóc thành công",
                    data = id
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    status = 400,
                    message = "Xóa màu tóc thất bại",
                    error = ex.Message
                });
            }
        }
        //QUẢN LÝ MÀU TÓC
    }
}
