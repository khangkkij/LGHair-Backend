using System.Net;
using System.Net.Mail;

namespace MinhTamHair.Services
{
    // 1. Đổi tên Interface
    public interface IQrEmailService
    {
        Task SendBookingEmailAsync(string toEmail, string customerName, string bookingId, DateTime bookingDate, decimal totalAmount);
    }

    // 2. Đổi tên Class
    public class QrEmailService : IQrEmailService
    {
        private readonly IConfiguration _configuration;

        public QrEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendBookingEmailAsync(string toEmail, string customerName, string bookingId, DateTime bookingDate, decimal totalAmount)
        {
            var senderEmail = _configuration["EmailSettings:SenderEmail"];
            var password = _configuration["EmailSettings:Password"];
            var senderName = _configuration["EmailSettings:SenderName"];

            if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(toEmail))
                return;

            string qrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={bookingId}";

            string body = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: auto; padding: 20px; border: 1px solid #f1d592; border-radius: 15px; background: #fff;'>
                <div style='text-align: center; margin-bottom: 20px;'>
                    <h2 style='color: #d4af37; margin: 0;'>MINH TÂM HAIR SALON</h2>
                    <p style='color: #888; font-size: 12px; margin-top: 5px; text-transform: uppercase;'>Xác nhận lịch hẹn thành công</p>
                </div>
                
                <p>Xin chào <b>{customerName}</b>,</p>
                <p>Cảm ơn bạn đã tin tưởng và đặt lịch tại Salon. Dưới đây là thông tin đơn hàng và mã QR Check-in của bạn:</p>
                
                <div style='background: #fffaf0; padding: 15px; border-radius: 10px; margin: 20px 0; border: 1px solid #f1e4bc;'>
                    <p style='margin: 5px 0;'><b>Mã lịch hẹn:</b> <span style='color: #d4af37;'>#{bookingId}</span></p>
                    <p style='margin: 5px 0;'><b>Thời gian:</b> {bookingDate:dd/MM/yyyy HH:mm}</p>
                    <p style='margin: 5px 0;'><b>Tổng tiền:</b> {totalAmount:N0} VNĐ</p>
                </div>

                <div style='text-align: center; margin-top: 30px;'>
                    <p style='font-size: 13px; color: #555;'><i>Vui lòng đưa mã QR này cho nhân viên quầy khi đến Salon:</i></p>
                    <img src='{qrCodeUrl}' alt='Mã QR Check-in' style='border: 3px solid #d4af37; border-radius: 10px; padding: 5px; box-shadow: 0 4px 10px rgba(0,0,0,0.1);' />
                </div>
                
                <p style='margin-top: 40px; text-align: center; color: #888; font-size: 12px; border-top: 1px dashed #eee; padding-top: 20px;'>
                    Trân trọng,<br/><b>Đội ngũ Minh Tâm Hair Salon</b>
                </p>
            </div>";

            using (var client = new SmtpClient("smtp.gmail.com", 587))
            {
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(senderEmail, password);

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = $"[Minh Tâm Hair] Xác nhận lịch hẹn #{bookingId}",
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(toEmail);

                await client.SendMailAsync(mailMessage);
            }
        }
    }
}