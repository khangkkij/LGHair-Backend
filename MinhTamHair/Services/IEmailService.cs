using MailKit.Security;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace MinhTamHair.Services
{
	public interface IEmailService
	{
		Task SendEmailAsync(string email, string subject, string htmlMessage);
	}

	public class EmailService : IEmailService
	{
		private readonly IConfiguration _config;

		public EmailService(IConfiguration config)
		{
			_config = config;
		}

		public async Task SendEmailAsync(string email, string subject, string htmlMessage)
		{
			var mailSettings = _config.GetSection("MailSettings");
			var message = new MimeMessage();

			message.From.Add(new MailboxAddress(mailSettings["DisplayName"], mailSettings["Mail"]));
			message.To.Add(new MailboxAddress("", email));
			message.Subject = subject;

			var bodyBuilder = new BodyBuilder { HtmlBody = htmlMessage };
			message.Body = bodyBuilder.ToMessageBody();

			using var client = new SmtpClient();
			try
			{
				await client.ConnectAsync(mailSettings["Host"], int.Parse(mailSettings["Port"]), SecureSocketOptions.StartTls);
				await client.AuthenticateAsync(mailSettings["Mail"], mailSettings["Password"]);
				await client.SendAsync(message);
				await client.DisconnectAsync(true);
			}
			catch (Exception ex)
			{
				throw new Exception("Lỗi gửi mail: " + ex.Message);
			}
		}
	}
}
