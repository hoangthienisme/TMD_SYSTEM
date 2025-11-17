using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class ForgotPasswordViewModel
	{
		[Required(ErrorMessage = "Email là bắt buộc")]
		[EmailAddress(ErrorMessage = "Email không hợp lệ")]
		[Display(Name = "Email đã đăng ký")]
		public string Email { get; set; }
	}

	public class VerifyOtpViewModel
	{
		[Required]
		public string Email { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập mã OTP")]
		[StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 chữ số")]
		[RegularExpression(@"^\d{6}$", ErrorMessage = "Mã OTP chỉ bao gồm số")]
		[Display(Name = "Mã OTP")]
		public string OtpCode { get; set; }
	}

	public class ResetPasswordViewModel
	{
		[Required]
		public string Email { get; set; }

		[Required]
		public string OtpCode { get; set; }

		[Required(ErrorMessage = "Mật khẩu mới là bắt buộc")]
		[StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
		[DataType(DataType.Password)]
		[Display(Name = "Mật khẩu mới")]
		public string NewPassword { get; set; }

		[Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
		[DataType(DataType.Password)]
		[Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
		[Display(Name = "Xác nhận mật khẩu")]
		public string ConfirmPassword { get; set; }
	}
}