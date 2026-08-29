using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinhTamHair.Models;
using MinhTamHair.Services.TimeHelper;

namespace MinhTamHair.Services.Cleanup
{

    public class BookingCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingCleanupService> _logger;

        public BookingCleanupService(IServiceProvider serviceProvider, ILogger<BookingCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //_logger.LogInformation("[SYSTEM] Dịch vụ dọn rác Lịch Đặt đã khởi động.");

            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    try
            //    {
            //        using (var scope = _serviceProvider.CreateScope())
            //        {
            //            var context = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();

            //            // Lấy mốc thời gian cách đây 15 phút
            //            var expiredTime = VnTimeHelper.Now().AddMinutes(-15);

            //            var expiredMaLds = await context.HoaDons
            //                .AsNoTracking()
            //                .Where(hd => hd.TrangThai == 0 && hd.NgayTao <= expiredTime)
            //                .Join(context.Lichdats.AsNoTracking(),
            //                      hd => hd.MaLd,
            //                      ld => ld.MaLd,
            //                      (hd, ld) => new { HoaDon = hd, LichDat = ld })
            //                .Where(x => x.LichDat.TrangThai == 5)
            //                .Select(x => x.LichDat.MaLd)
            //                .ToListAsync(stoppingToken);

            //            if (expiredMaLds.Any())
            //            {
            //                var strategy = context.Database.CreateExecutionStrategy();
            //                await strategy.ExecuteAsync(async () =>
            //                {
            //                    using (var transaction = await context.Database.BeginTransactionAsync(stoppingToken))
            //                    {
            //                        try
            //                        {
            //                            var lichDats = await context.Lichdats.Where(l => expiredMaLds.Contains(l.MaLd)).ToListAsync(stoppingToken);
            //                            var hoaDons = await context.HoaDons.Where(h => expiredMaLds.Contains(h.MaLd)).ToListAsync(stoppingToken);
            //                            var maHds = hoaDons.Select(h => h.MaHd).ToList();
            //                            var thanhToans = await context.Thanhtoans.Where(t => maHds.Contains(t.MaHd)).ToListAsync(stoppingToken);

            //                            // Chém về 0 nhả slot
            //                            foreach (var ld in lichDats) ld.TrangThai = 0;
            //                            foreach (var hd in hoaDons) hd.TrangThai = 0;
            //                            foreach (var tt in thanhToans) tt.TrangThai = 0;

            //                            await context.SaveChangesAsync(stoppingToken);
            //                            await transaction.CommitAsync(stoppingToken);

            //                            _logger.LogWarning($"[CLEANUP SUCCESS] Đã giải phóng thợ cho {expiredMaLds.Count} đơn: {string.Join(", ", expiredMaLds)}");
            //                        }
            //                        catch (Exception dbEx)
            //                        {
            //                            await transaction.RollbackAsync(stoppingToken);
            //                            _logger.LogError($"[CLEANUP ROLLBACK] {dbEx.Message}");
            //                        }
            //                    }
            //                });
            //            }
            //        }
            //    }
            //    catch (TaskCanceledException) { break; }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError($"[CLEANUP FATAL] {ex.Message}");
            //    }

            //    // Đi tuần tra mỗi 5 phút
            //    try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            //    catch (TaskCanceledException) { break; }
            //}
        }
    }
}