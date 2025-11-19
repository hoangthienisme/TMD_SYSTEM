using System.ComponentModel.DataAnnotations;

namespace TMDSystem.Models.ViewModels
{
	// ===== LEAVE REQUEST =====
	public class CreateLeaveRequestViewModel
	{
		[Required(ErrorMessage = "Vui lòng chọn loại nghỉ phép")]
		public string LeaveType { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu")]
		public string StartDate { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng chọn ngày kết thúc")]
		public string EndDate { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập lý do")]
		[StringLength(1000, ErrorMessage = "Lý do không được quá 1000 ký tự")]
		public string Reason { get; set; } = null!;

		public IFormFile? ProofDocument { get; set; }
	}

	// ===== LATE REQUEST =====
	public class CreateLateRequestViewModel
	{
		[Required(ErrorMessage = "Vui lòng chọn ngày")]
		public string RequestDate { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng chọn giờ đến dự kiến")]
		public string ExpectedArrivalTime { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập lý do")]
		[StringLength(1000, ErrorMessage = "Lý do không được quá 1000 ký tự")]
		public string Reason { get; set; } = null!;

		public IFormFile? ProofDocument { get; set; }
	}

	// ===== OVERTIME REQUEST =====
	public class CreateOvertimeRequestViewModel
	{
		[Required(ErrorMessage = "Vui lòng chọn ngày làm việc")]
		public string WorkDate { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng chọn giờ kết thúc")]
		public string ActualCheckOutTime { get; set; } = null!;

		[Required(ErrorMessage = "Vui lòng nhập số giờ tăng ca")]
		[Range(0.1, 12, ErrorMessage = "Số giờ tăng ca phải từ 0.1 đến 12")]
		public decimal OvertimeHours { get; set; }

		[Required(ErrorMessage = "Vui lòng nhập lý do")]
		[StringLength(1000, ErrorMessage = "Lý do không được quá 1000 ký tự")]
		public string Reason { get; set; } = null!;

		[StringLength(1000, ErrorMessage = "Mô tả công việc không được quá 1000 ký tự")]
		public string? TaskDescription { get; set; }
	}
}