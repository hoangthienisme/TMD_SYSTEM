using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Helpers;
using TMD.Models;

namespace TMDSystem.Controllers
{
	public class WorkScheduleController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;

		public WorkScheduleController(TmdContext context, AuditHelper auditHelper)
		{
			_context = context;
			_auditHelper = auditHelper;
		}

		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("RoleName") == "Admin";
		}

		// ============================================
		// DANH SÁCH LỊCH ĐẶC BIỆT
		// ============================================
		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var exceptions = await _context.WorkScheduleExceptions
				.Include(e => e.User)
				.Include(e => e.Department)
				.Where(e => e.IsActive == true)
				.OrderByDescending(e => e.WorkDate)
				.ToListAsync();

			ViewBag.Users = await _context.Users
				.Where(u => u.IsActive == true)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			ViewBag.Departments = await _context.Departments
				.Where(d => d.IsActive == true)
				.OrderBy(d => d.DepartmentName)
				.ToListAsync();

			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"WorkScheduleExceptions",
				0,
				"Xem danh sách lịch làm việc đặc biệt"
			);

			return View(exceptions);
		}

		// ============================================
		// TẠO LỊCH ĐẶC BIỆT
		// ============================================
		[HttpPost]
		public async Task<IActionResult> Create([FromBody] CreateScheduleExceptionRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"WorkScheduleExceptions",
					"Không có quyền",
					null
				);
				return Json(new { success = false, message = "Không có quyền!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId").Value;

				var exception = new WorkScheduleException
				{
					WorkDate = DateOnly.FromDateTime(request.WorkDate),
					UserId = request.UserId > 0 ? request.UserId : null,
					DepartmentId = request.DepartmentId > 0 ? request.DepartmentId : null,
					CheckInStartTime = request.CheckInStartTime.HasValue
						? TimeOnly.FromTimeSpan(request.CheckInStartTime.Value)
						: null,
					CheckInStandardTime = request.CheckInStandardTime.HasValue
						? TimeOnly.FromTimeSpan(request.CheckInStandardTime.Value)
						: null,
					CheckOutMinTime = request.CheckOutMinTime.HasValue
						? TimeOnly.FromTimeSpan(request.CheckOutMinTime.Value)
						: null,
					StandardHours = request.StandardHours,
					SalaryMultiplier = request.SalaryMultiplier,
					OvertimeMultiplier = request.OvertimeMultiplier,
					Description = request.Description,
					ExceptionType = request.ExceptionType ?? "Normal",
					IsActive = true,
					CreatedBy = adminId,
					CreatedAt = DateTime.Now
				};

				_context.WorkScheduleExceptions.Add(exception);
				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					adminId,
					"CREATE",
					"WorkScheduleExceptions",
					exception.ExceptionId,
					null,
					new
					{
						exception.WorkDate,
						exception.SalaryMultiplier,
						exception.ExceptionType,
						exception.Description
					},
					$"Tạo lịch đặc biệt ngày {exception.WorkDate:dd/MM/yyyy} - {exception.Description}",
					new Dictionary<string, object>
					{
						{ "AppliesTo", exception.UserId.HasValue ? "User" : exception.DepartmentId.HasValue ? "Department" : "All" },
						{ "CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = "Tạo lịch đặc biệt thành công!"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"WorkScheduleExceptions",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// XÓA LỊCH ĐẶC BIỆT
		// ============================================
		[HttpPost]
		public async Task<IActionResult> Delete([FromBody] DeleteScheduleRequest request)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền!" });

			try
			{
				var exception = await _context.WorkScheduleExceptions
					.FindAsync(request.ExceptionId);

				if (exception == null)
					return Json(new { success = false, message = "Không tìm thấy lịch!" });

				var adminId = HttpContext.Session.GetInt32("UserId").Value;

				exception.IsActive = false;
				exception.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"DELETE",
					"WorkScheduleExceptions",
					exception.ExceptionId,
					null,
					null,
					$"Xóa lịch đặc biệt ngày {exception.WorkDate:dd/MM/yyyy}"
				);

				return Json(new { success = true, message = "Xóa thành công!" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// REQUEST MODELS
		// ============================================
		public class CreateScheduleExceptionRequest
		{
			public DateTime WorkDate { get; set; }
			public int? UserId { get; set; }
			public int? DepartmentId { get; set; }
			public TimeSpan? CheckInStartTime { get; set; }
			public TimeSpan? CheckInStandardTime { get; set; }
			public TimeSpan? CheckOutMinTime { get; set; }
			public decimal? StandardHours { get; set; }
			public decimal SalaryMultiplier { get; set; } = 1.0m;
			public decimal? OvertimeMultiplier { get; set; }
			public string? Description { get; set; }
			public string? ExceptionType { get; set; }
		}

		public class DeleteScheduleRequest
		{
			public int ExceptionId { get; set; }
		}
	}
}