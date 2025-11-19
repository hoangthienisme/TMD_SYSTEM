using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Helpers;
using TMD.Models;

namespace TMDSystem.Controllers
{
	public class UserSalaryController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;

		public UserSalaryController(TmdContext context, AuditHelper auditHelper)
		{
			_context = context;
			_auditHelper = auditHelper;
		}

		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("RoleName") == "Admin";
		}

		// ============================================
		// DANH SÁCH CẤU HÌNH LƯƠNG NHÂN VIÊN
		// ============================================
		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var salarySettings = await _context.UserSalarySettings
				.Include(s => s.User)
					.ThenInclude(u => u.Department)
				.Where(s => s.IsActive == true)
				.OrderBy(s => s.User.FullName)
				.ToListAsync();

			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"UserSalarySettings",
				0,
				"Xem danh sách cấu hình lương nhân viên"
			);

			return View(salarySettings);
		}

		// ============================================
		// TẠO/CẬP NHẬT CẤU HÌNH LƯƠNG
		// ============================================
		[HttpPost]
		public async Task<IActionResult> CreateOrUpdate([FromBody] CreateSalarySettingRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE/UPDATE",
					"UserSalarySettings",
					"Không có quyền",
					new { UserId = request.UserId }
				);
				return Json(new { success = false, message = "Không có quyền!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId").Value;

				// Kiểm tra user tồn tại
				var user = await _context.Users.FindAsync(request.UserId);
				if (user == null)
				{
					return Json(new { success = false, message = "Không tìm thấy nhân viên!" });
				}

				// Tìm setting hiện tại
				var existing = await _context.UserSalarySettings
					.FirstOrDefaultAsync(s => s.UserId == request.UserId && s.IsActive == true);

				if (existing != null)
				{
					// UPDATE
					var oldValues = new
					{
						existing.SalaryType,
						existing.BaseSalary,
						existing.HourlyRate,
						existing.DefaultOvertimeRate,
						existing.AllowanceAmount
					};

					existing.SalaryType = request.SalaryType;
					existing.BaseSalary = request.BaseSalary;
					existing.HourlyRate = request.HourlyRate;
					existing.DefaultOvertimeRate = request.DefaultOvertimeRate;
					existing.AllowanceAmount = request.AllowanceAmount;
					existing.EffectiveFrom = DateOnly.FromDateTime(request.EffectiveFrom);
					existing.EffectiveTo = request.EffectiveTo.HasValue
						? DateOnly.FromDateTime(request.EffectiveTo.Value)
						: null;
					existing.UpdatedAt = DateTime.Now;
					existing.UpdatedBy = adminId;

					await _context.SaveChangesAsync();

					await _auditHelper.LogDetailedAsync(
						adminId,
						"UPDATE",
						"UserSalarySettings",
						existing.UserSalaryId,
						oldValues,
						new
						{
							existing.SalaryType,
							existing.BaseSalary,
							existing.HourlyRate,
							existing.DefaultOvertimeRate,
							existing.AllowanceAmount
						},
						$"Cập nhật cấu hình lương cho {user.FullName}",
						new Dictionary<string, object>
						{
							{ "UserName", user.FullName },
							{ "UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
						}
					);

					return Json(new { success = true, message = "Cập nhật cấu hình lương thành công!" });
				}
				else
				{
					// CREATE
					var newSetting = new UserSalarySetting
					{
						UserId = request.UserId,
						SalaryType = request.SalaryType,
						BaseSalary = request.BaseSalary,
						HourlyRate = request.HourlyRate,
						DefaultOvertimeRate = request.DefaultOvertimeRate,
						AllowanceAmount = request.AllowanceAmount,
						EffectiveFrom = DateOnly.FromDateTime(request.EffectiveFrom),
						EffectiveTo = request.EffectiveTo.HasValue
							? DateOnly.FromDateTime(request.EffectiveTo.Value)
							: null,
						IsActive = true,
						CreatedAt = DateTime.Now,
						CreatedBy = adminId
					};

					_context.UserSalarySettings.Add(newSetting);
					await _context.SaveChangesAsync();

					await _auditHelper.LogDetailedAsync(
						adminId,
						"CREATE",
						"UserSalarySettings",
						newSetting.UserSalaryId,
						null,
						new
						{
							newSetting.SalaryType,
							newSetting.BaseSalary,
							newSetting.HourlyRate,
							newSetting.DefaultOvertimeRate,
							newSetting.AllowanceAmount
						},
						$"Tạo cấu hình lương cho {user.FullName}",
						new Dictionary<string, object>
						{
							{ "UserName", user.FullName },
							{ "CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
						}
					);

					return Json(new { success = true, message = "Tạo cấu hình lương thành công!" });
				}
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE/UPDATE",
					"UserSalarySettings",
					$"Exception: {ex.Message}",
					new { UserId = request.UserId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// LẤY THÔNG TIN LƯƠNG CỦA 1 USER
		// ============================================
		[HttpGet]
		public async Task<IActionResult> GetUserSalary(int userId)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền!" });

			try
			{
				var setting = await _context.UserSalarySettings
					.Include(s => s.User)
					.FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive == true);

				if (setting == null)
				{
					// Trả về giá trị mặc định từ SystemSettings
					var baseSalary = await GetSettingValue("BASE_SALARY", "5000000");
					var overtimeRate = await GetSettingValue("OVERTIME_RATE", "1.5");

					return Json(new
					{
						success = true,
						hasCustomSalary = false,
						salaryType = "Monthly",
						baseSalary = decimal.Parse(baseSalary),
						hourlyRate = (decimal?)null,
						defaultOvertimeRate = decimal.Parse(overtimeRate),
						allowanceAmount = 0m,
						effectiveFrom = DateTime.Now,
						effectiveTo = (DateTime?)null
					});
				}

				return Json(new
				{
					success = true,
					hasCustomSalary = true,
					userSalaryId = setting.UserSalaryId,
					userId = setting.UserId,
					salaryType = setting.SalaryType,
					baseSalary = setting.BaseSalary,
					hourlyRate = setting.HourlyRate,
					defaultOvertimeRate = setting.DefaultOvertimeRate,
					allowanceAmount = setting.AllowanceAmount,
					effectiveFrom = setting.EffectiveFrom,
					effectiveTo = setting.EffectiveTo
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// HELPER
		// ============================================
		private async Task<string> GetSettingValue(string key, string defaultValue)
		{
			var setting = await _context.SystemSettings
				.FirstOrDefaultAsync(s => s.SettingKey == key && s.IsActive == true);
			return setting?.SettingValue ?? defaultValue;
		}

		// ============================================
		// REQUEST MODELS
		// ============================================
		public class CreateSalarySettingRequest
		{
			public int UserId { get; set; }
			public string SalaryType { get; set; } = "Monthly";
			public decimal BaseSalary { get; set; }
			public decimal? HourlyRate { get; set; }
			public decimal DefaultOvertimeRate { get; set; } = 1.5m;
			public decimal AllowanceAmount { get; set; } = 0;
			public DateTime EffectiveFrom { get; set; }
			public DateTime? EffectiveTo { get; set; }
		}
	}
}