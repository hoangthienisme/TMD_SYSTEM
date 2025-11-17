// Models/ViewModels/UpdateProfileViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	public class UpdateProfileViewModel
	{
		[Required(ErrorMessage = "Họ tên không được để trống")]
		public string FullName { get; set; }

		[EmailAddress(ErrorMessage = "Email không hợp lệ")]
		public string? Email { get; set; }

		[Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
		public string? PhoneNumber { get; set; }
	}
}