using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinhTamHair.Models;
using MinhTamHair.Services.TimeHelper;

namespace MinhTamHair.Services
{
    public class AttendanceBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AttendanceBackgroundService> _logger;
        // Chạy định kỳ mỗi 5 phút một lần
        private readonly TimeSpan _period = TimeSpan.FromMinutes(5);

        public AttendanceBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AttendanceBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_period);

            // Chạy 1 lần ngay khi server khởi động
            await DoWorkAsync(stoppingToken);

            // Sau đó cứ mỗi 5 phút lặp lại 1 lần
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoWorkAsync(stoppingToken);
            }
        }

        private async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MinhTamHairContext>();

                var now = VnTimeHelper.Now();
                var today = DateOnly.FromDateTime(now);
                var currentTime = now.TimeOfDay;

                // 1. TỰ ĐỘNG KHỞI TẠO CA LÀM CHO NGÀY HÔM NAY (NẾU CHƯA CÓ)
                await AutoInitTodayShiftsAsync(context, today, cancellationToken);

                // 2. TỰ ĐỘNG QUÉT VÀ ĐÁNH VẮNG CÁC CA QUÁ GIỜ MÀ CHƯA ĐIỂM DANH
                await AutoMarkAbsentShiftsAsync(context, today, currentTime, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AttendanceWorker] Lỗi trong tiến trình quét điểm danh nền");
            }
        }

        private async Task AutoInitTodayShiftsAsync(MinhTamHairContext context, DateOnly today, CancellationToken cancellationToken)
        {
            try
            {
                var yesterday = today.AddDays(-1);

                // Lấy danh sách nhân viên đang hoạt động
                var eligibleStaff = await context.Nhanviens
                    .Where(nv => nv.TrangThai == 1)
                    .ToListAsync(cancellationToken);

                if (!eligibleStaff.Any()) return;

                // Kiểm tra xem những ai chưa có lịch cho ngày hôm nay
                var existingMaNv = await context.Lichlamviecs
                    .Where(l => l.NgayLamViec == today)
                    .Select(l => l.MaNv)
                    .ToListAsync(cancellationToken);

                var staffToInit = eligibleStaff.Where(nv => !existingMaNv.Contains(nv.MaNv)).ToList();

                if (staffToInit.Any())
                {
                    // Lấy ca làm hôm qua làm mẫu
                    var yesterdayShifts = await context.Lichlamviecs
                        .Where(l => l.NgayLamViec == yesterday && l.MaNv != null)
                        .GroupBy(l => l.MaNv)
                        .Select(g => new
                        {
                            MaNv = g.Key,
                            CaLam = g.FirstOrDefault()!.CaLam
                        })
                        .ToDictionaryAsync(x => x.MaNv!, x => x.CaLam, cancellationToken);

                    foreach (var nv in staffToInit)
                    {
                        var inheritedCaLam = yesterdayShifts.GetValueOrDefault(nv.MaNv) ?? "Chưa xếp";
                        if (today.DayOfWeek == DayOfWeek.Sunday)
                        {
                            inheritedCaLam = "Chưa xếp";
                        }

                        context.Lichlamviecs.Add(new Lichlamviec
                        {
                            MaNv = nv.MaNv,
                            NgayLamViec = today,
                            CaLam = inheritedCaLam,
                            TrangThai = inheritedCaLam == "Chưa xếp" ? 0 : 1
                        });
                    }

                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("[AttendanceWorker] Đã tự động khởi tạo ca làm việc hôm nay ({Today}) cho {Count} nhân viên.", today, staffToInit.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AttendanceWorker] Lỗi khi tự động khởi tạo ca làm ngày hôm nay.");
            }
        }

        private async Task AutoMarkAbsentShiftsAsync(MinhTamHairContext context, DateOnly today, TimeSpan currentTime, CancellationToken cancellationToken)
        {
            try
            {
                // Lấy các ca đang ở trạng thái 'Chờ điểm danh' (1) từ ngày hôm nay trở về trước
                var pendingShifts = await context.Lichlamviecs
                    .Where(l => l.TrangThai == 1 &&
                                l.CaLam != "Chưa xếp" &&
                                l.CaLam != "Nghỉ lễ" &&
                                l.CaLam != "Nghỉ phép" &&
                                l.NgayLamViec != null &&
                                l.NgayLamViec <= today)
                    .ToListAsync(cancellationToken);

                if (!pendingShifts.Any()) return;

                int absentCount = 0;

                foreach (var l in pendingShifts)
                {
                    bool isAbsent = false;

                    if (l.NgayLamViec.Value < today)
                    {
                        isAbsent = true; // Quá ngày làm việc mà chưa điểm danh -> Vắng
                    }
                    else if (l.NgayLamViec.Value == today)
                    {
                        TimeSpan shiftEndTime;
                        if (l.CaLam.Contains("Ca sáng")) shiftEndTime = new TimeSpan(17, 0, 0);
                        else if (l.CaLam.Contains("Ca chiều")) shiftEndTime = new TimeSpan(18, 0, 0);
                        else if (l.CaLam.Contains("Ca tối")) shiftEndTime = new TimeSpan(23, 0, 0);
                        else shiftEndTime = new TimeSpan(23, 59, 59);

                        if (currentTime >= shiftEndTime)
                        {
                            isAbsent = true;
                        }
                    }

                    if (isAbsent)
                    {
                        l.TrangThai = 5; // Vắng mặt
                        l.GhiChu = string.IsNullOrEmpty(l.GhiChu) ? "Hệ thống tự đánh vắng do quá giờ" : l.GhiChu;
                        absentCount++;
                    }
                }

                if (absentCount > 0)
                {
                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("[AttendanceWorker] Đã tự động đánh vắng {Count} ca làm quá giờ lúc {Time}.", absentCount, VnTimeHelper.Now());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AttendanceWorker] Lỗi khi tự động đánh vắng ca làm quá giờ.");
            }
        }
    }
}
