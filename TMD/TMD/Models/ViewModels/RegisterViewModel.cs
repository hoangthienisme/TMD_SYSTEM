using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class RegisterViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
		[StringLength(50, ErrorMessage = "Tên đăng nhập tối đa 50 ký tự")]
		[RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "Tên đăng nhập chỉ được chứa chữ, số, dấu gạch dưới (_), gạch ngang (-) hoặc dấu chấm (.)")]
		[Display(Name = "Tên đăng nhập")]
		public string Username { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
		[StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6-100 ký tự")]
		[RegularExpression(@"^(?=.*[a-zA-Z])(?=.*\d)(?=.*[@$!%*?&_\-])[A-Za-z\d@$!%*?&_\-]{6,}$",
			ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự, bao gồm chữ cái, số và ký tự đặc biệt (@$!%*?&_-)")]
		[DataType(DataType.Password)]
		[Display(Name = "Mật khẩu")]
		public string Password { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập lại mật khẩu")]
		[DataType(DataType.Password)]
		[Compare("Password", ErrorMessage = "Mật khẩu không khớp")]
		[Display(Name = "Nhập lại mật khẩu")]
		public string ConfirmPassword { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập họ tên")]
		[StringLength(100)]
		[Display(Name = "Họ và tên")]
		public string FullName { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập email")]
		[EmailAddress(ErrorMessage = "Email không hợp lệ")]
		[Display(Name = "Email")]
		public string Email { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
		[Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
		[RegularExpression(@"^(0[3|5|7|8|9])+([0-9]{8})$", ErrorMessage = "Số điện thoại không hợp lệ (VD: 0912345678)")]
		[Display(Name = "Số điện thoại")]
		public string PhoneNumber { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng chọn phòng ban")]
		[Display(Name = "Phòng ban")]
		public int DepartmentId { get; set; }

		[Required(ErrorMessage = "Vui lòng chọn vai trò")]
		public int RoleId { get; set; }
	}
}