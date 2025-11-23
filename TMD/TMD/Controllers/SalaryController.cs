using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMD.Models;
using TMDSystem.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace TMDSystem.Controllers
{
	public class SalaryController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;

		public SalaryController(TmdContext context, AuditHelper auditHelper)
		{
			_context = context;
			_auditHelper = auditHelper;
		}

		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("RoleName") == "Admin";
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var baseSalary = GetSettingValueAsDecimal("BASE_SALARY", 5000000);
			var overtimeRate = GetSettingValueAsDecimal("OVERTIME_RATE", 1.5m);
			var workDaysPerMonth = GetSettingValueAsInt("WORK_DAYS_PER_MONTH", 26);
			var standardHoursPerDay = GetSettingValueAsDecimal("STANDARD_HOURS_PER_DAY", 8);

			ViewBag.BaseSalary = baseSalary;
			ViewBag.OvertimeRate = overtimeRate;
			ViewBag.WorkDaysPerMonth = workDaysPerMonth;
			ViewBag.StandardHoursPerDay = standardHoursPerDay;

			var departments = await _context.Departments
				.Where(d => d.IsActive == true)
				.OrderBy(d => d.DepartmentName)
				.ToListAsync();

			var users = await _context.Users
				.Where(u => u.IsActive == true)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			ViewBag.Departments = departments;
			ViewBag.Users = users;

			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"Salary",
				0,
				"Xem trang quản lý lương"
			);

			return View();
		}

		// ✅ TÍNH LƯƠNG - SỬ DỤNG ApplyMethod
		[HttpPost]
		public async Task<IActionResult> PreviewSalary([FromBody] SalaryFilterRequest request)
		{
			try
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền truy cập!" });

				if (string.IsNullOrEmpty(request.FromDate) || string.IsNullOrEmpty(request.ToDate))
					return Json(new { success = false, message = "Vui lòng chọn khoảng thời gian!" });

				if (!DateTime.TryParse(request.FromDate, out var fromDate) || !DateTime.TryParse(request.ToDate, out var toDate))
					return Json(new { success = false, message = "Định dạng ngày không hợp lệ!" });

				if (fromDate > toDate)
					return Json(new { success = false, message = "Ngày bắt đầu phải nhỏ hơn ngày kết thúc!" });

				// ✅ LẤY CONFIG TỪ DATABASE
				var baseSalary = GetSettingValueAsDecimal("BASE_SALARY", 5000000);
				var overtimeRate = GetSettingValueAsDecimal("OVERTIME_RATE", 1.5m);
				var standardHoursPerDay = GetSettingValueAsDecimal("STANDARD_HOURS_PER_DAY", 8);
				var workDaysPerMonth = GetSettingValueAsInt("WORK_DAYS_PER_MONTH", 26);

				var query = _context.Attendances
					.Include(a => a.User)
					.ThenInclude(u => u.Department)
					.Where(a => a.WorkDate >= DateOnly.FromDateTime(fromDate) &&
							   a.WorkDate <= DateOnly.FromDateTime(toDate));

				if (request.UserId.HasValue && request.UserId > 0)
					query = query.Where(a => a.UserId == request.UserId);

				if (request.DepartmentId.HasValue && request.DepartmentId > 0)
					query = query.Where(a => a.User.DepartmentId == request.DepartmentId);

				var attendances = await query
					.OrderBy(a => a.User.FullName)
					.ThenBy(a => a.WorkDate)
					.ToListAsync();

				var salaryData = attendances
	.GroupBy(a => a.UserId)
	.Select(group => CalculateEmployeeSalary(
		group.Key,
		group,
		baseSalary,
		overtimeRate,
		standardHoursPerDay,
		workDaysPerMonth
	))
	.Where(s => s != null)
	.OrderBy(s => s.FullName)
	.ToList();

				return Json(new { success = true, salaryData });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ✅ TÍNH LƯƠNG THEO ApplyMethod
		// ✅ HÀM TÍNH LƯƠNG ĐÃ FIX FULL (Copy đè vào hàm cũ)
		private EmployeeSalaryDto CalculateEmployeeSalary(
			int userId,
			IEnumerable<Attendance> attendances,
			decimal baseSalaryInput, // Nhận vào giá trị từ config (ví dụ 6500000)
			decimal overtimeRate,
			decimal standardHoursPerDay,
			int workDaysPerMonth)
		{
			var attendanceList = attendances.ToList();
			if (!attendanceList.Any()) return null;

			var user = attendanceList.First().User;
			if (user == null) return null;

			// 1. CHUẨN HÓA LƯƠNG CỨNG (Phòng hờ nhập thiếu số 0)
			decimal baseSalary = baseSalaryInput;
			if (baseSalary < 2000000 && baseSalary > 1000) baseSalary *= 10; // Nhập 650.000 -> 6.500.000
			else if (baseSalary <= 1000) baseSalary *= 1000000; // Nhập 6.5 -> 6.500.000

			// 2. TÍNH ĐƠN GIÁ (Làm tròn ngay từ đầu để tránh số lẻ)
			decimal dailyRate = 0;
			if (workDaysPerMonth > 0)
				dailyRate = Math.Round(baseSalary / workDaysPerMonth, 0); // Ví dụ: 250.000

			decimal hourlyRate = 0;
			if (standardHoursPerDay > 0)
				hourlyRate = Math.Round(dailyRate / standardHoursPerDay, 0); // Ví dụ: 31.250

			// Các biến tích lũy
			int workedDays = 0;
			int lateDays = attendanceList.Count(a => a.IsLate == true);

			decimal totalStandardHours = 0; // Giờ hành chính
			decimal totalOvertimeHours = 0; // Giờ tăng ca

			decimal salaryWithMultiplier = 0;
			decimal overtimeSalary = 0;
			decimal totalDeduction = attendanceList.Sum(a => a.DeductionAmount);

			foreach (var att in attendanceList)
			{
				// Chỉ tính những ngày có Check-in
				if (att.CheckInTime.HasValue)
				{
					workedDays++;

					// --- A. TÍNH LƯƠNG NGÀY CƠ BẢN ---
					var multiplier = att.SalaryMultiplier ?? 1.0m;
					salaryWithMultiplier += Math.Round(dailyRate * multiplier, 0);

					// --- B. TÍNH GIỜ HÀNH CHÍNH ---
					// Nếu TotalHours có dữ liệu thì lấy, không thì mặc định là 8 tiếng
					decimal dayStandardHours = (att.TotalHours.HasValue && att.TotalHours > 0)
										? att.TotalHours.Value
										: standardHoursPerDay;
					totalStandardHours += dayStandardHours;

					// --- C. TÍNH TĂNG CA ---
					if (att.ApprovedOvertimeHours > 0)
					{
						totalOvertimeHours += att.ApprovedOvertimeHours;

						// Công thức: Giờ OT * Đơn giá giờ * Hệ số OT
						decimal otMoney = att.ApprovedOvertimeHours * hourlyRate * overtimeRate;

						// Cộng dồn và làm tròn tiền
						overtimeSalary += Math.Round(otMoney, 0);
					}
				}
			}

			// 3. TÍNH CÁC KHOẢN PHỤ CẤP (Dynamic Settings)
			var dynamicSettings = _context.SystemSettings
				.Where(s => s.Category == "Salary" && s.IsActive == true && !string.IsNullOrEmpty(s.ApplyMethod))
				.ToList();

			decimal totalAllowances = 0;
			decimal totalBonuses = 0;
			decimal totalDynamicDeductions = 0;

			foreach (var setting in dynamicSettings)
			{
				if (!decimal.TryParse(setting.SettingValue, out var val)) continue;

				switch (setting.ApplyMethod?.ToUpper())
				{
					case "FIXED_MONTHLY":
						totalAllowances += val;
						break;
					case "PER_WORKDAY":
						totalAllowances += (val * workedDays);
						break;
					case "MULTIPLIER":
						if (setting.Unit == "%")
							totalBonuses += Math.Round(salaryWithMultiplier * val / 100, 0);
						else
							totalBonuses += Math.Round(salaryWithMultiplier * (val - 1), 0);
						break;
					case "PER_LATE_INSTANCE":
						totalDynamicDeductions += (val * lateDays);
						break;
					case "PERCENT_DEDUCTION":
						totalDynamicDeductions += Math.Round(salaryWithMultiplier * val / 100, 0);
						break;
					case "CONDITIONAL":
						if (setting.SettingKey == "BONUS_FULL_ATTENDANCE" && workedDays >= workDaysPerMonth && lateDays == 0)
							totalBonuses += val;
						break;
				}
			}

			// 4. TỔNG HỢP CUỐI CÙNG
			decimal finalBaseComponent = salaryWithMultiplier + totalAllowances + totalBonuses;
			decimal finalDeduction = totalDeduction + totalDynamicDeductions;
			decimal finalTotalSalary = finalBaseComponent + overtimeSalary - finalDeduction;

			// Trả về DTO
			return new EmployeeSalaryDto
			{
				UserId = userId,
				FullName = user.FullName,
				DepartmentName = user.Department?.DepartmentName ?? "N/A",
				WorkedDays = workedDays,
				LateDays = lateDays,

				// ✅ QUAN TRỌNG: Tổng giờ = Giờ hành chính + Giờ Tăng Ca
				TotalHours = Math.Round(totalStandardHours + totalOvertimeHours, 2),

				OvertimeHours = Math.Round(totalOvertimeHours, 2),

				// Các khoản tiền đã được làm tròn
				BaseSalary = Math.Round(finalBaseComponent, 0),
				OvertimeSalary = Math.Round(overtimeSalary, 0),
				Deduction = Math.Round(finalDeduction, 0),
				TotalSalary = Math.Round(finalTotalSalary, 0)
			};
		}

		// ✅ EXPORT EXCEL
		[HttpPost]
		public async Task<IActionResult> ExportSalaryExcel([FromBody] SalaryFilterRequest request)
		{
			try
			{
				if (!IsAdmin())
					return BadRequest("Không có quyền export!");

				if (!DateTime.TryParse(request.FromDate, out var fromDate) || !DateTime.TryParse(request.ToDate, out var toDate))
					return BadRequest("Định dạng ngày không hợp lệ!");

				var baseSalary = GetSettingValueAsDecimal("BASE_SALARY", 5000000);
				var overtimeRate = GetSettingValueAsDecimal("OVERTIME_RATE", 1.5m);
				var standardHoursPerDay = GetSettingValueAsDecimal("STANDARD_HOURS_PER_DAY", 8);
				var workDaysPerMonth = GetSettingValueAsInt("WORK_DAYS_PER_MONTH", 26);

				var query = _context.Attendances
					.Include(a => a.User)
					.ThenInclude(u => u.Department)
					.Where(a => a.WorkDate >= DateOnly.FromDateTime(fromDate) &&
							   a.WorkDate <= DateOnly.FromDateTime(toDate));

				if (request.UserId.HasValue && request.UserId > 0)
					query = query.Where(a => a.UserId == request.UserId);

				if (request.DepartmentId.HasValue && request.DepartmentId > 0)
					query = query.Where(a => a.User.DepartmentId == request.DepartmentId);

				var attendances = await query.ToListAsync();

				var salaryData = attendances
					.GroupBy(a => a.UserId)
					.Select(group => CalculateEmployeeSalary(
						group.Key,
						group,
						baseSalary,
						overtimeRate,
						standardHoursPerDay,
						workDaysPerMonth
					))
					.Where(s => s != null)
					.OrderBy(s => s.FullName)
					.ToList();

				var excelBytes = GenerateExcelFile(salaryData, fromDate, toDate);

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"EXPORT",
					"Salary",
					null,
					null,
					null,
					$"Export bảng lương từ {fromDate:dd/MM/yyyy} đến {toDate:dd/MM/yyyy}"
				);

				var fileName = $"BangLuong_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.xlsx";
				return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
			}
			catch (Exception ex)
			{
				return BadRequest($"Lỗi export: {ex.Message}");
			}
		}

		private byte[] GenerateExcelFile(List<EmployeeSalaryDto> salaryData, DateTime fromDate, DateTime toDate)
		{
			using (var workbook = new ClosedXML.Excel.XLWorkbook())
			{
				var worksheet = workbook.Worksheets.Add("Bảng Lương");

				worksheet.Column(1).Width = 5;
				worksheet.Column(2).Width = 10;
				worksheet.Column(3).Width = 15;
				worksheet.Column(4).Width = 15;
				worksheet.Column(5).Width = 10;
				worksheet.Column(6).Width = 8;
				worksheet.Column(7).Width = 10;
				worksheet.Column(8).Width = 10;
				worksheet.Column(9).Width = 15;
				worksheet.Column(10).Width = 15;
				worksheet.Column(11).Width = 15;
				worksheet.Column(12).Width = 15;

				var headerRow = worksheet.Row(1);
				headerRow.Cell(1).Value = "STT";
				headerRow.Cell(2).Value = "Mã NV";
				headerRow.Cell(3).Value = "Họ Tên";
				headerRow.Cell(4).Value = "Phòng Ban";
				headerRow.Cell(5).Value = "Ngày Công";
				headerRow.Cell(6).Value = "Đi Muộn";
				headerRow.Cell(7).Value = "Tổng Giờ";
				headerRow.Cell(8).Value = "Tăng Ca";
				headerRow.Cell(9).Value = "Lương CB";
				headerRow.Cell(10).Value = "Lương TC";
				headerRow.Cell(11).Value = "Khấu Trừ";
				headerRow.Cell(12).Value = "Tổng Lương";

				var headerRange = worksheet.Range($"A1:L1");
				headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(102, 126, 234);
				headerRange.Style.Font.Bold = true;
				headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
				headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

				int row = 2;
				decimal totalGrandSalary = 0;

				foreach (var salary in salaryData)
				{
					worksheet.Cell(row, 1).Value = row - 1;
					worksheet.Cell(row, 2).Value = $"NV{salary.UserId.ToString().PadLeft(4, '0')}";
					worksheet.Cell(row, 3).Value = salary.FullName;
					worksheet.Cell(row, 4).Value = salary.DepartmentName;
					worksheet.Cell(row, 5).Value = salary.WorkedDays;
					worksheet.Cell(row, 6).Value = salary.LateDays;
					worksheet.Cell(row, 7).Value = FormatHoursToDisplay(salary.TotalHours);
					worksheet.Cell(row, 8).Value = FormatHoursToDisplay(salary.OvertimeHours);
					worksheet.Cell(row, 9).Value = Math.Round(salary.BaseSalary, 0);
					worksheet.Cell(row, 10).Value = Math.Round(salary.OvertimeSalary, 0);
					worksheet.Cell(row, 11).Value = Math.Round(salary.Deduction, 0);
					worksheet.Cell(row, 12).Value = Math.Round(salary.TotalSalary, 0);

					for (int col = 9; col <= 12; col++)
						worksheet.Cell(row, col).Style.NumberFormat.Format = "#,##0";

					totalGrandSalary += salary.TotalSalary;
					row++;
				}

				var totalRow = row;
				worksheet.Cell(totalRow, 11).Value = "TỔNG CỘNG:";
				worksheet.Cell(totalRow, 11).Style.Font.Bold = true;
				worksheet.Cell(totalRow, 12).Value = Math.Round(totalGrandSalary, 0);
				worksheet.Cell(totalRow, 12).Style.Font.Bold = true;
				worksheet.Cell(totalRow, 12).Style.NumberFormat.Format = "#,##0";

				using (var ms = new MemoryStream())
				{
					workbook.SaveAs(ms);
					return ms.ToArray();
				}
			}
		}

		// ✅ CHI TIẾT LƯƠNG TỪNG NGÀY
		[HttpPost]
		public async Task<IActionResult> GetUserSalaryDetail([FromBody] UserSalaryDetailRequest request)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			try
			{
				var fromDate = DateOnly.FromDateTime(request.FromDate);
				var toDate = DateOnly.FromDateTime(request.ToDate);

				var user = await _context.Users
					.Include(u => u.Department)
					.FirstOrDefaultAsync(u => u.UserId == request.UserId);

				if (user == null)
					return Json(new { success = false, message = "Không tìm thấy nhân viên!" });

				var attendances = await _context.Attendances
					.Where(a => a.UserId == request.UserId &&
							   a.WorkDate >= fromDate &&
							   a.WorkDate <= toDate)
					.OrderBy(a => a.WorkDate)
					.ToListAsync();

				var baseSalary = GetSettingValueAsDecimal("BASE_SALARY", 5000000);
				var workDaysPerMonth = GetSettingValueAsInt("WORK_DAYS_PER_MONTH", 26);
				var standardHoursPerDay = GetSettingValueAsDecimal("STANDARD_HOURS_PER_DAY", 8);
				var overtimeRate = GetSettingValueAsDecimal("OVERTIME_RATE", 1.5m);
				var checkInStandardTime = GetSettingValueAsString("CHECK_IN_STANDARD_TIME", "08:00");

				// Tìm đoạn này trong GetUserSalaryDetail
				var dailyRate = Math.Round(baseSalary / workDaysPerMonth, 0);
				var hourlyRate = Math.Round(dailyRate / standardHoursPerDay, 0);

				TimeOnly.TryParse(checkInStandardTime, out var standardCheckInTime);

				// ✅ LẤY DYNAMIC SETTINGS
				var dynamicSettings = _context.SystemSettings
					.Where(s => s.Category == "Salary"
						&& s.IsActive == true
						&& s.IsEnabled == true
						&& !string.IsNullOrEmpty(s.ApplyMethod))
					.ToList();

				var allowances = new List<object>();
				var bonuses = new List<object>();
				var deductions = new List<object>();

				// ✅ TÍNH LƯƠNG CƠ BẢN THEO NGÀY CÔNG THỰC TẾ
				decimal baseSalaryByWorkedDays = 0;
				foreach (var att in attendances)
				{
					if (att.CheckInTime.HasValue || att.ApprovedOvertimeHours > 0)
					{
						var multiplier = att.SalaryMultiplier ?? 1.0m;
						baseSalaryByWorkedDays += dailyRate * multiplier;
					}
				}

				decimal totalAllowances = 0;
				decimal totalBonuses = 0;
				decimal totalDynamicDeductions = 0;

				// ✅ ÁP DỤNG DYNAMIC SETTINGS
				foreach (var setting in dynamicSettings)
				{
					if (!decimal.TryParse(setting.SettingValue, out var value))
						continue;

					var settingName = !string.IsNullOrEmpty(setting.Description) ? setting.Description : setting.SettingKey;

					switch (setting.ApplyMethod?.ToUpper())
					{
						case "FIXED_MONTHLY":
							allowances.Add(new { name = settingName, value = value });
							totalAllowances += value;
							break;

						case "PER_WORKDAY":
							var workedDays = attendances.Count(a => a.CheckInTime.HasValue);
							var perDayTotal = value * workedDays;
							allowances.Add(new { name = $"{settingName} ({value:N0} VNĐ × {workedDays} ngày)", value = perDayTotal });
							totalAllowances += perDayTotal;
							break;

						case "MULTIPLIER":
							if (setting.Unit == "%")
							{
								var bonusValue = (baseSalaryByWorkedDays * value / 100);
								bonuses.Add(new { name = $"{settingName} ({value}%)", value = bonusValue });
								totalBonuses += bonusValue;
							}
							break;

						case "PER_LATE_INSTANCE":
							var lateDays = attendances.Count(a => a.IsLate == true && a.HasLateRequest == false);
							if (lateDays > 0)
							{
								var lateTotal = value * lateDays;
								deductions.Add(new { name = $"{settingName} ({lateDays} lần)", value = lateTotal });
								totalDynamicDeductions += lateTotal;
							}
							break;

						case "CONDITIONAL":
							// Xử lý điều kiện đặc biệt
							if (setting.SettingKey == "BONUS_FULL_ATTENDANCE")
							{
								var totalDays = attendances.Count;
								var lateCount = attendances.Count(a => a.IsLate == true);
								if (totalDays >= workDaysPerMonth && lateCount == 0)
								{
									bonuses.Add(new { name = settingName, value = value, note = "✅ Đủ điều kiện" });
									totalBonuses += value;
								}
								else
								{
									bonuses.Add(new { name = settingName, value = 0m, note = $"❌ Không đủ điều kiện ({totalDays}/{workDaysPerMonth} ngày, {lateCount} lần trễ)" });
								}
							}
							break;
					}
				}

				var dailyDetails = new List<object>();
				var overtimeDays = new List<object>();
				decimal totalOvertimeSalary = 0;
				decimal totalAttendanceDeduction = 0;
				int totalLateDays = 0;
				decimal totalOvertimeHours = 0;
				decimal totalActualWorkHours = 0;
				int totalWorkedDays = 0;

				foreach (var att in attendances)
				{
					// ✅ KIỂM TRA NGÀY LÀM VIỆC
					if (!att.CheckInTime.HasValue)
						continue;

					totalWorkedDays++;

					// ✅ TÍNH GIỜ LÀM VIỆC (MỚI)
					decimal dayActualHours = 0;
					bool isAutoFilled = false;

					if (att.TotalHours.HasValue && att.TotalHours > 0)
					{
						dayActualHours = att.TotalHours.Value;
					}
					else
					{
						// ✅ Chưa có TotalHours → Ghi nhận 8 giờ chuẩn
						dayActualHours = standardHoursPerDay;
						isAutoFilled = !att.CheckOutTime.HasValue;
					}

					totalActualWorkHours += dayActualHours;

					// ✅ LƯƠNG NGÀY CƠ BẢN
					var multiplier = att.SalaryMultiplier ?? 1.0m;
					decimal daySalary = dailyRate * multiplier;

					// ✅ LƯƠNG TĂNG CA CHO NGÀY NÀY
					decimal dayOvertimeSalary = 0;
					decimal dayOvertimeHours = 0;
					if (att.IsOvertimeApproved && att.ApprovedOvertimeHours > 0)
					{
						dayOvertimeHours = att.ApprovedOvertimeHours;
						dayOvertimeSalary = dayOvertimeHours * hourlyRate * overtimeRate;
						totalOvertimeSalary += dayOvertimeSalary;
						totalOvertimeHours += dayOvertimeHours;

						overtimeDays.Add(new
						{
							date = att.WorkDate.ToString("dd/MM/yyyy"),
							hours = dayOvertimeHours,
							salary = dayOvertimeSalary
						});
					}

					decimal dayDeduction = att.DeductionAmount;
					totalAttendanceDeduction += dayDeduction;

					// ✅ TÍNH LƯƠNG NGÀY = Lương CB + Lương Tăng Ca - Khấu trừ
					decimal totalDaySalary = daySalary + dayOvertimeSalary - dayDeduction;

					int lateMinutes = 0;
					string lateDeductionReason = "";

					if (att.IsLate == true && att.CheckInTime.HasValue)
					{
						var actualCheckIn = TimeOnly.FromDateTime(att.CheckInTime.Value);
						if (actualCheckIn > standardCheckInTime)
						{
							var timeDiff = actualCheckIn - standardCheckInTime;
							lateMinutes = (int)timeDiff.TotalMinutes;
							totalLateDays++;
							lateDeductionReason = $"Đi muộn {FormatMinutesToHoursMinutes(lateMinutes)}";
						}
					}

					dailyDetails.Add(new
					{
						date = att.WorkDate.ToString("dd/MM/yyyy"),
						dayOfWeek = att.WorkDate.DayOfWeek.ToString(),
						checkInTime = att.CheckInTime?.ToString("HH:mm:ss") ?? "---",
						checkOutTime = att.CheckOutTime?.ToString("HH:mm:ss") ?? "---",
						hasCheckout = att.CheckOutTime.HasValue,
						actualWorkHours = dayActualHours,
						isAutoFilled = isAutoFilled,
						isLate = att.IsLate ?? false,
						lateMinutes = lateMinutes,
						lateDisplay = lateMinutes > 0 ? FormatMinutesToHoursMinutes(lateMinutes) : "",
						overtimeHours = dayOvertimeHours,
						salaryMultiplier = multiplier,
						baseSalary = daySalary,
						overtimeSalary = dayOvertimeSalary,
						deduction = dayDeduction,
						totalDaySalary = totalDaySalary,
						deductionReason = lateDeductionReason,
						hasLateRequest = att.HasLateRequest
					});
				}

				var totalDeduction = totalAttendanceDeduction + totalDynamicDeductions;

				// ✅ TỔNG LƯƠNG = Lương CB (theo ngày công) + Phụ cấp + Thưởng + Tăng Ca - Khấu Trừ
				var totalBaseSalaryFinal = baseSalaryByWorkedDays + totalAllowances + totalBonuses;
				var totalSalary = totalBaseSalaryFinal + totalOvertimeSalary - totalDeduction;

				var result = new
				{
					success = true,
					userInfo = new
					{
						userId = user.UserId,
						userName = user.Username,
						fullName = user.FullName,
						email = user.Email,
						departmentName = user.Department?.DepartmentName ?? "N/A",
						baseSalaryConfig = baseSalary
					},
					summary = new
					{
						fromDate = request.FromDate.ToString("dd/MM/yyyy"),
						toDate = request.ToDate.ToString("dd/MM/yyyy"),
						totalDays = attendances.Count,
						workedDays = totalWorkedDays,
						lateDays = totalLateDays,
						totalActualWorkHours = totalActualWorkHours,
						totalOvertimeHours = totalOvertimeHours,
						baseSalaryByWorkedDays = baseSalaryByWorkedDays,
						totalBaseSalary = totalBaseSalaryFinal,
						totalOvertimeSalary = totalOvertimeSalary,
						totalDeduction = totalDeduction,
						totalSalary = totalSalary,
						averageDailySalary = attendances.Count > 0 ? totalSalary / attendances.Count : 0
					},
					allowances = allowances,
					bonuses = bonuses,
					deductions = deductions,
					overtimeDays = overtimeDays,
					dailyDetails = dailyDetails
				};

				return Json(result);
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// HELPER METHODS
		// ============================================
		private string FormatMinutesToHoursMinutes(int totalMinutes)
		{
			if (totalMinutes == 0) return "0p";

			var hours = totalMinutes / 60;
			var minutes = totalMinutes % 60;

			if (hours > 0 && minutes > 0)
				return $"{hours}h{minutes}p";
			else if (hours > 0)
				return $"{hours}h";
			else
				return $"{minutes}p";
		}

		private string FormatHoursToDisplay(decimal totalHours)
		{
			if (totalHours == 0)
				return "0h";

			var hours = (int)totalHours;
			var minutes = (int)((totalHours - hours) * 60);

			if (minutes == 0)
				return $"{hours}h";

			return $"{hours}h{minutes}p";
		}

		private string GetSettingValueAsString(string key, string defaultValue)
		{
			var setting = _context.SystemSettings
				.FirstOrDefault(s => s.SettingKey == key
					&& s.IsActive == true
					&& s.IsEnabled == true);

			return setting?.SettingValue ?? defaultValue;
		}

		private decimal GetSettingValueAsDecimal(string key, decimal defaultValue)
		{
			var setting = _context.SystemSettings
				.FirstOrDefault(s => s.SettingKey == key
					&& s.IsActive == true
					&& s.IsEnabled == true);

			if (setting != null && decimal.TryParse(setting.SettingValue, out var value))
				return value;

			return defaultValue;
		}

		private int GetSettingValueAsInt(string key, int defaultValue)
		{
			var setting = _context.SystemSettings
				.FirstOrDefault(s => s.SettingKey == key
					&& s.IsActive == true
					&& s.IsEnabled == true);

			if (setting != null && int.TryParse(setting.SettingValue, out var value))
				return value;

			return defaultValue;
		}

		// ============================================
		// REQUEST MODELS
		// ============================================
		public class UserSalaryDetailRequest
		{
			public int UserId { get; set; }
			public DateTime FromDate { get; set; }
			public DateTime ToDate { get; set; }
		}

		public class SalaryFilterRequest
		{
			public string FromDate { get; set; }
			public string ToDate { get; set; }
			public int? UserId { get; set; }
			public int? DepartmentId { get; set; }
		}

		public class EmployeeSalaryDto
		{
			public int UserId { get; set; }
			public string FullName { get; set; }
			public string DepartmentName { get; set; }
			public int WorkedDays { get; set; }
			public int LateDays { get; set; }
			public decimal TotalHours { get; set; }
			public decimal OvertimeHours { get; set; }
			public decimal BaseSalary { get; set; }
			public decimal OvertimeSalary { get; set; }
			public decimal Deduction { get; set; }
			public decimal TotalSalary { get; set; }
		}
	}
}