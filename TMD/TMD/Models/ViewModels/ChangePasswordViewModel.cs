using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class ChangePasswordViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại")]
		[DataType(DataType.Password)]
		[Display(Name = "Mật khẩu hiện tại")]
		public string CurrentPassword { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
		[StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất {2} ký tự")]
		[DataType(DataType.Password)]
		[Display(Name = "Mật khẩu mới")]
		public string NewPassword { get; set; }

		[Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới")]
		[DataType(DataType.Password)]
		[Display(Name = "Xác nhận mật khẩu")]
		[Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
		public string ConfirmPassword { get; set; }
	}
}