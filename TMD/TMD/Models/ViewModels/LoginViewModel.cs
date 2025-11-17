using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class LoginViewModel
	{
		[Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
		[Display(Name = "Tên đăng nhập")]
		public string Username { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
		[DataType(DataType.Password)]
		[Display(Name = "Mật khẩu")]
		public string Password { get; set; } = null!;

		[Display(Name = "Ghi nhớ đăng nhập")]
		public bool RememberMe { get; set; }
	}
}