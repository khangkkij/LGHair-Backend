using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MinhTamHair.Services
{
    public interface IQrEmailService
    {
        Task SendBookingEmailAsync(string toEmail, string customerName, string bookingId, DateTime bookingDate, decimal totalAmount, decimal? depositAmount = null);
    }

    public class QrEmailService : IQrEmailService
    {
        private readonly IConfiguration _configuration;

        public QrEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendBookingEmailAsync(string toEmail, string customerName, string bookingId, DateTime bookingDate, decimal totalAmount, decimal? depositAmount = null)
        {
            var senderEmail = _configuration["MailSettings:Mail"] ?? _configuration["EmailSettings:SenderEmail"];
            var password = _configuration["MailSettings:Password"] ?? _configuration["EmailSettings:Password"];
            var senderName = _configuration["MailSettings:DisplayName"] ?? _configuration["EmailSettings:SenderName"] ?? "LGHair Salon";
            var host = _configuration["MailSettings:Host"] ?? "smtp.gmail.com";
            var port = int.TryParse(_configuration["MailSettings:Port"], out var p) ? p : 587;

            if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(toEmail))
                return;

            decimal coc10 = depositAmount ?? Math.Round(totalAmount * 0.1m);
            decimal conLai = totalAmount - coc10;
            string qrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={bookingId}";

            string body = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: auto; padding: 25px; border: 1px solid #e2e8f0; border-radius: 16px; background: #ffffff;'>
                <div style='text-align: center; margin-bottom: 24px;'>
                    <h2 style='color: #0f172a; margin: 0; font-size: 24px; font-weight: 800; letter-spacing: 1px;'>LGHair SALON</h2>
                    <p style='color: #059669; font-size: 13px; margin-top: 6px; font-weight: bold; text-transform: uppercase;'>✓ XÁC NHẬN ĐẶT LỊCH HẸN THÀNH CÔNG</p>
                </div>
                
                <p style='font-size: 14px; color: #334155;'>Xin chào <b>{customerName}</b>,</p>
                <p style='font-size: 14px; color: #334155;'>Cảm ơn Quý khách đã đặt lịch làm tóc tại <b>LGHair Salon</b>. Dưới đây là thông tin chi tiết lịch hẹn và mã QR Check-in:</p>
                
                <div style='background: #f8fafc; padding: 18px; border-radius: 12px; margin: 20px 0; border: 1px solid #e2e8f0;'>
                    <p style='margin: 6px 0; font-size: 14px;'><b>Mã lịch hẹn:</b> <span style='color: #2563eb; font-weight: bold;'>#{bookingId}</span></p>
                    <p style='margin: 6px 0; font-size: 14px;'><b>Thời gian hẹn:</b> <span style='color: #0f172a; font-weight: bold;'>{bookingDate:dd/MM/yyyy HH:mm}</span></p>
                    <p style='margin: 6px 0; font-size: 14px;'><b>Tổng tiền dịch vụ:</b> {totalAmount:N0} VNĐ</p>
                    <p style='margin: 6px 0; font-size: 14px; color: #059669;'><b>Số tiền đã đặt cọc (10%):</b> {coc10:N0} VNĐ</p>
                    <p style='margin: 6px 0; font-size: 14px; color: #b91c1c;'><b>Số tiền còn lại thanh toán tại Salon:</b> {conLai:N0} VNĐ</p>
                </div>

                <div style='text-align: center; margin: 30px 0; padding: 20px; background: #fafafa; border-radius: 12px; border: 2px dashed #cbd5e1;'>
                    <p style='font-size: 13px; color: #475569; margin-top: 0; font-weight: bold;'>MÃ QR CHECK-IN NHANH TẠI QUẦY SALON:</p>
                    <img src='{qrCodeUrl}' alt='Mã QR Check-in' style='width: 200px; height: 200px; border: 4px solid #ffffff; border-radius: 12px; box-shadow: 0 4px 15px rgba(0,0,0,0.1);' />
                    <p style='font-size: 11px; color: #94a3b8; margin-bottom: 0; margin-top: 8px;'>Quý khách chỉ cần đưa mã QR này cho lễ tân khi đến Salon</p>
                </div>
                
                <div style='background: #fffbeb; padding: 12px 16px; border-radius: 8px; border-left: 4px solid #f59e0b; font-size: 12px; color: #92400e;'>
                    <b>Lưu ý quan trọng:</b> Quý khách vui lòng có mặt trước giờ hẹn 5 - 10 phút. Nếu trễ hẹn quá 30 phút, lịch hẹn sẽ được tạm hủy tự động để đảm bảo quyền lợi phục vụ khách hàng khác.
                </div>

                <p style='margin-top: 30px; text-align: center; color: #64748b; font-size: 12px; border-top: 1px solid #f1f5f9; padding-top: 20px;'>
                    Hotline hỗ trợ: <b>0123.456.789</b> | Địa chỉ: <b>123 Đường ABC, Buôn Ma Thuột</b><br/>
                    Trân trọng,<br/><b>Đội ngũ LGHair Salon</b>
                </p>
            </div>";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = $"[LGHair] Xác nhận lịch hẹn #{bookingId} thành công";

            var bodyBuilder = new BodyBuilder { HtmlBody = body };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(senderEmail, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
