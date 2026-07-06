using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MinhTamHair.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinhTamHair.Services
{
    public class EInvoiceService : IEInvoiceService
    {
        private readonly MinhTamHairContext _context;
        private readonly EInvoiceOptions _options;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IMemoryCache _cache;
        private const string TokenCacheKey = "einvoice_access_token";
        private readonly IEmailService _emailService;

        public EInvoiceService(
            MinhTamHairContext context,
            IOptions<EInvoiceOptions> options,
            IHttpClientFactory httpFactory,
            IMemoryCache cache,
            IEmailService emailService)
        {
            _context = context;
            _options = options.Value;
            _httpFactory = httpFactory;
            _cache = cache;
            _emailService = emailService;
        }

        // ================== BƯỚC 1: LẤY / CACHE TOKEN ==================
        private async Task<string> GetAccessTokenAsync()
        {
            if (_cache.TryGetValue(TokenCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
                return cached;

            var client = _httpFactory.CreateClient();
            var authValue = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>();

            if (!response.IsSuccessStatusCode || body?.Data?.AccessToken == null)
                throw new Exception("Không lấy được access token từ SePay eInvoice: " + (body?.Message ?? response.StatusCode.ToString()));

            // Cache gần hết hạn (trừ hao 5 phút) để tránh dùng token vừa hết hạn
            var expiry = TimeSpan.FromSeconds(Math.Max(60, body.Data.ExpiresIn - 300));
            _cache.Set(TokenCacheKey, body.Data.AccessToken, expiry);

            return body.Data.AccessToken;
        }

        // ================== BƯỚC 2: TẠO + BƯỚC 3: KIỂM TRA KẾT QUẢ ==================
        public async Task<EInvoiceResult> PhatHanhHoaDonAsync(string maHddt)
        {
            var hoaDonDT = await _context.HoaDonDienTus.FirstOrDefaultAsync(h => h.MaHddt == maHddt);
            if (hoaDonDT == null)
                return new EInvoiceResult { Success = false, ErrorMessage = "Không tìm thấy yêu cầu xuất hóa đơn." };

            if (hoaDonDT.TrangThai == 1)
                return new EInvoiceResult { Success = true, SoHoaDon = hoaDonDT.SoHoaDon, LinkPdf = hoaDonDT.LinkPdf };

            var hoaDon = await _context.HoaDons.FindAsync(hoaDonDT.MaHd);
            var items = new List<EInvoiceItemDto>();
            int lineNo = 1;

            // ===== Gộp dòng DỊCH VỤ (nếu đơn hàng có gắn lịch đặt MaLd) =====
            if (hoaDon != null && !string.IsNullOrEmpty(hoaDon.MaLd))
            {
                var chiTietDv = await _context.ChitietLichdats.Where(c => c.MaLd == hoaDon.MaLd).ToListAsync();
                foreach (var ct in chiTietDv)
                {
                    string tenDv = "Dich vu";
                    if (!string.IsNullOrEmpty(ct.MaDv))
                    {
                        var dv = await _context.Dichvus.FindAsync(ct.MaDv);
                        tenDv = dv?.TenDv ?? tenDv;
                    }
                    else if (!string.IsNullOrEmpty(ct.MaCb))
                    {
                        var cb = await _context.Combos.FindAsync(ct.MaCb);
                        tenDv = cb?.TenCombo ?? tenDv;
                    }

                    items.Add(new EInvoiceItemDto
                    {
                        LineNumber = lineNo++,
                        LineType = 1,
                        ItemCode = ct.MaDv ?? ct.MaCb,
                        ItemName = tenDv,
                        Unit = "Dich vu",
                        Quantity = 1,
                        UnitPrice = ct.GiaLucDat ?? 0m,
                        TaxRate = hoaDonDT.LoaiKhach == 1 ? 10 : null
                    });
                }
            }

            // ===== Gộp dòng SẢN PHẨM =====
            var chiTiet = await _context.ChitietHoadonSps.Where(c => c.MaHd == hoaDonDT.MaHd).ToListAsync();
            foreach (var ct in chiTiet)
            {
                var bt = await _context.BienTheSanphams.FindAsync(ct.MaBienThe);
                var sp = bt != null ? await _context.SanPhams.FindAsync(bt.MaSanPham) : null;

                items.Add(new EInvoiceItemDto
                {
                    LineNumber = lineNo++,
                    LineType = 1,
                    ItemCode = bt?.MaBienThe,
                    ItemName = $"{sp?.TenSp} - {bt?.TenBienThe}".Trim(' ', '-'),
                    Unit = "Cai",
                    Quantity = (decimal)ct.SoLuong,
                    UnitPrice = ct.DonGiaLucBan ?? 0m,
                    // Chỉ set TaxRate khi xuất hóa đơn GTGT (khách doanh nghiệp)
                    TaxRate = hoaDonDT.LoaiKhach == 1 ? 10 : null
                });
            }

            if (items.Count == 0)
            {
                hoaDonDT.TrangThai = (byte)2;
                hoaDonDT.GhiChuLoi = "Không tìm thấy dịch vụ/sản phẩm nào để xuất hóa đơn";
                await _context.SaveChangesAsync();
                return new EInvoiceResult { Success = false, ErrorMessage = hoaDonDT.GhiChuLoi };
            }

            // ===== PHÂN BỔ GIẢM GIÁ VOUCHER (NẾU CÓ) VÀO TỪNG DÒNG HÀNG =====
            // HoaDon.ThanhTien là số tiền khách THỰC TRẢ (đã trừ voucher, cộng thêm phí ship nếu có).
            // Nếu không phân bổ, hóa đơn sẽ ghi sai cao hơn số tiền khách thực trả.
            decimal phiShip = 0m;
            var thongTinGH = await _context.ThongtinGiaohangs.FirstOrDefaultAsync(t => t.MaHd == hoaDonDT.MaHd);
            if (thongTinGH != null) phiShip = thongTinGH.PhiShip ?? 0m;

            decimal tongTruocGiam = items.Sum(i => i.Quantity * i.UnitPrice);
            decimal tongThucTraHangHoa = (hoaDon?.ThanhTien ?? tongTruocGiam) - phiShip;
            decimal tongGiamGia = tongTruocGiam - tongThucTraHangHoa;

            if (tongGiamGia > 0 && tongTruocGiam > 0)
            {
                decimal daPhanBo = 0m;
                for (int i = 0; i < items.Count; i++)
                {
                    decimal tyLe = (items[i].Quantity * items[i].UnitPrice) / tongTruocGiam;
                    decimal giamCuaDong = Math.Round(tongGiamGia * tyLe, 0);

                    // Dòng cuối nhận phần dư làm tròn để tổng khớp chính xác tuyệt đối
                    if (i == items.Count - 1)
                        giamCuaDong = tongGiamGia - daPhanBo;

                    items[i].DiscountAmount = giamCuaDong;
                    daPhanBo += giamCuaDong;
                }
            }

            // Thêm dòng phí vận chuyển riêng (nếu có), không áp dụng giảm giá
            if (phiShip > 0)
            {
                items.Add(new EInvoiceItemDto
                {
                    LineNumber = lineNo++,
                    LineType = 1,
                    ItemCode = "PHISHIP",
                    ItemName = "Phi van chuyen",
                    Unit = "Lan",
                    Quantity = 1,
                    UnitPrice = phiShip,
                    TaxRate = hoaDonDT.LoaiKhach == 1 ? 10 : null
                });
            }

            var buyer = new EInvoiceBuyerDto
            {
                Type = hoaDonDT.LoaiKhach == 1 ? "company" : "personal",
                Name = hoaDonDT.TenNguoiMua ?? "Khach hang",
                TaxCode = hoaDonDT.Mst,
                LegalName = hoaDonDT.LoaiKhach == 1 ? hoaDonDT.TenNguoiMua : null,
                Address = hoaDonDT.DiaChiXuatHd,
                Email = hoaDonDT.EmailNhanHd
            };

            var templateCode = hoaDonDT.LoaiKhach == 1 ? _options.TemplateCodeVat : _options.TemplateCodeSale;

            var payload = new
            {
                template_code = templateCode,
                invoice_series = _options.InvoiceSeries,
                issued_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                currency = "VND",
                provider_account_id = _options.ProviderAccountId,
                payment_method = "TM",
                is_draft = false,
                buyer = new
                {
                    type = buyer.Type,
                    name = buyer.Name,
                    legal_name = buyer.LegalName,
                    tax_code = buyer.TaxCode,
                    address = buyer.Address,
                    email = buyer.Email,
                    phone = buyer.Phone
                },
                items = items.Select(i => new
                {
                    line_number = i.LineNumber,
                    line_type = i.LineType,
                    item_code = i.ItemCode,
                    item_name = i.ItemName,
                    unit = i.Unit,
                    quantity = i.Quantity,
                    unit_price = i.UnitPrice,
                    tax_rate = i.TaxRate,
                    discount_amount = i.DiscountAmount > 0 ? (decimal?)i.DiscountAmount : null
                }),
                notes = $"Don hang {hoaDonDT.MaHd}"
            };

            try
            {
                var token = await GetAccessTokenAsync();
                var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var createResponse = await client.PostAsJsonAsync($"{_options.BaseUrl}/v1/invoices/create", payload);
                var createBody = await createResponse.Content.ReadFromJsonAsync<CreateInvoiceResponse>();

                if (!createResponse.IsSuccessStatusCode || createBody?.Data?.TrackingCode == null)
                {
                    hoaDonDT.TrangThai = 2;
                    hoaDonDT.GhiChuLoi = createBody?.Message ?? "Lỗi tạo yêu cầu xuất hóa đơn";
                    await _context.SaveChangesAsync();
                    return new EInvoiceResult { Success = false, ErrorMessage = hoaDonDT.GhiChuLoi };
                }

                var trackingCode = createBody.Data.TrackingCode;
                hoaDonDT.ReferenceCode = trackingCode;

                // ============ BƯỚC 4: POLLING KIỂM TRA KẾT QUẢ ============
                CheckInvoiceResponse? checkBody = null;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    await Task.Delay(2500);
                    var checkResponse = await client.GetAsync($"{_options.BaseUrl}/v1/invoices/create/check/{trackingCode}");
                    checkBody = await checkResponse.Content.ReadFromJsonAsync<CheckInvoiceResponse>();

                    if (checkBody?.Data?.Status == "Success" || checkBody?.Data?.Status == "Failed")
                        break;
                    // status == "Pending" -> thử lại
                }

                if (checkBody?.Data?.Status == "Success" && checkBody.Data.Invoice != null)
                {
                    hoaDonDT.TrangThai = 1;
                    hoaDonDT.SoHoaDon = checkBody.Data.Invoice.InvoiceNumber;
                    hoaDonDT.KyHieu = checkBody.Data.Invoice.InvoiceSeries;
                    hoaDonDT.LinkPdf = checkBody.Data.Invoice.PdfUrl;
                    hoaDonDT.LinkXml = checkBody.Data.Invoice.XmlUrl;
                    hoaDonDT.NgayXuat = DateTime.Now;
                    await _context.SaveChangesAsync();

                    // ===== GỬI EMAIL HÓA ĐƠN ĐIỆN TỬ CHO KHÁCH =====
                    // Không để lỗi gửi mail làm fail cả quy trình xuất hóa đơn (hóa đơn đã xuất thành công rồi)
                    if (!string.IsNullOrWhiteSpace(hoaDonDT.EmailNhanHd))
                    {
                        try
                        {
                            var html = $@"
                <div style='font-family:Arial,sans-serif;font-size:14px;color:#333;'>
                    <p>Kính gửi <b>{hoaDonDT.TenNguoiMua ?? "Quý khách"}</b>,</p>
                    <p>Cảm ơn Quý khách đã mua sắm tại <b>MinhTamHair</b>. Hóa đơn điện tử của Quý khách đã được phát hành thành công.</p>
                    <table style='border-collapse:collapse;margin:12px 0;'>
                        <tr><td style='padding:4px 8px;'>Số hóa đơn:</td><td style='padding:4px 8px;'><b>{hoaDonDT.SoHoaDon}</b></td></tr>
                        <tr><td style='padding:4px 8px;'>Ký hiệu:</td><td style='padding:4px 8px;'>{hoaDonDT.KyHieu}</td></tr>
                        <tr><td style='padding:4px 8px;'>Mã đơn hàng:</td><td style='padding:4px 8px;'>{hoaDonDT.MaHd}</td></tr>
                    </table>
                    <p>
                        <a href='{hoaDonDT.LinkPdf}' style='background:#1a1a2e;color:#fff;padding:10px 18px;text-decoration:none;border-radius:4px;' target='_blank'>Tải hóa đơn PDF</a>
                        &nbsp;
                    </p>
                    <p style='color:#888;font-size:12px;'>Đây là email tự động, vui lòng không phản hồi trực tiếp email này.</p>
                </div>";

                            await _emailService.SendEmailAsync(
                                hoaDonDT.EmailNhanHd,
                                $"Hóa đơn điện tử #{hoaDonDT.SoHoaDon} - MinhTamHair",
                                html);
                        }
                        catch (Exception mailEx)
                        {
                            // Ghi lại lỗi gửi mail riêng, không đè GhiChuLoi (vốn dùng cho lỗi xuất hóa đơn)
                            hoaDonDT.GhiChuLoi = (hoaDonDT.GhiChuLoi ?? "") + $" | Lỗi gửi email hóa đơn: {mailEx.Message}";
                            await _context.SaveChangesAsync();
                        }
                    }

                    return new EInvoiceResult
                    {
                        Success = true,
                        SoHoaDon = hoaDonDT.SoHoaDon,
                        LinkPdf = hoaDonDT.LinkPdf,
                        LinkXml = hoaDonDT.LinkXml,
                        ReferenceCode = trackingCode
                    };
                }

                hoaDonDT.TrangThai = checkBody?.Data?.Status == "Failed" ? (byte)2 : (byte)0; // 0 = vẫn "Pending", để job nền check lại sau
                hoaDonDT.GhiChuLoi = checkBody?.Data?.Message ?? "Hết thời gian chờ kết quả, sẽ kiểm tra lại sau";
                await _context.SaveChangesAsync();

                return new EInvoiceResult { Success = false, ErrorMessage = hoaDonDT.GhiChuLoi, ReferenceCode = trackingCode };
            }
            catch (Exception ex)
            {
                hoaDonDT.TrangThai = 2;
                hoaDonDT.GhiChuLoi = ex.Message;
                await _context.SaveChangesAsync();
                return new EInvoiceResult { Success = false, ErrorMessage = ex.Message };
            }
        }
    }

    // ================== DTO PHẢN HỒI TỪ SEPAY (JSON trả về là snake_case) ==================
    public class TokenResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public TokenData? Data { get; set; }
        public class TokenData
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = null!;
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }

    public class CreateInvoiceResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public CreateData? Data { get; set; }
        public class CreateData
        {
            [JsonPropertyName("tracking_code")]
            public string TrackingCode { get; set; } = null!;
        }
    }

    public class CheckInvoiceResponse
    {
        public bool Success { get; set; }
        public CheckData? Data { get; set; }
        public class CheckData
        {
            public string Status { get; set; } = null!; // Pending | Success | Failed
            public string? Message { get; set; }
            public InvoiceDetail? Invoice { get; set; }
        }
        public class InvoiceDetail
        {
            [JsonPropertyName("invoice_number")]
            public string? InvoiceNumber { get; set; }
            [JsonPropertyName("invoice_series")]
            public string? InvoiceSeries { get; set; }
            [JsonPropertyName("pdf_url")]
            public string? PdfUrl { get; set; }
            [JsonPropertyName("xml_url")]
            public string? XmlUrl { get; set; }
            [JsonPropertyName("total_amount")]
            public decimal TotalAmount { get; set; }
        }
    }
}