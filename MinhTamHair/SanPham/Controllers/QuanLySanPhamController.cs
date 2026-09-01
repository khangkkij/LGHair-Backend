using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using MinhTamHair.SanPham.Controllers;
using MinhTamHair.Services.UploadIMG;

namespace MinhTamHair.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuanLySanPhamController : ControllerBase
{
    private readonly MinhTamHairContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly IUploadDVSP _uploadImg;
    private readonly IProductCacheService _cache;

    public QuanLySanPhamController(
        MinhTamHairContext context, IWebHostEnvironment env, IUploadDVSP uploadImg, IProductCacheService cache)
    {
        _context = context;
        _env = env;
        _uploadImg = uploadImg;
        _cache = cache;
    }

    // Upload ảnh lên Cloudinary qua service IUploadDVSP (riêng cho module Sản phẩm).
    // Trả về SecureUrl (link đầy đủ https) để lưu thẳng vào DB.
    // Lưu ý: IUploadDVSP.AddIMG hiện upload cố định vào folder "DVSP" trên Cloudinary
    // (tham số subfolder không còn tác dụng phân thư mục nữa, giữ lại để không phải sửa
    // toàn bộ chỗ gọi hàm này trong file).
    private async Task<string?> SaveImageAsync(IFormFile? file, string subfolder)
    {
        if (file == null || file.Length == 0)
        {
            Console.WriteLine("[SaveImageAsync] Không có file (null hoặc rỗng).");
            return null;
        }
        var allowed = new[] { "image/jpeg", "image/png", "image/webp", "image/gif" };
        if (!allowed.Contains(file.ContentType))
        {
            Console.WriteLine($"[SaveImageAsync] ContentType không hợp lệ: {file.ContentType}");
            return null;
        }

        var result = await _uploadImg.AddIMG(file);
        if (result?.Error != null)
        {
            Console.WriteLine($"[SaveImageAsync] Cloudinary trả lỗi: {result.Error.Message}");
            return null;
        }
        if (string.IsNullOrEmpty(result?.SecureUrl?.ToString()))
        {
            Console.WriteLine($"[SaveImageAsync] Upload không có lỗi nhưng SecureUrl rỗng. StatusCode={result?.StatusCode}");
            return null;
        }

        Console.WriteLine($"[SaveImageAsync] Upload OK: {result.SecureUrl}");
        return result.SecureUrl.ToString();
    }

    // Xóa ảnh trên Cloudinary dựa vào URL đã lưu trong DB.
    // Cloudinary cần "public ID" (không phải URL) để xóa, nên phải suy ngược lại từ URL.
    private async Task DeleteImageFile(string? imageUrl)
    {
        var publicId = GetPublicIdFromUrl(imageUrl);
        if (publicId == null) return;

        await _uploadImg.DelIMG(publicId);
    }

