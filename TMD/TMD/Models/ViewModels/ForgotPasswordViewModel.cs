using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class ForgotPasswordViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập email")]
		[EmailAddress(ErrorMessage = "Email không hợp lệ")]
		public string Email { get; set; }
	}

	public class VerifyOtpViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập email")]
		[EmailAddress(ErrorMessage = "Email không hợp lệ")]
		public string Email { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập mã OTP")]
		[StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự")]
		public string OtpCode { get; set; }
	}

	public class ResetPasswordViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập email")]
		[EmailAddress]
		public string Email { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập mã OTP")]
		public string OtpCode { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
		[StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
		[DataType(DataType.Password)]
		public string NewPassword { get; set; }

		[Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
		[Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; }
	}
}