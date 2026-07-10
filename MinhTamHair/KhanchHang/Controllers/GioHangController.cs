using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MinhTamHair.Models;
using MinhTamHair.Models.DTOs;
using System.Security.Claims;
using System.Text.Json;

namespace MinhTamHair.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GioHangController : ControllerBase
    {
        private readonly MinhTamHairContext _context;
        private readonly IDistributedCache _cache;

        public GioHangController(MinhTamHairContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ========================================================
        // HÀM TRỢ GIÚP: LẤY THÔNG TIN ĐỊNH DANH (GUEST HOẶC USER)
        // ========================================================
        private (string key, string ownerName, bool isAuthenticated, string? userId) GetCartContext()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var userId = User.FindFirstValue("MaKh") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
                return ($"Cart_User_{userId}", $"User: {userId}", true, userId);
            }

            var guestId = Request.Headers["X-Guest-Id"].ToString();
            if (string.IsNullOrEmpty(guestId))
            {
                throw new BadHttpRequestException("Thiếu header X-Guest-Id cho khách chưa đăng nhập.");
            }
            return ($"Cart_Guest_{guestId}", $"Guest: {guestId}", false, null);
        }

        private async Task SaveCartToCacheAsync(string key, GioHangDto cart)
        {
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) };
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(cart), options);
        }

        // ========================================================
        // 1. API: TRUY XUẤT CHI TIẾT GIỎ HÀNG (BIẾT RÕ CỦA AI, CÓ GÌ)
        // ========================================================
        [HttpGet("chi-tiet")]
        public async Task<IActionResult> GetCart()
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                var cart = string.IsNullOrEmpty(cachedData)
                    ? new GioHangDto { CartOwner = ctx.ownerName }
                    : JsonSerializer.Deserialize<GioHangDto>(cachedData)!;

                cart.CartOwner = ctx.ownerName;

                if (ctx.isAuthenticated && ctx.userId != null)
                {
                    // GIỮ LẠI CẢ DỊCH VỤ VÀ COMBO TỪ CACHE
                    var phanCacheDuocGiuLai = cart.Items.Where(x => x.Loai == "DICHVU" || x.Loai == "COMBO").ToList(); 

                    var phanBienTheDb = await _context.ChitietGiohangs
                        .Where(c => c.MaGhNavigation.MaKh == ctx.userId)
                        .Select(c => new GioHangItemDto
                        {
                            Id = c.MaBienThe,
                            Loai = "BIENTHE",
                            Ten = c.MaBienTheNavigation.TenBienThe ?? "Sản phẩm",
                            Gia = c.MaBienTheNavigation.GiaBan ?? 0,
                            HinhAnh = c.MaBienTheNavigation.HinhAnhDaiDien,
                            SoLuong = c.SoLuong ?? 0
                        }).ToListAsync();

                    cart.Items = phanBienTheDb.Concat(phanCacheDuocGiuLai).ToList();
                    await SaveCartToCacheAsync(ctx.key, cart); 
                }

                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message }); 
            }
        }

        // ========================================================
        // 2. API: THÊM BIẾN THỂ SẢN PHẨM VÀO GIỎ
        // ========================================================
        [HttpPost("them-bien-the")]
        public async Task<IActionResult> AddBienThe([FromBody] ThemBienTheRequest request)
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                var cart = string.IsNullOrEmpty(cachedData) ? new GioHangDto { CartOwner = ctx.ownerName } : JsonSerializer.Deserialize<GioHangDto>(cachedData)!;

                var existingItem = cart.Items.FirstOrDefault(x => x.Id == request.MaBienThe && x.Loai == "BIENTHE");
                if (existingItem != null)
                {
                    existingItem.SoLuong += request.SoLuong;
                }
                else
                {
                    var bt = await _context.BienTheSanphams.FirstOrDefaultAsync(x => x.MaBienThe == request.MaBienThe);
                    if (bt == null) return NotFound(new { message = "Không tìm thấy biến thể sản phẩm." });

                    cart.Items.Add(new GioHangItemDto
                    {
                        Id = bt.MaBienThe,
                        Loai = "BIENTHE",
                        Ten = bt.TenBienThe ?? "Sản phẩm",
                        Gia = bt.GiaBan ?? 0,
                        HinhAnh = bt.HinhAnhDaiDien,
                        SoLuong = request.SoLuong
                    });
                }

                // Lưu vào Cache
                await SaveCartToCacheAsync(ctx.key, cart);

                // Nếu ĐÃ ĐĂNG NHẬP -> Lưu trực tiếp vào Database bảng CHITIET_GIOHANG
                if (ctx.isAuthenticated && ctx.userId != null)
                {
                    await LuuBienTheVaoSqlAsync(ctx.userId, request.MaBienThe, request.SoLuong);
                }

                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ========================================================
        // 3. API: THÊM DỊCH VỤ VÀO GIỎ (LƯU QUA CACHE)
        // ========================================================
        [HttpPost("them-dich-vu")]
        public async Task<IActionResult> AddDichVu([FromBody] ThemDichVuRequest request)
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                var cart = string.IsNullOrEmpty(cachedData) ? new GioHangDto { CartOwner = ctx.ownerName } : JsonSerializer.Deserialize<GioHangDto>(cachedData)!;

                var existingItem = cart.Items.FirstOrDefault(x => x.Id == request.MaDv && x.Loai == "DICHVU");
                if (existingItem != null)
                {
                    existingItem.SoLuong += request.SoLuong;
                }
                else
                {
                    var dv = await _context.Dichvus.Include(d => d.Hinhanhs).FirstOrDefaultAsync(x => x.MaDv == request.MaDv);
                    if (dv == null) return NotFound(new { message = "Không tìm thấy dịch vụ." });

                    cart.Items.Add(new GioHangItemDto
                    {
                        Id = dv.MaDv,
                        Loai = "DICHVU",
                        Ten = dv.TenDv ?? "Dịch vụ",
                        Gia = dv.Gia ?? 0,
                        HinhAnh = dv.Hinhanhs.FirstOrDefault()?.DuongDan,
                        SoLuong = request.SoLuong
                    });
                }

                // Cả Guest và User đều lưu phần Dịch vụ thông qua Cache hệ thống
                await SaveCartToCacheAsync(ctx.key, cart);
                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ========================================================
        // 4. API: ĐỒNG BỘ GỘP GIỎ HÀNG NGAY KHI USER ĐĂNG NHẬP
        // ========================================================
        [HttpPost("dong-bo-khi-login")]
        public async Task<IActionResult> SyncCartAfterLogin([FromHeader(Name = "X-Guest-Id")] string guestId)
        {
            if (User.Identity == null || !User.Identity.IsAuthenticated)
                return Unauthorized(new { message = "Vui lòng đính kèm token đăng nhập để đồng bộ." });

            var userId = User.FindFirstValue("MaKh") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            var guestKey = $"Cart_Guest_{guestId}";
            var userKey = $"Cart_User_{userId}";

            // 1. Lấy dữ liệu giỏ ẩn danh (Guest)
            var cachedGuest = await _cache.GetStringAsync(guestKey);
            if (string.IsNullOrEmpty(cachedGuest))
                return Ok(new { message = "Giỏ hàng tạm thời trống, không cần gộp." });

            var guestCart = JsonSerializer.Deserialize<GioHangDto>(cachedGuest)!;

            // 2. Lấy hoặc tạo mới dữ liệu giỏ thành viên (User)
            var cachedUser = await _cache.GetStringAsync(userKey);
            var userCart = string.IsNullOrEmpty(cachedUser)
                ? new GioHangDto { CartOwner = $"User: {userId}" }
                : JsonSerializer.Deserialize<GioHangDto>(cachedUser)!;

            // 3. Tiến hành duyệt qua từng món đồ từ giỏ Guest để gộp
            foreach (var item in guestCart.Items)
            {
                // Kiểm tra xem trong giỏ hiện tại của User đã có mặt hàng này chưa
                var existingInUser = userCart.Items.FirstOrDefault(x => x.Id == item.Id && x.Loai == item.Loai);
                if (existingInUser != null)
                {
                    existingInUser.SoLuong += item.SoLuong;
                }
                else
                {
                    userCart.Items.Add(item);
                }

                // NẾU LÀ BIẾN THỂ SẢN PHẨM -> Đẩy trực tiếp thông tin lưu xuống Database SQL Server
                if (item.Loai == "BIENTHE")
                {
                    await LuuBienTheVaoSqlAsync(userId, item.Id, item.SoLuong);
                }
            }

            // 4. Cập nhật lại Cache của User và Xóa Cache tạm của Guest
            await SaveCartToCacheAsync(userKey, userCart);
            await _cache.RemoveAsync(guestKey);

            return Ok(userCart);
        }

        // ========================================================
        // 5. API: XÓA TỪNG MÓN CỤ THỂ THEO ID VÀ LOẠI
        // ========================================================
        [HttpDelete("xoa-item")]
        public async Task<IActionResult> RemoveItem([FromBody] XoaItemRequest request)
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                if (string.IsNullOrEmpty(cachedData)) return NotFound(new { message = "Giỏ hàng trống." });

                var cart = JsonSerializer.Deserialize<GioHangDto>(cachedData)!;
                var target = cart.Items.FirstOrDefault(x => x.Id == request.Id && x.Loai == request.Loai);

                if (target == null) return NotFound(new { message = "Không tìm thấy mặt hàng này trong giỏ." });

                cart.Items.Remove(target);
                await SaveCartToCacheAsync(ctx.key, cart);

                // Nếu đã đăng nhập và xóa Sản phẩm biến thể -> Xóa luôn ở Database
                if (ctx.isAuthenticated && ctx.userId != null && request.Loai == "BIENTHE")
                {
                    var chiTiet = await _context.ChitietGiohangs
                        .FirstOrDefaultAsync(c => c.MaGhNavigation.MaKh == ctx.userId && c.MaBienThe == request.Id);
                    if (chiTiet != null)
                    {
                        _context.ChitietGiohangs.Remove(chiTiet);
                        await _context.SaveChangesAsync();
                    }
                }

                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ========================================================
        // 6. API: XÓA SẠCH GIỎ HÀNG (CLEAR ALL)
        // ========================================================
        [HttpDelete("xoa-sach")]
        public async Task<IActionResult> ClearCart([FromQuery] string? loai)
        {
            try
            {
                var ctx = GetCartContext();
                if (string.IsNullOrEmpty(loai))
                {
                    // Trường hợp 1: Không truyền 'loai' -> Xóa sạch toàn bộ (cả cache và SQL DB)
                    await _cache.RemoveAsync(ctx.key);
                    if (ctx.isAuthenticated && ctx.userId != null)
                    {
                        var chiTiets = await _context.ChitietGiohangs
                            .Where(c => c.MaGhNavigation.MaKh == ctx.userId)
                            .ToListAsync();
                        if (chiTiets.Any())
                        {
                            _context.ChitietGiohangs.RemoveRange(chiTiets);
                            await _context.SaveChangesAsync();
                        }
                    }
                    return Ok(new GioHangDto { CartOwner = ctx.ownerName });
                }
                else
                {
                    // Trường hợp 2: Có truyền 'loai' -> Chỉ lọc xóa đúng loại được yêu cầu
                    var cachedData = await _cache.GetStringAsync(ctx.key);
                    if (!string.IsNullOrEmpty(cachedData))
                    {
                        var cart = JsonSerializer.Deserialize<GioHangDto>(cachedData)!;
                        if (loai == "BIENTHE")
                        {
                            // Chỉ lọc bỏ các sản phẩm BIENTHE trong Cache, giữ lại Dịch vụ/Combo
                            cart.Items = cart.Items.Where(x => x.Loai != "BIENTHE").ToList();
                        }
                        else if (loai == "DICHVU")
                        {
                            // Chỉ lọc bỏ Dịch vụ (DICHVU) và Combo (COMBO) trong Cache, giữ lại Sản phẩm
                            cart.Items = cart.Items.Where(x => x.Loai != "DICHVU" && x.Loai != "COMBO").ToList();
                        }
                        await SaveCartToCacheAsync(ctx.key, cart);
                    }
                    // Nếu loại xóa là sản phẩm (BIENTHE) và đã đăng nhập -> Xóa các sản phẩm trong SQL Database
                    if (loai == "BIENTHE" && ctx.isAuthenticated && ctx.userId != null)
                    {
                        var chiTiets = await _context.ChitietGiohangs
                            .Where(c => c.MaGhNavigation.MaKh == ctx.userId)
                            .ToListAsync();
                        if (chiTiets.Any())
                        {
                            _context.ChitietGiohangs.RemoveRange(chiTiets);
                            await _context.SaveChangesAsync();
                        }
                    }
                    // Trả về giỏ hàng mới sau khi đã lọc xóa
                    var updatedCachedData = await _cache.GetStringAsync(ctx.key);
                    var updatedCart = string.IsNullOrEmpty(updatedCachedData)
                        ? new GioHangDto { CartOwner = ctx.ownerName }
                        : JsonSerializer.Deserialize<GioHangDto>(updatedCachedData)!;
                    return Ok(updatedCart);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ==========================================
        // HÀM PRIVATE: LOGIC GHI NHẬN BIẾN THỂ VÀO SQL
        // ==========================================
        private async Task LuuBienTheVaoSqlAsync(string maKh, string maBienThe, int soLuong)
        {
            var gioHang = await _context.Giohangs.FirstOrDefaultAsync(g => g.MaKh == maKh);
            if (gioHang == null)
            {
                gioHang = new Giohang { MaKh = maKh };
                _context.Giohangs.Add(gioHang);
                await _context.SaveChangesAsync();
            }

            var chiTiet = await _context.ChitietGiohangs
                .FirstOrDefaultAsync(c => c.MaGh == gioHang.MaGh && c.MaBienThe == maBienThe);

            if (chiTiet != null)
            {
                chiTiet.SoLuong += soLuong;
            }
            else
            {
                _context.ChitietGiohangs.Add(new ChitietGiohang
                {
                    MaGh = gioHang.MaGh,
                    MaBienThe = maBienThe,
                    SoLuong = soLuong
                });
            }
            await _context.SaveChangesAsync();
        }
        [HttpPost("them-combo")]
        public async Task<IActionResult> AddCombo([FromBody] ThemComboRequest request)
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                var cart = string.IsNullOrEmpty(cachedData) ? new GioHangDto { CartOwner = ctx.ownerName } : JsonSerializer.Deserialize<GioHangDto>(cachedData)!;

                var existingItem = cart.Items.FirstOrDefault(x => x.Id == request.MaCb && x.Loai == "COMBO");
                if (existingItem != null)
                {
                    existingItem.SoLuong += request.SoLuong;
                }
                else
                {
                    // LƯU Ý: Đảm bảo DbContext của bạn có DbSet<Combo> Combos
                    var cb = await _context.Combos.FirstOrDefaultAsync(x => x.MaCb == request.MaCb);
                    if (cb == null) return NotFound(new { message = "Không tìm thấy combo." });

                    cart.Items.Add(new GioHangItemDto
                    {
                        Id = cb.MaCb,
                        Loai = "COMBO",
                        Ten = cb.TenCombo ?? "Combo Dịch Vụ",
                        Gia = cb.Gia ?? 0,
                        HinhAnh = cb.HinhAnh, // Thay đổi trường này cho khớp Model Combo của bạn
                        SoLuong = request.SoLuong
                    });
                }

                await SaveCartToCacheAsync(ctx.key, cart);
                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ========================================================
        // TÍNH NĂNG MỚI: CẬP NHẬT TĂNG GIẢM SỐ LƯỢNG
        // ========================================================
        [HttpPut("cap-nhat-so-luong")]
        public async Task<IActionResult> UpdateQuantity([FromBody] CapNhatSoLuongRequest request)
        {
            try
            {
                var ctx = GetCartContext();
                var cachedData = await _cache.GetStringAsync(ctx.key);
                if (string.IsNullOrEmpty(cachedData)) return NotFound(new { message = "Giỏ hàng trống." });

                var cart = JsonSerializer.Deserialize<GioHangDto>(cachedData)!;
                var existingItem = cart.Items.FirstOrDefault(x => x.Id == request.Id && x.Loai == request.Loai);

                if (existingItem == null) return NotFound(new { message = "Không tìm thấy mặt hàng trong giỏ." });

                if (request.SoLuong <= 0)
                {
                    cart.Items.Remove(existingItem);
                }
                else
                {
                    existingItem.SoLuong = request.SoLuong;
                }

                await SaveCartToCacheAsync(ctx.key, cart);

                // Cập nhật Database nếu là Sản Phẩm và đã đăng nhập
                if (ctx.isAuthenticated && ctx.userId != null && request.Loai == "BIENTHE")
                {
                    var chiTiet = await _context.ChitietGiohangs
                        .FirstOrDefaultAsync(c => c.MaGhNavigation.MaKh == ctx.userId && c.MaBienThe == request.Id);

                    if (chiTiet != null)
                    {
                        if (request.SoLuong <= 0)
                        {
                            _context.ChitietGiohangs.Remove(chiTiet);
                        }
                        else
                        {
                            chiTiet.SoLuong = request.SoLuong;
                        }
                        await _context.SaveChangesAsync();
                    }
                }

                return Ok(cart);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}