    // URL Cloudinary dạng:
    // https://res.cloudinary.com/<cloud_name>/image/upload/v1234567890/minhtamhair/sanpham/abc123.jpg
    // Public ID là phần "minhtamhair/sanpham/abc123" (bỏ domain, "v<version>/" và phần đuôi mở rộng).
    private static string? GetPublicIdFromUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.Contains("res.cloudinary.com"))
            return null;

        var uploadIndex = imageUrl.IndexOf("/upload/", StringComparison.OrdinalIgnoreCase);
        if (uploadIndex < 0) return null;

        var afterUpload = imageUrl[(uploadIndex + "/upload/".Length)..];

        // Bỏ segment version (v1234567890/) nếu có
        var parts = afterUpload.Split('/');
        var startIdx = (parts.Length > 0 && parts[0].StartsWith('v') && parts[0].Length > 1 && parts[0][1..].All(char.IsDigit))
            ? 1
            : 0;

        var withoutVersion = string.Join('/', parts[startIdx..]);
        var lastDot = withoutVersion.LastIndexOf('.');
        return lastDot > 0 ? withoutVersion[..lastDot] : withoutVersion;
    }

    // Sinh mã tự động dạng PREFIX + số thứ tự (VD: SP01, SP02...).
    // Lấy toàn bộ mã có prefix phù hợp, tách phần số, lấy giá trị lớn nhất rồi +1,
    // tránh trường hợp trùng mã khi có mã cũ không đúng định dạng số hoặc bị xóa ở giữa.
    private async Task<string> SinhMaTuDongAsync(string prefix, int soChuSo)
    {
        var maList = prefix == "SP"
            ? await _context.SanPhams.Select(s => s.MaSp).Where(m => m.StartsWith(prefix)).ToListAsync()
            : await _context.BienTheSanphams.Select(b => b.MaBienThe).Where(m => m.StartsWith(prefix)).ToListAsync();

        var maxSo = 0;
        foreach (var ma in maList)
        {
            var phanSo = ma.Substring(prefix.Length);
            if (int.TryParse(phanSo, out var so) && so > maxSo)
                maxSo = so;
        }

        var soMoi = maxSo + 1;
        return prefix + soMoi.ToString().PadLeft(soChuSo, '0');
    }

    // Lấy phần số đứng sau prefix chữ cái để sort theo giá trị số thực,
    // tránh sort sai khi mã có độ dài khác nhau (VD "SP100" đứng trước "SP99" nếu so chuỗi).
    private static int LayPhanSoTuMa(string? ma)
    {
        if (string.IsNullOrEmpty(ma)) return 0;
        var phanSo = new string(ma.Where(char.IsDigit).ToArray());
        return int.TryParse(phanSo, out var so) ? so : 0;
    }


    [HttpGet("danhmuc")]
    public async Task<IActionResult> GetDanhMuc()
    {
        var list = await _context.DanhMucSPs
            .AsNoTracking()
            .Where(d => d.TrangThai == 1) // chỉ lấy danh mục đang hiện, loại banner (2) và danh mục tạm ẩn (0)
            .OrderBy(d => d.TenDanhMuc)
            .Select(d => new { d.MaDMSP, d.TenDanhMuc, d.TrangThai })
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("thuonghieu")]
    public async Task<IActionResult> GetThuongHieu()
    {
        var list = await _context.Thuonghieus
            .AsNoTracking()
            .OrderBy(t => t.TenThuongHieu)
            .Select(t => new { t.MaTh, t.TenThuongHieu, t.TrangThai })
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? keyword,
        [FromQuery] string? maDanhMuc,
        [FromQuery] string? maTH,
        [FromQuery] int? trangThai,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100; // chặn client xin quá nhiều 1 lần

        var query = _context.SanPhams.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(s => s.TenSp!.Contains(keyword) || s.MaSp.Contains(keyword));

        if (!string.IsNullOrWhiteSpace(maDanhMuc))
            query = query.Where(s => s.MaDanhMuc == maDanhMuc);

        if (!string.IsNullOrWhiteSpace(maTH))
            query = query.Where(s => s.MaTh == maTH);

        if (trangThai.HasValue)
            query = query.Where(s => s.TrangThai == trangThai.Value);

        // Đếm tổng số bản ghi khớp filter (1 câu SELECT COUNT)
        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        // Phân trang và sắp xếp trực tiếp trên SQL Server
        var list = await query
            .OrderByDescending(s => s.MaSp.Length)
            .ThenByDescending(s => s.MaSp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.MaSp,
                s.TenSp,
                s.MaDanhMuc,
                s.MaTh,
                s.HinhAnhDaiDien,
                s.TrangThai,
                SoLuongBienThe = s.BienTheSanphams.Count(bt => bt.TrangThai == 1),
            })
            .ToListAsync();

        return Ok(new { data = list, total, totalPages, page, pageSize });
    }

    // Tạo sản phẩm luôn phải kèm theo biến thể đầu tiên trong CÙNG 1 request:
    // sản phẩm chỉ là thông tin chung, đơn vị thực sự được bán là biến thể.
    // Toàn bộ được bọc trong 1 transaction — nếu bước tạo biến thể lỗi thì
    // rollback luôn sản phẩm, tránh phát sinh "sản phẩm mồ côi" không có biến thể nào để bán.
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateSanPhamDto dto)
    {
        // "Tên biến thể" (phân loại) không bắt buộc phía người dùng — UI không nhắc khái niệm
        // "biến thể" khi tạo sản phẩm đơn giản, nên nếu thiếu thì fallback về "Mặc định"
        // thay vì chặn request. Giá bán vẫn bắt buộc vì không thể suy luận ra giá trị mặc định hợp lý.
        var tenBienThe = string.IsNullOrWhiteSpace(dto.TenBienThe) ? "Mặc định" : dto.TenBienThe.Trim();
        if (dto.GiaBan == null || dto.GiaBan <= 0)
            return BadRequest(new { message = "Vui lòng nhập giá bán hợp lệ." });

        // Dùng CreateExecutionStrategy() vì DbContext đã bật EnableRetryOnFailure ở Program.cs.
        // Không được BeginTransactionAsync() trực tiếp trong trường hợp này, vì execution strategy
        // cần kiểm soát toàn bộ transaction để có thể tự retry lại từ đầu khi gặp lỗi transient.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var maSpMoi = await SinhMaTuDongAsync("SP", 2);
                var imgPath = await SaveImageAsync(dto.File, "sanpham");

                var sp = new Models.SanPham
                {
                    MaSp = maSpMoi,
                    TenSp = dto.TenSp?.Trim(),
                    MaDanhMuc = dto.MaDanhMuc,
                    MaTh = string.IsNullOrWhiteSpace(dto.MaTh) ? null : dto.MaTh,
                    MoTa = dto.MoTa?.Trim(),
                    ThanhPhan = dto.ThanhPhan?.Trim(),
                    HuongDanSuDung = dto.HuongDanSuDung?.Trim(),
                    HinhAnhDaiDien = imgPath,
                    TrangThai = dto.TrangThai ?? 1,
                };
                _context.SanPhams.Add(sp);
                await _context.SaveChangesAsync();

                // Ảnh gallery của sản phẩm
                if (dto.GalleryFiles != null && dto.GalleryFiles.Count > 0)
                {
                    foreach (var gf in dto.GalleryFiles.Take(10))
                    {
                        var gPath = await SaveImageAsync(gf, "sanpham_gallery");
                        if (gPath != null)
                            _context.Hinhanhsps.Add(new Hinhanhsp { MaSanPham = sp.MaSp, DuongDan = gPath });
                    }
                    await _context.SaveChangesAsync();
                }

                // Giá bán & tồn kho ban đầu — lưu vào 1 biến thể mặc định, tạo ngay trong cùng transaction
                var maBtMoi = await SinhMaTuDongAsync("BT", 2);

                // Ảnh biến thể: nếu người dùng có upload ảnh riêng cho biến thể thì dùng ảnh đó,
                // dù đây là biến thể "Mặc định" (không nhập Màu/Dung tích) hay biến thể có nhập
                // Màu/Dung tích riêng — cả 2 trường hợp đều xử lý giống nhau. Nếu không upload,
                // fallback lấy luôn ảnh đại diện của sản phẩm vừa tạo ở trên, tránh để trống ảnh.
                var btImgPath = await SaveImageAsync(dto.BienTheFile, "bienthe") ?? sp.HinhAnhDaiDien;

                var bt = new BienTheSanpham
                {
                    MaBienThe = maBtMoi,
                    MaSanPham = sp.MaSp,
                    TenBienThe = tenBienThe,
                    GiaBan = dto.GiaBan ?? 0,
                    SoLuongTon = dto.SoLuong ?? 0,
                    PhanTramHoaHong = dto.PhanTramHoaHong,
                    HinhAnhDaiDien = btImgPath,
                    TrangThai = 1,
                };
                ChuanHoaTrangThaiBienThe(bt);
                _context.BienTheSanphams.Add(bt);
                await _context.SaveChangesAsync();

                if (dto.BienTheGalleryFiles != null && dto.BienTheGalleryFiles.Count > 0)
                {
                    foreach (var gf in dto.BienTheGalleryFiles.Take(10))
                    {
                        var gPath = await SaveImageAsync(gf, "bienthe_gallery");
                        if (gPath != null)
                            _context.HinhanhBTs.Add(new HinhanhBT { MaBienThe = bt.MaBienThe, DuongDan = gPath });
                    }
                    await _context.SaveChangesAsync();
                }

                if (bt.SoLuongTon.GetValueOrDefault() > 0)
                {
                    _context.LichSuTonKhos.Add(new LichSuTonKho
                    {
                        MaBienThe = bt.MaBienThe,
                        LoaiBienDong = "DIEU_CHINH",
                        SoLuongThayDoi = bt.SoLuongTon!.Value,
                        TonTruoc = 0,
                        TonSau = bt.SoLuongTon.Value,
                        MaChungTu = null,
                        MaNv = dto.MaNv,
                        GhiChu = "Tạo biến thể mới cùng sản phẩm",
                        ThoiGian = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Xóa cache SAU khi transaction commit thành công — tránh xóa cache rồi lại rollback,
                // vốn sẽ để lại cache trống trong khi DB thực ra chưa có gì thay đổi.
                await _cache.InvalidateAllProductCacheAsync();

                return Ok(new
                {
                    message = "Thêm sản phẩm và biến thể thành công.",
                    maSp = sp.MaSp,
                    maBienThe = bt.MaBienThe
                });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromForm] UpdateSanPhamDto dto)
    {
        var sp = await _context.SanPhams.FindAsync(id);
        if (sp == null) return NotFound(new { message = "Không tìm thấy sản phẩm." });

        sp.TenSp = dto.TenSp?.Trim() ?? sp.TenSp;
        sp.MaDanhMuc = dto.MaDanhMuc ?? sp.MaDanhMuc;
        sp.MaTh = string.IsNullOrWhiteSpace(dto.MaTh) ? null : dto.MaTh;
        sp.MoTa = dto.MoTa?.Trim() ?? sp.MoTa;
        sp.ThanhPhan = dto.ThanhPhan?.Trim() ?? sp.ThanhPhan;
        sp.HuongDanSuDung = dto.HuongDanSuDung?.Trim() ?? sp.HuongDanSuDung;
        sp.TrangThai = dto.TrangThai ?? sp.TrangThai;

        if (dto.File != null && dto.File.Length > 0)
        {
            await DeleteImageFile(sp.HinhAnhDaiDien);
            sp.HinhAnhDaiDien = await SaveImageAsync(dto.File, "sanpham");
        }

        // Thêm ảnh gallery mới
        if (dto.GalleryFiles != null && dto.GalleryFiles.Count > 0)
        {
            foreach (var gf in dto.GalleryFiles.Take(10))
            {
                var gPath = await SaveImageAsync(gf, "sanpham_gallery");
                if (gPath != null)
                    _context.Hinhanhsps.Add(new Hinhanhsp { MaSanPham = id, DuongDan = gPath });
            }
        }

        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Cập nhật thành công." });
    }

    // Xóa sản phẩm phải dọn sạch TOÀN BỘ dữ liệu con theo đúng thứ tự (từ lá lên gốc) trong
    // 1 transaction, KHÔNG trông cậy vào cascade delete của SQL Server: FK
    // "BienTheSanpham.MaSanPham -> SanPham.MaSp" và "LichSuTonKho.MaBienThe ->
    // BienTheSanpham.MaBienThe" trong DB thực tế đang ở chế độ SET NULL (không phải CASCADE),
    // vì cả 2 cột FK đều nullable — nên trước đây "_context.SanPhams.Remove(sp)" xóa được
    // sản phẩm "thành công" nhưng để lại BienTheSanpham/LichSuTonKho/HinhanhBT mồ côi
    // (MaSanPham/MaBienThe bị SQL tự set NULL) thay vì báo lỗi hoặc dọn theo.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var sp = await _context.SanPhams.Include(s => s.BienTheSanphams).ThenInclude(bt => bt.HinhanhBTs).Include(s => s.Hinhanhsps)
            .FirstOrDefaultAsync(s => s.MaSp == id);

        if (sp == null)
            return NotFound(new { message = "Không tìm thấy sản phẩm." });

        var maBienThes = sp.BienTheSanphams
            .Select(bt => bt.MaBienThe)
            .ToList();

        bool daPhatSinhDuLieu =
            await _context.ChitietHoadonSps.AnyAsync(x => maBienThes.Contains(x.MaBienThe))
            || await _context.ChitietGiohangs.AnyAsync(x => maBienThes.Contains(x.MaBienThe))
            || await _context.ChitietPhieunhaps.AnyAsync(x => maBienThes.Contains(x.MaBienThe));

        if (daPhatSinhDuLieu)
        {
            return BadRequest(new
            {
                message = "Sản phẩm đã phát sinh dữ liệu, không thể xóa."
            });
        }

        // Dùng CreateExecutionStrategy() vì DbContext đã bật EnableRetryOnFailure ở Program.cs
        // (đồng bộ cách làm với action Create ở trên).
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Xóa file ảnh vật lý (Cloudinary) trước khi xóa bản ghi trong DB.
                foreach (var bt in sp.BienTheSanphams)
                {
                    await DeleteImageFile(bt.HinhAnhDaiDien);
                    foreach (var hbt in bt.HinhanhBTs)
                        await DeleteImageFile(hbt.DuongDan);
                }
                foreach (var h in sp.Hinhanhsps)
                    await DeleteImageFile(h.DuongDan);
                await DeleteImageFile(sp.HinhAnhDaiDien);

                // Xóa TƯỜNG MINH từ lá lên gốc — không để SQL tự SET NULL rồi bỏ rơi bản ghi con:
                // 1) Lịch sử tồn kho của mọi biến thể thuộc sản phẩm này
                var lichSuTonKho = await _context.LichSuTonKhos
                    .Where(l => maBienThes.Contains(l.MaBienThe))
                    .ToListAsync();
                _context.LichSuTonKhos.RemoveRange(lichSuTonKho);

                // 2) Ảnh gallery của từng biến thể
                var hinhAnhBT = sp.BienTheSanphams.SelectMany(bt => bt.HinhanhBTs).ToList();
                _context.HinhanhBTs.RemoveRange(hinhAnhBT);

                // 3) Bản thân các biến thể
                _context.BienTheSanphams.RemoveRange(sp.BienTheSanphams);

                // 4) Ảnh gallery của sản phẩm
                _context.Hinhanhsps.RemoveRange(sp.Hinhanhsps);

                // 5) Cuối cùng mới xóa sản phẩm
                _context.SanPhams.Remove(sp);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            // Xóa cache SAU khi transaction commit thành công — tránh xóa cache rồi lại rollback.
            await _cache.InvalidateAllProductCacheAsync();
            return Ok(new { message = "Đã xóa sản phẩm." });
        });
    }

    [HttpPatch("{id}/trangthai")]
    public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
    {
        var sp = await _context.SanPhams.FindAsync(id);
        if (sp == null) return NotFound();
        sp.TrangThai = dto.TrangThai;
        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Đã cập nhật trạng thái.", trangThai = sp.TrangThai });
    }

    [HttpGet("{maSP}/gallery")]
    public async Task<IActionResult> GetGallerySP(string maSP)
    {
        var list = await _context.Hinhanhsps
            .Where(h => h.MaSanPham == maSP)
            .Select(h => new { h.MaHa, h.DuongDan })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("{maSP}/gallery")]
    public async Task<IActionResult> AddGallerySP(string maSP, [FromForm] GalleryUploadDto dto)
    {
        var sp = await _context.SanPhams.FindAsync(maSP);
        if (sp == null) return NotFound(new { message = "Không tìm thấy sản phẩm." });

        if (dto.Files == null || dto.Files.Count == 0)
            return BadRequest(new { message = "Không có file nào được tải lên." });

        var added = new List<object>();
        foreach (var f in dto.Files.Take(10))
        {
            var path = await SaveImageAsync(f, "sanpham_gallery");
            if (path == null) continue;
            var h = new Hinhanhsp { MaSanPham = maSP, DuongDan = path };
            _context.Hinhanhsps.Add(h);
            await _context.SaveChangesAsync();
            added.Add(new { h.MaHa, h.DuongDan });
        }

        if (added.Count > 0)
            await _cache.InvalidateAllProductCacheAsync();

        return Ok(new { message = $"Đã thêm {added.Count} ảnh.", images = added });
    }

    [HttpDelete("gallery/{maHa:int}")]
    public async Task<IActionResult> DeleteGallerySP(int maHa)
    {
        var h = await _context.Hinhanhsps.FindAsync(maHa);
        if (h == null) return NotFound(new { message = "Không tìm thấy ảnh." });
        await DeleteImageFile(h.DuongDan);
        _context.Hinhanhsps.Remove(h);
        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Đã xóa ảnh." });
    }

    [HttpGet("{maSP}/bienthe")]
    public async Task<IActionResult> GetBienThe(string maSP)
    {
        var listTho = await _context.BienTheSanphams
            .AsNoTracking()
            .Where(bt => bt.MaSanPham == maSP)
            .Select(bt => new
            {
                bt.MaBienThe,
                MaSanPham = bt.MaSanPham,
                bt.TenBienThe,
                bt.GiaBan,
                SoLuong = bt.SoLuongTon,
                bt.PhanTramHoaHong,
                bt.HinhAnhDaiDien,
                bt.TrangThai,
            })
            .ToListAsync();

        // Biến thể mới tạo (mã lớn hơn) hiển thị trước
        var list = listTho.OrderByDescending(bt => LayPhanSoTuMa(bt.MaBienThe)).ToList();

        return Ok(list);
    }
    [HttpPost("bienthe")]
    public async Task<IActionResult> CreateBienThe([FromForm] CreateBienTheDto dto)
    {
        var maBtMoi = await SinhMaTuDongAsync("BT", 2);

        // Ảnh biến thể: ưu tiên ảnh người dùng upload riêng. Nếu không có — dù biến thể này
        // để "Mặc định" hay người dùng có nhập Màu/Dung tích riêng — đều fallback lấy ảnh đại
        // diện của sản phẩm cha, tránh để trống ảnh biến thể.
        var imgPath = await SaveImageAsync(dto.File, "bienthe");
        if (imgPath == null)
        {
            imgPath = await _context.SanPhams
                .Where(s => s.MaSp == dto.MaSanPham)
                .Select(s => s.HinhAnhDaiDien)
                .FirstOrDefaultAsync();
        }

        var bt = new BienTheSanpham
        {
            MaBienThe = maBtMoi,
            MaSanPham = dto.MaSanPham,
            TenBienThe = dto.TenBienThe?.Trim(),
            GiaBan = dto.GiaBan ?? 0,
            SoLuongTon = dto.SoLuong ?? 0,
            PhanTramHoaHong = dto.PhanTramHoaHong,
            HinhAnhDaiDien = imgPath,
            TrangThai = dto.TrangThai ?? 1,
        };
        ChuanHoaTrangThaiBienThe(bt);
        _context.BienTheSanphams.Add(bt);
        await _context.SaveChangesAsync();

        if (dto.GalleryFiles != null && dto.GalleryFiles.Count > 0)
        {
            foreach (var gf in dto.GalleryFiles.Take(10))
            {
                var gPath = await SaveImageAsync(gf, "bienthe_gallery");
                if (gPath != null)
                    _context.HinhanhBTs.Add(new HinhanhBT { MaBienThe = bt.MaBienThe, DuongDan = gPath });
            }
            await _context.SaveChangesAsync();
        }
        if (bt.SoLuongTon.GetValueOrDefault() > 0)
        {
            _context.LichSuTonKhos.Add(new LichSuTonKho
            {
                MaBienThe = bt.MaBienThe,
                LoaiBienDong = "DIEU_CHINH",
                SoLuongThayDoi = bt.SoLuongTon!.Value,
                TonTruoc = 0,
                TonSau = bt.SoLuongTon.Value,
                MaChungTu = null,
                MaNv = dto.MaNv,
                GhiChu = string.IsNullOrWhiteSpace(dto.GhiChu) ? "Tạo biến thể mới" : dto.GhiChu.Trim(),
                ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
            });
            await _context.SaveChangesAsync();
        }
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Thêm biến thể thành công.", maBienThe = bt.MaBienThe });
    }
    [HttpPut("bienthe/{maBienThe}")]
    public async Task<IActionResult> UpdateBienThe(string maBienThe, [FromForm] UpdateBienTheDto dto)
    {
        var bt = await _context.BienTheSanphams.FindAsync(maBienThe);
        if (bt == null) return NotFound(new { message = "Không tìm thấy biến thể." });

        bt.TenBienThe = dto.TenBienThe?.Trim() ?? bt.TenBienThe;
        bt.GiaBan = dto.GiaBan ?? bt.GiaBan;
        bt.PhanTramHoaHong = dto.PhanTramHoaHong ?? bt.PhanTramHoaHong;
        bt.TrangThai = dto.TrangThai ?? bt.TrangThai;

        if (dto.SoLuong.HasValue && dto.SoLuong.Value != (bt.SoLuongTon ?? 0))
        {
            if (string.IsNullOrWhiteSpace(dto.GhiChu))
                return BadRequest(new { message = "Vui lòng nhập lý do điều chỉnh số lượng." });

            var tonTruoc = bt.SoLuongTon ?? 0;
            var tonSau = dto.SoLuong.Value;
            bt.SoLuongTon = tonSau;

            _context.LichSuTonKhos.Add(new LichSuTonKho
            {
                MaBienThe = bt.MaBienThe,
                LoaiBienDong = "DIEU_CHINH",
                SoLuongThayDoi = tonSau - tonTruoc,
                TonTruoc = tonTruoc,
                TonSau = tonSau,
                MaChungTu = null,
                MaNv = dto.MaNv,
                GhiChu = dto.GhiChu.Trim(),
                ThoiGian = MinhTamHair.Services.TimeHelper.VnTimeHelper.Now()
            });
        }

        ChuanHoaTrangThaiBienThe(bt);

        if (dto.File != null && dto.File.Length > 0)
        {
            await DeleteImageFile(bt.HinhAnhDaiDien);
            bt.HinhAnhDaiDien = await SaveImageAsync(dto.File, "bienthe");
        }

        if (dto.GalleryFiles != null && dto.GalleryFiles.Count > 0)
        {
            foreach (var gf in dto.GalleryFiles.Take(10))
            {
                var gPath = await SaveImageAsync(gf, "bienthe_gallery");
                if (gPath != null)
                    _context.HinhanhBTs.Add(new HinhanhBT { MaBienThe = maBienThe, DuongDan = gPath });
            }
        }

        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Cập nhật biến thể thành công." });
    }
    [HttpDelete("bienthe/{maBienThe}")]
    public async Task<IActionResult> DeleteBienThe(string maBienThe)
    {
        var bt = await _context.BienTheSanphams
            .Include(b => b.HinhanhBTs)
            .FirstOrDefaultAsync(b => b.MaBienThe == maBienThe);

        if (bt == null)
            return NotFound(new { message = "Không tìm thấy biến thể." });

        // Kiểm tra dữ liệu liên quan
        bool daCoHoaDon = await _context.ChitietHoadonSps
            .AnyAsync(x => x.MaBienThe == maBienThe);

        bool daCoGioHang = await _context.ChitietGiohangs
            .AnyAsync(x => x.MaBienThe == maBienThe);

        bool daCoPhieuNhap = await _context.ChitietPhieunhaps
            .AnyAsync(x => x.MaBienThe == maBienThe);

        if (daCoHoaDon || daCoGioHang || daCoPhieuNhap)
        {
            return BadRequest(new
            {
                message = "Biến thể đã phát sinh dữ liệu, không thể xóa."
            });
        }

        foreach (var h in bt.HinhanhBTs)
        {
            await DeleteImageFile(h.DuongDan);
        }

        _context.HinhanhBTs.RemoveRange(bt.HinhanhBTs);

        await DeleteImageFile(bt.HinhAnhDaiDien);

        // Xóa TƯỜNG MINH lịch sử tồn kho của biến thể trước — FK "LichSuTonKho.MaBienThe ->
        // BienTheSanpham.MaBienThe" trong DB là SET NULL (không cascade), nên nếu bỏ qua bước
        // này, các dòng LichSuTonKho sẽ bị SQL tự set NULL và trở thành rác mồ côi thay vì
        // được dọn theo biến thể (cùng lỗi như action Delete sản phẩm ở trên).
        var lichSuTonKho = await _context.LichSuTonKhos
            .Where(l => l.MaBienThe == maBienThe)
            .ToListAsync();
        _context.LichSuTonKhos.RemoveRange(lichSuTonKho);

        _context.BienTheSanphams.Remove(bt);

        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();

        return Ok(new { message = "Đã xóa biến thể." });
    }
    [HttpGet("bienthe/{maBienThe}/gallery")]
    public async Task<IActionResult> GetGalleryBT(string maBienThe)
    {
        var list = await _context.HinhanhBTs
            .Where(h => h.MaBienThe == maBienThe)
            .Select(h => new { h.MaHa, h.DuongDan })
            .ToListAsync();
        return Ok(list);
    }
    [HttpPost("bienthe/{maBienThe}/gallery")]
    public async Task<IActionResult> AddGalleryBT(string maBienThe, [FromForm] GalleryUploadDto dto)
    {
        var bt = await _context.BienTheSanphams.FindAsync(maBienThe);
        if (bt == null) return NotFound(new { message = "Không tìm thấy biến thể." });

        if (dto.Files == null || dto.Files.Count == 0)
            return BadRequest(new { message = "Không có file nào được tải lên." });

        var added = new List<object>();
        foreach (var f in dto.Files.Take(10))
        {
            var path = await SaveImageAsync(f, "bienthe_gallery");
            if (path == null) continue;
            var h = new HinhanhBT { MaBienThe = maBienThe, DuongDan = path };
            _context.HinhanhBTs.Add(h);
            await _context.SaveChangesAsync();
            added.Add(new { h.MaHa, h.DuongDan });
        }

        if (added.Count > 0)
            await _cache.InvalidateAllProductCacheAsync();

        return Ok(new { message = $"Đã thêm {added.Count} ảnh.", images = added });
    }
    [HttpDelete("bienthe/gallery/{maHa:int}")]
    public async Task<IActionResult> DeleteGalleryBT(int maHa)
    {
        var h = await _context.HinhanhBTs.FindAsync(maHa);
        if (h == null) return NotFound(new { message = "Không tìm thấy ảnh." });
        await DeleteImageFile(h.DuongDan);
        _context.HinhanhBTs.Remove(h);
        await _context.SaveChangesAsync();
        await _cache.InvalidateAllProductCacheAsync();
        return Ok(new { message = "Đã xóa ảnh." });
    }
    [HttpGet("init")]
    public async Task<IActionResult> GetInit()
    {
        var danhMuc = await _context.DanhMucSPs
            .AsNoTracking()
            .Where(d => d.TrangThai == 1)
            .Select(d => new { d.MaDMSP, d.TenDanhMuc })
            .OrderBy(d => d.TenDanhMuc)
            .ToListAsync();

        var thuongHieu = await _context.Thuonghieus
            .AsNoTracking()
            .Where(t => t.TrangThai == 1)
            .Select(t => new { t.MaTh, t.TenThuongHieu })
            .OrderBy(t => t.TenThuongHieu)
            .ToListAsync();

        return Ok(new { danhMuc, thuongHieu });
    }
    [HttpGet("filter")]
    public async Task<IActionResult> Filter(
        [FromQuery] string? search,
        [FromQuery] string? danhMuc,
        [FromQuery] string? thuongHieu,
        [FromQuery] string? priceRange,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 9)
    {
        var query = _context.SanPhams
            .AsNoTracking()
            .Where(sp => sp.TrangThai == 1)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(sp =>
                sp.TenSp!.Contains(search) ||
                (sp.MoTa != null && sp.MoTa.Contains(search)));

        if (!string.IsNullOrWhiteSpace(danhMuc) && danhMuc != "all")
            query = query.Where(sp => sp.MaDanhMuc == danhMuc);

        if (!string.IsNullOrWhiteSpace(thuongHieu) && thuongHieu != "all")
            query = query.Where(sp => sp.MaTh == thuongHieu);

        if (!string.IsNullOrWhiteSpace(priceRange) && priceRange != "all")
        {
            query = priceRange switch
            {
                "low" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) < 200000),
                "mid" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) >= 200000 &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) <= 500000),
                "high" => query.Where(sp => sp.BienTheSanphams.Any(bt => bt.TrangThai == 1) &&
                              sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => bt.GiaBan) > 500000),
                _ => query
            };
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var paged = await query
            .OrderByDescending(sp => sp.MaSp.Length)
            .ThenByDescending(sp => sp.MaSp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(sp => new
            {
                sp.MaSp,
                sp.TenSp,
                sp.MoTa,
                sp.HinhAnhDaiDien,
                GiaTu = sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => (decimal?)bt.GiaBan),
                GiaDen = sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Max(bt => (decimal?)bt.GiaBan),
                SoLuongTon = sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).Sum(bt => bt.SoLuongTon ?? 0),
                SoBienThe = sp.BienTheSanphams.Count(bt => bt.TrangThai == 1),
                SoSao = sp.DanhgiaSanphams.Where(dg => dg.TrangThai == 1).Any()
                    ? Math.Round(sp.DanhgiaSanphams.Where(dg => dg.TrangThai == 1).Average(dg => (double)(dg.SoSao ?? 0)), 1)
                    : (double?)null,
                SoLuongDanhGia = sp.DanhgiaSanphams.Count(dg => dg.TrangThai == 1),
                DanhMuc = sp.MaDanhMucTheNavigation == null ? null : new
                {
                    sp.MaDanhMucTheNavigation.MaDMSP,
                    sp.MaDanhMucTheNavigation.TenDanhMuc,
                },
                ThuongHieu = sp.Thuonghieu == null ? null : new
                {
                    sp.Thuonghieu.MaTh,
                    sp.Thuonghieu.TenThuongHieu,
                },
            })
            .ToListAsync();

        return Ok(new { data = paged, totalPages, total });
    }
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var sp = await _context.SanPhams
            .AsNoTracking()
            .Include(s => s.MaDanhMucTheNavigation)
            .Include(s => s.Thuonghieu)
            .Include(s => s.Hinhanhsps)
            .Include(s => s.BienTheSanphams)
                .ThenInclude(bt => bt.HinhanhBTs)
            .Include(s => s.DanhgiaSanphams)
            .FirstOrDefaultAsync(s => s.MaSp == id && s.TrangThai == 1);

        if (sp == null) return NotFound(new { message = "Không tìm thấy sản phẩm." });

        var btHD = sp.BienTheSanphams.Where(bt => bt.TrangThai == 1).OrderBy(bt => bt.GiaBan).ToList();
        var danhGia = sp.DanhgiaSanphams.Where(dg => dg.TrangThai == 1).ToList();

        var goiY = await _context.SanPhams
            .AsNoTracking()
            .Where(s => s.MaDanhMuc == sp.MaDanhMuc && s.MaSp != id && s.TrangThai == 1)
            .Take(4)
            .Select(s => new
            {
                s.MaSp,
                s.TenSp,
                s.MoTa,
                s.HinhAnhDaiDien,
                GiaTu = s.BienTheSanphams.Where(bt => bt.TrangThai == 1).Min(bt => (decimal?)bt.GiaBan),
                GiaDen = s.BienTheSanphams.Where(bt => bt.TrangThai == 1).Max(bt => (decimal?)bt.GiaBan),
                ThuongHieu = s.Thuonghieu == null ? null : new
                {
                    s.Thuonghieu.MaTh,
                    s.Thuonghieu.TenThuongHieu,
                },
            })
            .ToListAsync();

        return Ok(new
        {
            sp.MaSp,
            sp.TenSp,
            sp.MoTa,
            sp.ThanhPhan,
            sp.HuongDanSuDung,
            sp.HinhAnhDaiDien,

            DanhMuc = sp.MaDanhMucTheNavigation == null ? null : new
            {
                sp.MaDanhMucTheNavigation.MaDMSP,
                sp.MaDanhMucTheNavigation.TenDanhMuc,
            },
            ThuongHieu = sp.Thuonghieu == null ? null : new
            {
                sp.Thuonghieu.MaTh,
                sp.Thuonghieu.TenThuongHieu,
                sp.Thuonghieu.HinhAnh,
            },
            HinhAnhs = sp.Hinhanhsps.Select(h => new { h.MaHa, h.DuongDan }).ToList(),

            BienThes = btHD.Select(bt => new
            {
                bt.MaBienThe,
                bt.TenBienThe,
                bt.GiaBan,
                bt.SoLuongTon,
                bt.HinhAnhDaiDien,
                bt.PhanTramHoaHong,
                HinhAnhs = bt.HinhanhBTs.Select(h => new { h.MaHa, h.DuongDan }).ToList(),
            }).ToList(),

            GiaTu = btHD.Any() ? btHD.Min(bt => bt.GiaBan) : (decimal?)null,
            GiaDen = btHD.Any() ? btHD.Max(bt => bt.GiaBan) : (decimal?)null,

            SoSao = danhGia.Any() ? Math.Round(danhGia.Average(dg => dg.SoSao ?? 0), 1) : (double?)null,
            SoLuongDanhGia = danhGia.Count,
            SuggestedProducts = goiY,
        });
    }
    private static void ChuanHoaTrangThaiBienThe(BienTheSanpham bt)
    {
        bt.TrangThai = (bt.SoLuongTon ?? 0) > 0 ? 1 : 0;
    }
    private async Task DongBoTrangThaiTheoSoLuongAsync(string? maSanPham = null)
    {
        var query = _context.BienTheSanphams.Where(bt =>
            ((bt.SoLuongTon ?? 0) == 0 && bt.TrangThai == 1) ||
            ((bt.SoLuongTon ?? 0) > 0 && bt.TrangThai == 0));

        if (!string.IsNullOrWhiteSpace(maSanPham))
            query = query.Where(bt => bt.MaSanPham == maSanPham);

        var lechs = await query.ToListAsync();
        if (lechs.Count == 0) return;

        foreach (var bt in lechs)
            bt.TrangThai = (bt.SoLuongTon ?? 0) > 0 ? 1 : 0;

        await _context.SaveChangesAsync();
    }
}

