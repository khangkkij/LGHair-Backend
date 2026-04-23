using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.DTO.Sale;
using MinhTamHair.Models;
using System;
using System.Linq;

namespace MinhTamHair.Controllers
{
   
    public class SaleRequestPayload : SaleRequest
    {
        public List<SaleRequestDetails> ChiTietFlashSales { get; set; } = new List<SaleRequestDetails>();
    }

    [Route("api/[controller]")]
    [ApiController]
    public class SaleController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        public SaleController(MinhTamHairContext context)
        {
            _context = context;
        }

        private string GenerateRandomMaFL()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            try
            {
                var sales = await _context.Flashsales
             .Where(x => x.TrangThai != 2 || x.NgayKetThuc > DateTime.Now)
              .ToListAsync();

                bool statusChanged = false;
                var dateNow = DateTime.Now;

                foreach (var s in sales)
                {
                    if (s.NgayKetThuc < dateNow && s.TrangThai != 2)
                    {
                        s.TrangThai = 2; 
                        statusChanged = true;
                    }
                    else if (s.NgayBatDau <= dateNow && s.NgayKetThuc >= dateNow)
                    {
                        if (s.TrangThai != 1)
                        {
                            s.TrangThai = 1;
                            statusChanged = true;
                        }
                    }
                    else if (s.NgayBatDau > dateNow && s.TrangThai != 0)
                    {
                        s.TrangThai = 0; 
                        statusChanged = true;
                    }
                }

                if (statusChanged)
                {
                    await _context.SaveChangesAsync();
                }

                var dataSale = _context.Flashsales
                    // Join FlashSale với ChiTiet
                    .Join(_context.ChitietFlashsales,
                        fs => fs.MaFl,
                        ct => ct.MaFl,
                        (fs, ct) => new { fs, ct })

                    // Left Join với DichVu
                    .GroupJoin(_context.Dichvus,
                        x => x.ct.MaDv,
                        dv => dv.MaDv,
                        (x, dvGroup) => new { x.fs, x.ct, dvGroup })
                    .SelectMany(
                        x => x.dvGroup.DefaultIfEmpty(),
                        (x, dv) => new { x.fs, x.ct, dv })

                    // Left Join với Combo
                    .GroupJoin(_context.Combos,
                        x => x.ct.MaCb,
                        cb => cb.MaCb,
                        (x, cbGroup) => new { x.fs, x.ct, x.dv, cbGroup })
                    .SelectMany(
                        x => x.cbGroup.DefaultIfEmpty(),
                        (x, cb) => new { x.fs, x.ct, x.dv, cb })

                    // GroupBy theo thông tin FlashSale
                    .GroupBy(x => new
                    {
                        x.fs.MaFl,
                        x.fs.NgayBatDau,
                        x.fs.NgayKetThuc,
                        x.fs.TrangThai
                    })
                    .Select(g => new SaleResponse
                    {
                        MaFL = g.Key.MaFl,
                        NgayBatDau = g.Key.NgayBatDau ?? DateTime.MinValue,
                        NgayKetThuc = g.Key.NgayKetThuc ?? DateTime.MinValue,
                        TrangThai = g.Key.TrangThai ?? 0,
                        TongDichVu = g.Count().ToString(),

                        ChiTietFlashSales = g.Select(x => new SaleResponseDetails
                        {
                          
                            MaDV = x.ct.MaDv,
                            MaCB = x.ct.MaCb,

                            TenDv = x.dv != null ? x.dv.TenDv : null,
                            TenCb = x.cb != null ? x.cb.TenCombo : null,

                            PhanTramGiam = (int)(x.ct.PhanTramGiam ?? 0),
                            Gia = x.dv != null ? (x.dv.Gia ?? 0) : (x.cb != null ? (x.cb.Gia ?? 0) : 0),

                            GiaSauGiam = x.dv != null
                                ? (x.dv.Gia ?? 0) - ((x.dv.Gia ?? 0) * (decimal)(x.ct.PhanTramGiam ?? 0) / 100)
                                : (x.cb != null ? (x.cb.Gia ?? 0) - ((x.cb.Gia ?? 0) * (decimal)(x.ct.PhanTramGiam ?? 0) / 100) : 0)
                        }).ToList()
                    })
            .ToList();

                return Ok(new
                {
                    Message = "Lấy dữ liệu thành công",
                    Data = dataSale,

                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy danh sách Flash Sale: " + ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetSaleById(string? id)
        {
            try
            {
                var sale = await _context.Flashsales.FirstOrDefaultAsync(x => x.MaFl == id);

                if (sale == null)
                {
                    return NotFound(new { message = "Không tìm thấy đợt Flash Sale này!" });
                }

                var dateNow = DateTime.Now;
                bool statusChanged = false;

                // Cập nhật trạng thái theo thời gian thực (0: Chưa bắt đầu, 1: Đang diễn ra, 2: Đã kết thúc)
                if (sale.NgayKetThuc < dateNow && sale.TrangThai != 2)
                {
                    sale.TrangThai = 2;
                    statusChanged = true;
                }
                else if (sale.NgayBatDau <= dateNow && sale.NgayKetThuc >= dateNow && sale.TrangThai != 1)
                {
                    sale.TrangThai = 1;
                    statusChanged = true;
                }
                else if (sale.NgayBatDau > dateNow && sale.TrangThai != 0)
                {
                    sale.TrangThai = 0;
                    statusChanged = true;
                }

                if (statusChanged)
                {
                    await _context.SaveChangesAsync();
                }

                var dataSale = _context.Flashsales
                    .Where(fs => fs.MaFl == id)
                    .Join(_context.ChitietFlashsales,
                        fs => fs.MaFl,
                        ct => ct.MaFl,
                        (fs, ct) => new { fs, ct })
                    .GroupJoin(_context.Dichvus,
                        x => x.ct.MaDv,
                        dv => dv.MaDv,
                        (x, dvGroup) => new { x.fs, x.ct, dvGroup })
                    .SelectMany(
                        x => x.dvGroup.DefaultIfEmpty(),
                        (x, dv) => new { x.fs, x.ct, dv })
                    .GroupJoin(_context.Combos,
                        x => x.ct.MaCb,
                        cb => cb.MaCb,
                        (x, cbGroup) => new { x.fs, x.ct, x.dv, cbGroup })
                    .SelectMany(
                        x => x.cbGroup.DefaultIfEmpty(),
                        (x, cb) => new { x.fs, x.ct, x.dv, cb })
                    .GroupBy(x => new
                    {
                        x.fs.MaFl,
                        x.fs.NgayBatDau,
                        x.fs.NgayKetThuc,
                        x.fs.TrangThai
                    })
                    .Select(g => new SaleResponse
                    {
                        MaFL = g.Key.MaFl,
                        NgayBatDau = g.Key.NgayBatDau ?? DateTime.MinValue,
                        NgayKetThuc = g.Key.NgayKetThuc ?? DateTime.MinValue,
                        TrangThai = g.Key.TrangThai ?? 0,
                        TongDichVu = g.Count().ToString(),

                        ChiTietFlashSales = g.Select(x => new SaleResponseDetails
                        {
                            // SỬA Ở ĐÂY: Trả về MaDV và MaCB cho FE
                            MaDV = x.ct.MaDv,
                            MaCB = x.ct.MaCb,

                            TenDv = x.dv != null ? x.dv.TenDv : null,
                            TenCb = x.cb != null ? x.cb.TenCombo : null,
                            PhanTramGiam = (int)(x.ct.PhanTramGiam ?? 0),
                            Gia = x.dv != null ? (x.dv.Gia ?? 0) : (x.cb != null ? (x.cb.Gia ?? 0) : 0),

                            GiaSauGiam = x.dv != null
                                ? (x.dv.Gia ?? 0) - ((x.dv.Gia ?? 0) * (decimal)(x.ct.PhanTramGiam ?? 0) / 100)
                                : (x.cb != null ? (x.cb.Gia ?? 0) - ((x.cb.Gia ?? 0) * (decimal)(x.ct.PhanTramGiam ?? 0) / 100) : 0)
                        }).ToList()
                    })
                    .FirstOrDefault();

                if (dataSale == null)
                {
                    return NotFound(new { message = "Không tìm thấy dữ liệu chi tiết!" });
                }

                return Ok(dataSale);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi lấy Flash Sale: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSale([FromBody] SaleRequestPayload addSale)
        {
            try
            {
                if (addSale == null) return BadRequest(new { message = "Dữ liệu không hợp lệ!" });

                if (addSale.NgayBatDau >= addSale.NgayKetThuc)
                    return BadRequest(new { message = "Ngày kết thúc phải lớn hơn ngày bắt đầu!" });

               
                bool isOverlap = await _context.Flashsales.AnyAsync(x =>
                    x.TrangThai != 2 && 
                    addSale.NgayBatDau < x.NgayKetThuc &&
                    addSale.NgayKetThuc > x.NgayBatDau);

                if (isOverlap)
                    return BadRequest(new { message = "Khoảng thời gian này bị trùng với một chương trình Flash Sale khác đang hoặc sắp diễn ra. Vui lòng chọn thời gian khác!" });

                if (addSale.ChiTietFlashSales == null || !addSale.ChiTietFlashSales.Any())
                    return BadRequest(new { message = "Phải có ít nhất 1 dịch vụ hoặc combo để sale!" });

                if (addSale.ChiTietFlashSales.Any(x =>
                    (!string.IsNullOrEmpty(x.MaDV) && !string.IsNullOrEmpty(x.MaCB)) ||
                    (string.IsNullOrEmpty(x.MaDV) && string.IsNullOrEmpty(x.MaCB))))
                {
                    return BadRequest(new { message = "Chỉ được chọn DUY NHẤT Dịch Vụ HOẶC Combo. Không được chọn cả 2 hoặc để trống." });
                }

                string createSaleId;
                do
                {
                    createSaleId = GenerateRandomMaFL();
                }
                while (await _context.Flashsales.AnyAsync(fs => fs.MaFl == createSaleId));

                var objSale = new Flashsale
                {
                    MaFl = createSaleId,
                    NgayBatDau = addSale.NgayBatDau,
                    NgayKetThuc = addSale.NgayKetThuc,
                    TrangThai = addSale.TrangThai = 1,
                };
                _context.Flashsales.Add(objSale);

                var chiTietSale = addSale.ChiTietFlashSales.Select(x => new ChitietFlashsale
                {
                    MaFl = createSaleId,
                    MaDv = string.IsNullOrEmpty(x.MaDV) ? null : x.MaDV,
                    MaCb = string.IsNullOrEmpty(x.MaCB) ? null : x.MaCB,
                    PhanTramGiam = (int?)x.PhanTramGiam
                }).ToList();

                _context.ChitietFlashsales.AddRange(chiTietSale);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Tạo Flash Sale thành công!" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi tạo Flash Sale: " + ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSale(string id, [FromBody] SaleRequestPayload updateDto)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var saleExisting = await _context.Flashsales
                        .FirstOrDefaultAsync(x => x.MaFl == id);

                    if (saleExisting == null)
                        return NotFound(new { message = "Không tìm thấy chương trình khuyến mãi." });

                    if (updateDto.NgayBatDau >= updateDto.NgayKetThuc)
                        return BadRequest(new { message = "Ngày kết thúc phải lớn hơn ngày bắt đầu." });

                   
                    bool isOverlap = await _context.Flashsales.AnyAsync(x =>
                        x.MaFl != id &&     
                        x.TrangThai != 2 && 
                        updateDto.NgayBatDau < x.NgayKetThuc &&
                        updateDto.NgayKetThuc > x.NgayBatDau);

                    if (isOverlap)
                        return BadRequest(new { message = "Khoảng thời gian này bị trùng với một chương trình Flash Sale khác đang hoặc sắp diễn ra. Vui lòng chọn thời gian khác!" });

                    if (updateDto.ChiTietFlashSales == null || !updateDto.ChiTietFlashSales.Any())
                        return BadRequest(new { message = "Danh sách áp dụng không được để trống." });

                    if (updateDto.ChiTietFlashSales.Any(x => x.PhanTramGiam <= 0 || x.PhanTramGiam > 100))
                        return BadRequest(new { message = "Mức giảm giá không hợp lệ (1-100%)." });

                    if (updateDto.ChiTietFlashSales.Any(x =>
                       (!string.IsNullOrEmpty(x.MaDV) && !string.IsNullOrEmpty(x.MaCB)) ||
                       (string.IsNullOrEmpty(x.MaDV) && string.IsNullOrEmpty(x.MaCB))))
                    {
                        return BadRequest(new { message = "Chỉ được chọn DUY NHẤT Dịch Vụ HOẶC Combo. Không được chọn cả 2 hoặc để trống." });
                    }

                    saleExisting.NgayBatDau = updateDto.NgayBatDau;
                    saleExisting.NgayKetThuc = updateDto.NgayKetThuc;
                    saleExisting.TrangThai = updateDto.TrangThai;

                    await _context.ChitietFlashsales
                        .Where(x => x.MaFl == id)
                        .ExecuteDeleteAsync();

                    var newDetails = updateDto.ChiTietFlashSales.Select(x => new ChitietFlashsale
                    {
                        MaFl = id,
                        MaDv = string.IsNullOrEmpty(x.MaDV) ? null : x.MaDV,
                        MaCb = string.IsNullOrEmpty(x.MaCB) ? null : x.MaCB,
                        PhanTramGiam = (int?)x.PhanTramGiam
                    });

                    await _context.ChitietFlashsales.AddRangeAsync(newDetails);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new { message = "Cập nhật FlashSale thành công" });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new { message = "Lỗi hệ thống khi cập nhật FlashSale: " + ex.Message });
                }
            });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSale(string id)
        {
            try
            {
                var sale = await _context.Flashsales.FindAsync(id);
                if (sale == null)
                {
                    return NotFound(new { message = "Flash sale không tồn tại" });
                }

                await _context.ChitietFlashsales
                    .Where(ct => ct.MaFl == id)
                    .ExecuteDeleteAsync();

                _context.Flashsales.Remove(sale);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Xóa flash sale thành công" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Lỗi khi xóa Flash sale: " + ex.Message });
            }
        }
    }
}