using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinhTamHair.Models;
using MinhTamHair.Services.TimeHelper;

namespace MinhTamHair.Services
{
    public class VoucherExpirationBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VoucherExpirationBackgroundService> _logger;
        // Chạy định kỳ mỗi 30 phút một lần
        private readonly TimeSpan _period = TimeSpan.FromMinutes(30);

        public VoucherExpirationBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<VoucherExpirationBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_period);

            // Chạy 1 lần ngay khi server khởi động (để dọn dẹp ngay)
            await DoWorkAsync(stoppingToken);

            // Sau đó cứ mỗi 30 phút lặp lại 1 lần
            while (await timer.WaitForNextTickAsync(stoppingToken))
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
                await VoucherCartLogic.AutoDisableExpiredVouchersAsync(context, now, cancellationToken);

                _logger.LogInformation("[VoucherWorker] Đã quét và cập nhật voucher hết hạn lúc {Time}", now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VoucherWorker] Lỗi khi quét voucher hết hạn ngầm");
            }
        }
    }
}
