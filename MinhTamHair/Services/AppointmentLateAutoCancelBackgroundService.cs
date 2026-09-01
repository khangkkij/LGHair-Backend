using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinhTamHair.Models;
using MinhTamHair.Services.TimeHelper;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MinhTamHair.Services
{
    /// <summary>
    /// Background Service tự động quét và Tạm Hủy các đơn lịch hẹn trễ quá 30 phút.
    /// Chu kỳ: 5 phút/lần.
    /// Tối ưu siêu nhẹ: Chỉ quét trong khung giờ hoạt động (08:00 - 21:30) và chỉ khi có đơn chờ check-in trong ngày.
    /// </summary>
    public class AppointmentLateAutoCancelBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AppointmentLateAutoCancelBackgroundService> _logger;
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);

        public AppointmentLateAutoCancelBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AppointmentLateAutoCancelBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_period);

            // Chạy kiểm tra ngay khi khởi động
            await DoWorkAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoWorkAsync(stoppingToken);
            }
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                var now = VnTimeHelper.Now();

                // 1. Tối ưu nhẹ: Chỉ quét trong khung giờ Salon làm việc (08:00 đến 21:30)
                if (now.Hour < 8 || (now.Hour >= 21 && now.Minute > 30))
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();
                var emailService = scope.ServiceProvider.GetService<IEmailService>();

                var today = DateOnly.FromDateTime(now);
                var currentTimeOnly = TimeOnly.FromDateTime(now);

                // 2. Chỉ truy vấn các đơn của ngày HÔM NAY đang ở trạng thái Chờ phục vụ (1: Đã cọc, 2: Đã trả 100%)
                var pendingAppointments = await context.Lichdats
                    .Include(ld => ld.MaKhNavigation)
                    .Where(ld => ld.NgayHen == today && (ld.TrangThai == 1 || ld.TrangThai == 2) && ld.GioBatDau.HasValue)
                    .ToListAsync(cancellationToken);

                // Nếu không có đơn nào chờ check-in, dừng quét ngay để tiết kiệm tài nguyên
                if (!pendingAppointments.Any())
                {
                    return;
                }

                int cancelledCount = 0;

                foreach (var ld in pendingAppointments)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Tính thời gian: Giờ hẹn + 30 phút
                    var maxAllowedTime = ld.GioBatDau.Value.AddMinutes(30);

                    // Nếu giờ hiện tại đã vượt quá (Giờ hẹn + 30 phút) => Khách trễ quá 30 phút
                    if (currentTimeOnly > maxAllowedTime)
                    {
                        var lateMins = (int)(currentTimeOnly - ld.GioBatDau.Value).TotalMinutes;

                        // Chuyển trạng thái lịch đặt sang 4: Tạm hủy do trễ hẹn
                        ld.TrangThai = 4;

                        // Cập nhật Hóa đơn tương ứng
                        var hd = await context.HoaDons.FirstOrDefaultAsync(h => h.MaLd == ld.MaLd, cancellationToken);
                        if (hd != null && hd.TrangThai != 4)
                        {
                            hd.TrangThai = 4;
                        }

                        cancelledCount++;
                        _logger.LogWarning("[AutoCancelLate] Tự động tạm hủy đơn #{MaLd} - Khách: {TenKh} (Trễ {LateMins}p)", 
                            ld.MaLd, ld.MaKhNavigation?.HoTen, lateMins);

                        // Gửi Email thông báo tự động cho khách hàng
                        var email = ld.MaKhNavigation?.Email;
                        var tenKh = ld.MaKhNavigation?.HoTen ?? "Quý khách";
                        var maDon = hd?.MaHd ?? ld.MaLd;

                        if (emailService != null && !string.IsNullOrEmpty(email))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var subject = $"[LGHair Salon] Thông báo tạm hủy lịch hẹn #{maDon} & Mở khóa quyền Dời lịch / Hoàn tiền";
                                    var htmlContent = $@"
                                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;'>
                                        <div style='text-align: center; padding-bottom: 15px; border-bottom: 2px solid #f1f5f9;'>
                                            <h2 style='color: #0f172a; margin: 0; text-transform: uppercase; letter-spacing: 1px;'>LGHair SALON</h2>
                                            <p style='color: #64748b; font-size: 13px; margin: 5px 0 0;'>Thông báo tình trạng lịch hẹn</p>
                                        </div>
                                        
                                        <div style='padding: 20px 0;'>
                                            <p style='font-size: 15px; color: #1e293b;'>Kính gửi <b>{tenKh}</b>,</p>
                                            <p style='font-size: 14px; color: #334155; line-height: 1.6;'>
                                                Lịch hẹn của Quý khách (Mã đơn: <b style='color: #be123c;'>#{maDon}</b>) đã được hệ thống <b>Tạm hủy</b> do đã quá thời gian hẹn phục vụ hơn 30 phút mà Salon chưa tiếp nhận thủ tục check-in.
                                            </p>
                                            
                                            <div style='background-color: #f8fafc; border-left: 4px solid #f59e0b; padding: 14px 16px; margin: 15px 0; border-radius: 6px;'>
                                                <p style='margin: 0 0 8px; font-size: 14px; font-weight: bold; color: #92400e;'>📋 Hệ thống đã Mở Khóa Quyền Hạn cho đơn hàng #{maDon}:</p>
                                                <ul style='margin: 0; padding-left: 18px; font-size: 13px; color: #78350f; line-height: 1.6;'>
                                                    <li><b>Lựa chọn 1 (Khuyên dùng):</b> Quý khách vào trang web <b>LGHair Salon &gt; Tài khoản &gt; Lịch sử đặt lịch</b> và bấm nút <b>[🔄 Dời Lịch Hẹn]</b> để chọn ngày/giờ mới hoàn toàn <b>MIỄN PHÍ (Bảo lưu 100% tiền)</b>.</li>
                                                    <li><b>Lựa chọn 2:</b> Bấm nút <b>[💳 Yêu Cầu Hoàn Tiền]</b> để điền số tài khoản ngân hàng nhận tiền hoàn.</li>
                                                    <li><b>Thời hạn xử lý:</b> Trong vòng <b>3 ngày</b> kể từ hôm nay. Sau 3 ngày, phí thủ tục bảo lưu đơn là <b>5.000đ/ngày</b> quá hạn.</li>
                                                </ul>
                                            </div>
                                            
                                            <div style='background: #f1f5f9; padding: 12px; border-radius: 8px; text-align: center; margin-top: 20px;'>
                                                <p style='margin: 0; font-size: 13.5px; color: #334155; font-weight: bold;'>
                                                    👉 Quý khách chỉ cần mở website LGHair Salon, đăng nhập tài khoản và vào mục <span style='color: #0f172a;'>""Lịch sử hóa đơn""</span> để thực hiện ngay!
                                                </p>
                                            </div>
                                            
                                            <p style='font-size: 13px; color: #64748b; text-align: center; margin-top: 20px;'>
                                                Nếu cần hỗ trợ nhanh, Quý khách vui lòng liên hệ Hotline: <b>0901.234.567</b>
                                            </p>
                                        </div>
                                        
                                        <div style='border-top: 1px solid #f1f5f9; padding-top: 15px; text-align: center; font-size: 12px; color: #94a3b8;'>
                                            © 2026 LGHair Salon. Trân trọng cảm ơn Quý khách!
                                        </div>
                                    </div>
                                    ";

                                    await emailService.SendEmailAsync(email, subject, htmlContent);
                                }
                                catch (Exception exMail)
                                {
                                    _logger.LogError(exMail, "[AutoCancelLate] Lỗi gửi email tạm hủy #{MaDon}", maDon);
                                }
                            });
                        }
                    }
                }

                if (cancelledCount > 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("[AutoCancelLate] Đã tự động tạm hủy {Count} đơn trễ hẹn > 30p và giải phóng ca Stylist.", cancelledCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AutoCancelLate] Lỗi khi chạy background worker quét đơn trễ");
            }
        }
    }
}