// ════════════════════════════════════════════════════════════════════
// DTOs
// ════════════════════════════════════════════════════════════════════

public class CreateSanPhamDto
{
    public string? TenSp { get; set; }
    public string? MaDanhMuc { get; set; }
    public string? MaTh { get; set; }
    public string? MoTa { get; set; }
    public string? ThanhPhan { get; set; }
    public string? HuongDanSuDung { get; set; }
    public int? TrangThai { get; set; }
    public IFormFile? File { get; set; }
    public List<IFormFile>? GalleryFiles { get; set; }

    // ── Giá bán & tồn kho ban đầu (bắt buộc giá): tạo sản phẩm luôn phải kèm 1 biến thể để bán được.
    //    TenBienThe không bắt buộc, fallback "Mặc định" nếu người dùng không nhập ──
    public string? TenBienThe { get; set; }
    public decimal? GiaBan { get; set; }
    public int? SoLuong { get; set; }
    public decimal? PhanTramHoaHong { get; set; }
    public IFormFile? BienTheFile { get; set; }
    public List<IFormFile>? BienTheGalleryFiles { get; set; }
    public string? MaNv { get; set; }
}

public class UpdateSanPhamDto
{
    public string? TenSp { get; set; }
    public string? MaDanhMuc { get; set; }
    public string? MaTh { get; set; }
    public string? MoTa { get; set; }
    public string? ThanhPhan { get; set; }
    public string? HuongDanSuDung { get; set; }
    public int? TrangThai { get; set; }
    public IFormFile? File { get; set; }
    public List<IFormFile>? GalleryFiles { get; set; }
}

public class ToggleStatusDto
{
    public int TrangThai { get; set; }
}

public class CreateBienTheDto
{
    public string? MaSanPham { get; set; }
    public string? TenBienThe { get; set; }
    public decimal? GiaBan { get; set; }
    public int? SoLuong { get; set; }
    public decimal? PhanTramHoaHong { get; set; }
    public int? TrangThai { get; set; }
    public IFormFile? File { get; set; }
    public List<IFormFile>? GalleryFiles { get; set; }
    public string? MaNv { get; set; }
    public string? GhiChu { get; set; }
}

public class UpdateBienTheDto
{
    public string? MaSanPham { get; set; }
    public string? TenBienThe { get; set; }
    public decimal? GiaBan { get; set; }
    public int? SoLuong { get; set; }
    public decimal? PhanTramHoaHong { get; set; }
    public int? TrangThai { get; set; }
    public IFormFile? File { get; set; }
    public List<IFormFile>? GalleryFiles { get; set; }
    public string? MaNv { get; set; }
    public string? GhiChu { get; set; }
}

public class GalleryUploadDto
{
    public List<IFormFile>? Files { get; set; }
}