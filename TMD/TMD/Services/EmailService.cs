using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TMDSystem.Services
{
	public interface IEmailService
	{
		Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string userName);
	}

	public class EmailService : IEmailService
	{
		private readonly IConfiguration _configuration;
		private readonly ILogger<EmailService> _logger;

		public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
		{
			_configuration = configuration;
			_logger = logger;
		}

		public async Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string userName)
		{
			try
			{
				// Lấy thông tin từ appsettings.json
				var smtpHost = _configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
				var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
				var senderEmail = _configuration["EmailSettings:SenderEmail"];
				var senderName = _configuration["EmailSettings:SenderName"] ?? "TMD System";
				var appPassword = _configuration["EmailSettings:AppPassword"];

				// Validate configuration
				if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(appPassword))
				{
					_logger.LogError("Email configuration is missing. Please check appsettings.json");
					return false;
				}

				using (var client = new SmtpClient(smtpHost, smtpPort))
				{
					client.EnableSsl = true;
					client.UseDefaultCredentials = false;
					client.Credentials = new NetworkCredential(senderEmail, appPassword);
					client.Timeout = 30000; // 30 seconds timeout

					var mailMessage = new MailMessage
					{
						From = new MailAddress(senderEmail, senderName),
						Subject = "Mã OTP Đặt Lại Mật Khẩu - TMD System",
						IsBodyHtml = true,
						Body = GenerateOtpEmailBody(otpCode, userName)
					};

					mailMessage.To.Add(toEmail);

					await client.SendMailAsync(mailMessage);
					_logger.LogInformation($"OTP email sent successfully to {toEmail}");
					return true;
				}
			}
			catch (SmtpException smtpEx)
			{
				_logger.LogError($"SMTP Error sending email: {smtpEx.Message}");
				_logger.LogError($"Status Code: {smtpEx.StatusCode}");
				return false;
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error sending email: {ex.Message}");
				_logger.LogError($"Stack Trace: {ex.StackTrace}");
				return false;
			}
		}

		private string GenerateOtpEmailBody(string otpCode, string userName)
		{
			return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; background: #f4f4f4; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
                   color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: white; padding: 30px; border-radius: 0 0 10px 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .otp-box {{ background: #f0f4ff; border: 2px dashed #667eea; 
                    padding: 25px; text-align: center; margin: 25px 0; border-radius: 8px; }}
        .otp-code {{ font-size: 36px; font-weight: bold; color: #667eea; 
                     letter-spacing: 10px; font-family: 'Courier New', monospace; margin: 10px 0; }}
        .warning {{ background: #fff3cd; border-left: 4px solid #ffc107; 
                    padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .footer {{ text-align: center; margin-top: 30px; padding-top: 20px; 
                   border-top: 1px solid #e0e0e0; color: #666; font-size: 12px; }}
        .button {{ display: inline-block; padding: 12px 30px; background: #667eea; 
                   color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        ul {{ margin: 10px 0; padding-left: 20px; }}
        li {{ margin: 5px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1 style='margin: 0; font-size: 28px;'>🔐 Đặt Lại Mật Khẩu</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>TMD System</p>
        </div>
        <div class='content'>
            <p style='font-size: 16px;'>Xin chào <strong style='color: #667eea;'>{userName}</strong>,</p>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Để tiếp tục, vui lòng sử dụng mã OTP bên dưới:</p>
            
            <div class='otp-box'>
                <p style='margin: 0; color: #666; font-size: 14px;'>MÃ OTP CỦA BẠN:</p>
                <div class='otp-code'>{otpCode}</div>
                <p style='margin: 0; color: #999; font-size: 12px;'>Vui lòng nhập mã này trong vòng 5 phút</p>
            </div>

            <div class='warning'>
                <strong style='color: #856404;'>⚠️ Lưu ý quan trọng:</strong>
                <ul style='margin-top: 10px;'>
                    <li>Mã OTP có hiệu lực trong <strong>5 phút</strong></li>
                    <li>Không chia sẻ mã này với bất kỳ ai</li>
                    <li>TMD System sẽ không bao giờ yêu cầu mã OTP qua điện thoại</li>
                    <li>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này</li>
                </ul>
            </div>

            <p style='margin-top: 30px; color: #666;'>
                Nếu bạn gặp vấn đề, vui lòng liên hệ bộ phận hỗ trợ của chúng tôi.
            </p>

            <p style='margin-top: 30px;'>
                Trân trọng,<br>
                <strong style='color: #667eea;'>TMD System Team</strong>
            </p>
        </div>
        <div class='footer'>
            <p style='margin: 5px 0;'>Email này được gửi tự động, vui lòng không trả lời.</p>
            <p style='margin: 5px 0;'>&copy; 2025 TMD System. All rights reserved.</p>
            <p style='margin: 5px 0;'>
                <a href='#' style='color: #667eea; text-decoration: none;'>Chính sách bảo mật</a> | 
                <a href='#' style='color: #667eea; text-decoration: none;'>Điều khoản sử dụng</a>
            </p>
        </div>
    </div>
</body>
</html>";
		}
	}
}