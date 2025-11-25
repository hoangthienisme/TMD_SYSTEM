using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Models.ViewModels;
using TMDSystem.Helpers;
using BCrypt.Net;
using TMD.Models;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using TMDSystem.Hubs;

namespace TMDSystem.Controllers
{
	public class StaffController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;
		private readonly IWebHostEnvironment _env;
		private readonly HttpClient _httpClient;
		private readonly IHubContext<NotificationHub> _hubContext;


		public StaffController(TmdContext context, AuditHelper auditHelper, IWebHostEnvironment env, IHttpClientFactory httpClientFactory, IHubContext<NotificationHub> hubContext)
		{
			_context = context;
			_auditHelper = auditHelper;
			_env = env;
			_httpClient = httpClientFactory.CreateClient();
			_hubContext = hubContext;

		}

		private bool IsAuthenticated()
		{
			return HttpContext.Session.GetInt32("UserId") != null;
		}

		private bool IsStaffOrAdmin()
		{
			var roleName = HttpContext.Session.GetString("RoleName");
			return roleName == "Staff" || roleName == "Admin";
		}

		// ============================================
		// REVERSE GEOCODING - LẤY ĐỊA CHỈ TỪ TỌA ĐỘ (IMPROVED WITH RETRY)
		// ============================================
		private async System.Threading.Tasks.Task<string> GetAddressFromCoordinates(decimal latitude, decimal longitude)
		{
			// Retry logic - 3 attempts with exponential backoff
			for (int attempt = 0; attempt < 3; attempt++)
			{
				try
				{
					var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}&addressdetails=1&accept-language=vi";

					_httpClient.DefaultRequestHeaders.Clear();
					_httpClient.DefaultRequestHeaders.Add("User-Agent", "TMDSystem/1.0 (Contact: admin@tmdystem.com)");
					_httpClient.Timeout = TimeSpan.FromSeconds(10);

					var response = await _httpClient.GetStringAsync(url);
					var jsonDoc = JsonDocument.Parse(response);

					if (jsonDoc.RootElement.TryGetProperty("display_name", out var displayName))
					{
						var address = displayName.GetString();
						if (!string.IsNullOrEmpty(address))
						{
							return address;
						}
					}

					// Fallback to coordinates
					return $"Lat: {latitude:F6}, Long: {longitude:F6}";
				}
				catch (TaskCanceledException)
				{
					if (attempt == 2) // Last attempt
					{
						return $"Lat: {latitude:F6}, Long: {longitude:F6}";
					}
					await System.Threading.Tasks.Task.Delay(1000 * (attempt + 1)); // Exponential backoff
				}
				catch
				{
					if (attempt == 2)
					{
						return $"Lat: {latitude:F6}, Long: {longitude:F6}";
					}
					await System.Threading.Tasks.Task.Delay(500);
				}
			}

			return $"Lat: {latitude:F6}, Long: {longitude:F6}";
		}

		// ============================================
		// STAFF DASHBOARD
		// ============================================
		public async System.Threading.Tasks.Task<IActionResult> Dashboard()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			if (!IsStaffOrAdmin())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			var user = await _context.Users
				.Include(u => u.Department)
				.Include(u => u.Role)
				.FirstOrDefaultAsync(u => u.UserId == userId);

			if (user == null)
				return RedirectToAction("Login", "Account");

			ViewBag.User = user;

			var myLoginHistory = await _context.LoginHistories
				.Where(l => l.UserId == userId && l.IsSuccess == true)
				.OrderByDescending(l => l.LoginTime)
				.Take(5)
				.ToListAsync();

			ViewBag.MyLoginHistory = myLoginHistory;

			var thisMonthLogins = await _context.LoginHistories
				.CountAsync(l => l.UserId == userId
					&& l.IsSuccess == true
					&& l.LoginTime.HasValue
					&& l.LoginTime.Value.Month == DateTime.Now.Month
					&& l.LoginTime.Value.Year == DateTime.Now.Year);

			ViewBag.ThisMonthLogins = thisMonthLogins;

			var lastLogin = await _context.LoginHistories
				.Where(l => l.UserId == userId && l.IsSuccess == true)
				.OrderByDescending(l => l.LoginTime)
				.Skip(1)
				.FirstOrDefaultAsync();

			ViewBag.LastLogin = lastLogin;

			if (user.DepartmentId.HasValue)
			{
				ViewBag.DepartmentUserCount = await _context.Users
					.CountAsync(u => u.DepartmentId == user.DepartmentId && u.IsActive == true);
			}

			var firstDayOfMonth = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
			var attendanceCount = await _context.Attendances
				.CountAsync(a => a.UserId == userId && a.WorkDate >= firstDayOfMonth);

			ViewBag.AttendanceThisMonth = attendanceCount;

			var totalHours = await _context.Attendances
				.Where(a => a.UserId == userId && a.WorkDate >= firstDayOfMonth)
				.SumAsync(a => a.TotalHours ?? 0);

			ViewBag.TotalHoursThisMonth = totalHours;

			return View();
		}

		// ============================================
		// PROFILE MANAGEMENT
		// ============================================

		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> Profile()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			var user = await _context.Users
				.Include(u => u.Role)
				.Include(u => u.Department)
				.FirstOrDefaultAsync(u => u.UserId == userId);

			if (user == null)
				return NotFound();

			ViewBag.User = user;
			return View();
		}

		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> UpdateProfileJson([FromBody] UpdateProfileViewModel model)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại." });
			}

			var userId = HttpContext.Session.GetInt32("UserId");

			var user = await _context.Users.FindAsync(userId);
			if (user == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"UPDATE",
					"User",
					"Không tìm thấy người dùng",
					new { UserId = userId }
				);

				return Json(new { success = false, message = "Không tìm thấy người dùng" });
			}

			if (!string.IsNullOrEmpty(model.Email))
			{
				var emailExists = await _context.Users
					.AnyAsync(u => u.Email == model.Email && u.UserId != userId);

				if (emailExists)
				{
					await _auditHelper.LogFailedAttemptAsync(
						userId,
						"UPDATE",
						"User",
						"Email đã được sử dụng",
						new { Email = model.Email }
					);

					return Json(new { success = false, message = "Email đã được sử dụng bởi người dùng khác" });
				}
			}

			try
			{
				var oldData = new
				{
					user.FullName,
					user.Email,
					user.PhoneNumber
				};

				user.FullName = model.FullName;
				user.Email = model.Email;
				user.PhoneNumber = model.PhoneNumber;
				user.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				HttpContext.Session.SetString("FullName", user.FullName);

				var newData = new
				{
					user.FullName,
					user.Email,
					user.PhoneNumber
				};

				await _auditHelper.LogDetailedAsync(
					userId,
					"UPDATE",
					"User",
					user.UserId,
					oldData,
					newData,
					"Cập nhật thông tin cá nhân",
					new Dictionary<string, object>
					{
						{ "ChangedFields", GetChangedFields(oldData, newData) },
						{ "UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = "Cập nhật thông tin thành công!"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"UPDATE",
					"User",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new
				{
					success = false,
					message = $"Có lỗi xảy ra: {ex.Message}"
				});
			}
		}

		private string GetChangedFields(object oldData, object newData)
		{
			var changes = new List<string>();
			var oldProps = oldData.GetType().GetProperties();
			var newProps = newData.GetType().GetProperties();

			foreach (var oldProp in oldProps)
			{
				var newProp = newProps.FirstOrDefault(p => p.Name == oldProp.Name);
				if (newProp != null)
				{
					var oldVal = oldProp.GetValue(oldData)?.ToString() ?? "";
					var newVal = newProp.GetValue(newData)?.ToString() ?? "";

					if (oldVal != newVal)
					{
						changes.Add($"{oldProp.Name}: '{oldVal}' → '{newVal}'");
					}
				}
			}

			return changes.Count > 0 ? string.Join(", ", changes) : "No changes";
		}

		// ============================================
		// CHANGE PASSWORD
		// ============================================

		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> ChangePasswordJson([FromBody] ChangePasswordViewModel model)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại." });
			}

			var userId = HttpContext.Session.GetInt32("UserId");

			if (!ModelState.IsValid)
			{
				var errors = ModelState.Values
					.SelectMany(v => v.Errors)
					.Select(e => e.ErrorMessage)
					.FirstOrDefault();

				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					$"Dữ liệu không hợp lệ: {errors}",
					null
				);

				return Json(new { success = false, message = errors ?? "Dữ liệu không hợp lệ" });
			}

			var user = await _context.Users
				.FirstOrDefaultAsync(u => u.UserId == userId);

			if (user == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					"Không tìm thấy người dùng",
					null
				);

				return Json(new { success = false, message = "Không tìm thấy người dùng" });
			}

			if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					"Mật khẩu hiện tại không đúng",
					new
					{
						Username = user.Username,
						IP = HttpContext.Connection.RemoteIpAddress?.ToString()
					}
				);

				return Json(new { success = false, message = "Mật khẩu hiện tại không đúng" });
			}

			if (BCrypt.Net.BCrypt.Verify(model.NewPassword, user.PasswordHash))
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					"Mật khẩu mới trùng với mật khẩu cũ",
					new { Username = user.Username }
				);

				return Json(new { success = false, message = "Mật khẩu mới phải khác mật khẩu hiện tại" });
			}

			try
			{
				var oldPasswordHash = user.PasswordHash;

				user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
				user.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();

				var resetHistory = new PasswordResetHistory
				{
					UserId = user.UserId,
					ResetByUserId = userId,
					OldPasswordHash = oldPasswordHash,
					ResetTime = DateTime.Now,
					ResetReason = "Đổi mật khẩu thông qua trang Profile",
					Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString()
				};

				_context.PasswordResetHistories.Add(resetHistory);
				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					user.UserId,
					new { Action = "Change Password", OldPasswordHash = "***HIDDEN***" },
					new { Action = "Password Changed Successfully" },
					"Đổi mật khẩu thành công qua Profile",
					new Dictionary<string, object>
					{
						{ "Method", "Self-Service" },
						{ "IP", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" },
						{ "ChangedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				HttpContext.Session.Clear();

				return Json(new
				{
					success = true,
					message = "Đổi mật khẩu thành công! Vui lòng đăng nhập lại với mật khẩu mới."
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"PASSWORD_CHANGE",
					"User",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new
				{
					success = false,
					message = $"Có lỗi xảy ra: {ex.Message}"
				});
			}
		}

		// ============================================
		// MY LOGIN HISTORY
		// ============================================

		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> MyLoginHistory()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			await _auditHelper.LogViewAsync(
				userId.Value,
				"LoginHistory",
				userId.Value,
				"Xem lịch sử đăng nhập cá nhân"
			);

			var history = await _context.LoginHistories
				.Where(l => l.UserId == userId)
				.OrderByDescending(l => l.LoginTime)
				.Take(50)
				.ToListAsync();

			return View(history);
		}

		// ============================================
		// MY DEPARTMENT INFO
		// ============================================

		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> MyDepartment()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			var user = await _context.Users
				.Include(u => u.Department)
				.FirstOrDefaultAsync(u => u.UserId == userId);

			if (user == null || !user.DepartmentId.HasValue)
			{
				TempData["Error"] = "Bạn chưa được phân công vào phòng ban nào";
				return RedirectToAction("Dashboard");
			}

			var department = await _context.Departments
				.Include(d => d.Users)
					.ThenInclude(u => u.Role)
				.FirstOrDefaultAsync(d => d.DepartmentId == user.DepartmentId);

			ViewBag.MyDepartment = department;
			ViewBag.CurrentUser = user;

			return View();
		}

		// ============================================
		// MY TASKS
		// ============================================

		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> MyTasks()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			var myTasks = await _context.UserTasks
				.Include(ut => ut.Task)
				.Where(ut => ut.UserId == userId)
				.OrderBy(ut => ut.Task.TaskName)
				.ToListAsync();

			return View(myTasks);
		}
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> UpdateTaskProgress([FromBody] UpdateTaskProgressRequest request)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId");

			var userTask = await _context.UserTasks
				.Include(ut => ut.Task)
				.Include(ut => ut.User)
				.FirstOrDefaultAsync(ut => ut.UserTaskId == request.UserTaskId && ut.UserId == userId);

			if (userTask == null)
				return Json(new { success = false, message = "Không tìm thấy công việc" });

			if (!new[] { "TODO", "InProgress", "Completed" }.Contains(request.Status))
				return Json(new { success = false, message = "Trạng thái không hợp lệ" });

			try
			{
				var oldData = new { userTask.Status, userTask.ReportLink };

				userTask.Status = request.Status;
				userTask.ReportLink = request.ReportLink;
				userTask.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				var newData = new { userTask.Status, userTask.ReportLink };

				await _auditHelper.LogDetailedAsync(
					userId,
					"UPDATE",
					"UserTask",
					userTask.UserTaskId,
					oldData,
					newData,
					$"Cập nhật trạng thái công việc: {userTask.Task.TaskName}",
					new Dictionary<string, object>
					{
				{ "TaskName", userTask.Task.TaskName },
				{ "OldStatus", oldData.Status ?? "NULL" },
				{ "NewStatus", newData.Status }
					}
				);

				// GỬI THÔNG BÁO CHO ADMIN KHI HOÀN THÀNH
				if (request.Status == "Completed")
				{
					await _hubContext.Clients.Group("Admins").SendAsync(
						"ReceiveMessage",
						"Task hoàn thành",
						$"{userTask.User.FullName} đã hoàn thành: {userTask.Task.TaskName}",
						"success",
						$"/Admin/TaskList"
					);
				}

				return Json(new { success = true, message = "Cập nhật trạng thái thành công!" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// CHECK-IN / CHECK-OUT với UPLOAD ẢNH
		// ============================================

		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetTodayAttendance()
		{
			if (!IsAuthenticated())
			{
				return Json(new { hasCheckedIn = false });
			}

			var userId = HttpContext.Session.GetInt32("UserId");
			var today = DateOnly.FromDateTime(DateTime.Now);

			var attendance = await _context.Attendances
				.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == today);

			if (attendance == null || !attendance.CheckInTime.HasValue)
			{
				return Json(new { hasCheckedIn = false });
			}

			return Json(new
			{
				hasCheckedIn = true,
				checkInTime = attendance.CheckInTime.Value.ToString("HH:mm:ss"),
				hasCheckedOut = attendance.CheckOutTime.HasValue,
				checkOutTime = attendance.CheckOutTime?.ToString("HH:mm:ss"),
				checkInPhotos = attendance.CheckInPhotos,
				checkOutPhotos = attendance.CheckOutPhotos,
				checkInLatitude = attendance.CheckInLatitude,
				checkInLongitude = attendance.CheckInLongitude,
				checkOutLatitude = attendance.CheckOutLatitude,
				checkOutLongitude = attendance.CheckOutLongitude,
				checkInAddress = attendance.CheckInAddress,
				checkOutAddress = attendance.CheckOutAddress
			});
		}

		// ============================================
		// IMPROVED CHECK-IN - Prevent Multiple Check-ins
		// ============================================
		[HttpPost]
		[RequestSizeLimit(10_485_760)]
		public async System.Threading.Tasks.Task<IActionResult> CheckIn([FromForm] CheckInRequest request)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;
			var serverNow = DateTime.Now;
			var today = DateOnly.FromDateTime(serverNow);

			var existingAttendance = await _context.Attendances
				.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == today);

			if (existingAttendance != null && existingAttendance.CheckInTime.HasValue)
			{
				var checkInTimeStr = existingAttendance.CheckInTime.Value.ToString("HH:mm:ss");
				return Json(new
				{
					success = false,
					message = $"Bạn đã check-in hôm nay rồi!\nThời gian check-in: {checkInTimeStr}",
					alreadyCheckedIn = true,
					checkInTime = checkInTimeStr
				});
			}

			if (request.Photo == null || request.Photo.Length == 0)
				return Json(new { success = false, message = "Vui lòng chụp ảnh hoặc tải lên ảnh để check-in" });

			if (request.Photo.Length > 10 * 1024 * 1024)
				return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 10MB" });

			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
			var extension = Path.GetExtension(request.Photo.FileName).ToLower();
			if (!allowedExtensions.Contains(extension))
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG" });

			if (request.Latitude == 0 || request.Longitude == 0)
				return Json(new { success = false, message = "Không thể lấy vị trí GPS. Vui lòng bật GPS và thử lại" });

			try
			{
				var configs = await _context.SalaryConfigurations
					.Where(c => c.IsActive == true)
					.ToDictionaryAsync(c => c.ConfigCode, c => c.Value);

				var standardStartTime = TimeOnly.Parse(configs.GetValueOrDefault("CHECK_IN_STANDARD_TIME", "08:00"));

				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "attendance");
				if (!Directory.Exists(uploadsFolder))
					Directory.CreateDirectory(uploadsFolder);

				var uniqueFileName = $"{userId}_{serverNow:yyyyMMdd_HHmmss}_checkin{extension}";
				var filePath = Path.Combine(uploadsFolder, uniqueFileName);

				using (var fileStream = new FileStream(filePath, FileMode.Create))
					await request.Photo.CopyToAsync(fileStream);

				var photoPath = $"/uploads/attendance/{uniqueFileName}";
				var checkInTime = new TimeOnly(serverNow.Hour, serverNow.Minute, serverNow.Second);
				var isLate = checkInTime > standardStartTime;
				var address = await GetAddressFromCoordinates(request.Latitude, request.Longitude);

				var attendance = existingAttendance ?? new Attendance
				{
					UserId = userId,
					WorkDate = today,
					CreatedAt = serverNow
				};

				attendance.CheckInTime = serverNow;
				attendance.CheckInLatitude = request.Latitude;
				attendance.CheckInLongitude = request.Longitude;
				attendance.CheckInAddress = address;
				attendance.CheckInPhotos = photoPath;
				attendance.CheckInNotes = request.Notes;
				attendance.CheckInIpaddress = HttpContext.Connection.RemoteIpAddress?.ToString();
				attendance.IsLate = isLate;
				attendance.IsWithinGeofence = true;
				attendance.TotalHours = 0;

				if (existingAttendance == null)
					_context.Attendances.Add(attendance);

				await _context.SaveChangesAsync();

				// GỬI THÔNG BÁO CHO ADMIN NẾU ĐI TRỄ
				if (isLate)
				{
					var user = await _context.Users.FindAsync(userId);
					await _hubContext.Clients.Group("Admins").SendAsync(
						"ReceiveMessage",
						"Nhân viên đi trễ",
						$"{user?.FullName ?? "Nhân viên"} vừa check-in muộn lúc {serverNow:HH:mm:ss}",
						"warning",
						$"/Admin/AttendanceHistory?userId={userId}&fromDate={serverNow:yyyy-MM-dd}&toDate={serverNow:yyyy-MM-dd}"
					);
				}

				return Json(new
				{
					success = true,
					message = $"Check-in thành công!\nThời gian: {serverNow:HH:mm:ss}\nVị trí: {address}" +
							  (isLate ? $"\nGhi nhận: Đến sau {standardStartTime:HH:mm}" : "\nĐúng giờ!"),
					serverTime = serverNow.ToString("yyyy-MM-dd HH:mm:ss"),
					checkInTime = serverNow.ToString("HH:mm:ss"),
					address = address,
					isLate = isLate
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}


		// ============================================
		// IMPROVED CHECK-OUT - Calculate Exact Working Hours
		// ============================================
		[HttpPost]
		[RequestSizeLimit(10_485_760)]
		public async System.Threading.Tasks.Task<IActionResult> CheckOut([FromForm] CheckOutRequest request)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;
			var serverNow = DateTime.Now;
			var today = DateOnly.FromDateTime(serverNow);

			var attendance = await _context.Attendances
				.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == today);

			if (attendance == null || !attendance.CheckInTime.HasValue)
				return Json(new { success = false, message = "Bạn chưa check-in hôm nay" });

			if (attendance.CheckOutTime.HasValue)
				return Json(new { success = false, message = "Bạn đã check-out hôm nay rồi! Chúc bạn một ngày vui vẻ! 😊", isCompleted = true });

			if (request.Photo == null || request.Photo.Length == 0)
				return Json(new { success = false, message = "Vui lòng chụp ảnh hoặc tải lên ảnh để check-out" });

			if (request.Photo.Length > 10 * 1024 * 1024)
				return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 10MB" });

			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
			var extension = Path.GetExtension(request.Photo.FileName).ToLower();
			if (!allowedExtensions.Contains(extension))
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG" });

			// ✅ Validate GPS
			if (request.Latitude == 0 || request.Longitude == 0)
			{
				return Json(new
				{
					success = false,
					message = "⚠️ Không thể lấy vị trí GPS. Vui lòng đợi GPS ổn định và thử lại."
				});
			}

			if (Math.Abs(request.Latitude) > 90 || Math.Abs(request.Longitude) > 180)
			{
				return Json(new
				{
					success = false,
					message = "⚠️ Tọa độ GPS không hợp lệ. Vui lòng thử lại."
				});
			}

			try
			{
				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "attendance");
				if (!Directory.Exists(uploadsFolder))
					Directory.CreateDirectory(uploadsFolder);

				var uniqueFileName = $"{userId}_{serverNow:yyyyMMdd_HHmmss}_checkout{extension}";
				var filePath = Path.Combine(uploadsFolder, uniqueFileName);

				using (var fileStream = new FileStream(filePath, FileMode.Create))
					await request.Photo.CopyToAsync(fileStream);

				var photoPath = $"/uploads/attendance/{uniqueFileName}";
				var address = await GetAddressFromCoordinates(request.Latitude, request.Longitude);

				// ✅ CALCULATE EXACT WORKING HOURS
				var duration = serverNow - attendance.CheckInTime.Value;
				var totalHours = (decimal)duration.TotalHours;
				var hours = duration.Hours;
				var minutes = duration.Minutes;
				var seconds = duration.Seconds;

				attendance.CheckOutTime = serverNow;
				attendance.CheckOutLatitude = request.Latitude;
				attendance.CheckOutLongitude = request.Longitude;
				attendance.CheckOutAddress = address;
				attendance.CheckOutPhotos = photoPath;
				attendance.CheckOutNotes = request.Notes;
				attendance.CheckOutIpaddress = HttpContext.Connection.RemoteIpAddress?.ToString();
				attendance.TotalHours = totalHours;
				attendance.ActualWorkHours = totalHours;

				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					userId, "CHECK_OUT", "Attendance", attendance.AttendanceId,
					null, new { CheckOutTime = serverNow.ToString("HH:mm:ss"), TotalHours = $"{hours:D2}:{minutes:D2}:{seconds:D2}", Address = address },
					$"Check-out tại {address} - Tổng giờ: {hours:D2}:{minutes:D2}:{seconds:D2}",
					new Dictionary<string, object> { { "CheckOutTime", serverNow.ToString("HH:mm:ss") }, { "TotalHours", $"{hours:D2}:{minutes:D2}:{seconds:D2}" } }
				);

				return Json(new
				{
					success = true,
					message = $"✅ Check-out thành công!\n⏰ Thời gian: {serverNow:HH:mm:ss}\n⌚ Tổng giờ làm: {hours:D2}:{minutes:D2}:{seconds:D2}\n📍 Vị trí: {address}\n\n😊 Chúc bạn một buổi tối vui vẻ!",
					totalHours = totalHours,
					totalHoursFormatted = $"{hours:D2}:{minutes:D2}:{seconds:D2}",
					serverTime = serverNow.ToString("yyyy-MM-dd HH:mm:ss"),
					checkOutTime = serverNow.ToString("HH:mm:ss"),
					address = address
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(userId, "CHECK_OUT", "Attendance", $"Exception: {ex.Message}", new { Error = ex.ToString() });
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// TASKS SUMMARY FOR DASHBOARD
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetMyTasksSummary()
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId");

			try
			{
				await _auditHelper.LogViewAsync(
					userId.Value,
					"UserTask",
					userId.Value,
					"Xem tóm tắt công việc trên Dashboard"
				);

				var myTasks = await _context.UserTasks
					.Include(ut => ut.Task)
					.Where(ut => ut.UserId == userId && ut.Task.IsActive == true)
					.ToListAsync();

				var tasksSummary = myTasks
					.OrderByDescending(ut => ut.Task.Priority == "High" ? 1 : ut.Task.Priority == "Medium" ? 2 : 3)
					.ThenBy(ut => ut.Task.Deadline)
					.Select(ut =>
					{
						var task = ut.Task;

						// ✅ XÁC ĐỊNH TRẠNG THÁI
						string status;
						if (ut.CompletedThisWeek == null || ut.CompletedThisWeek == 0)
							status = "TODO";
						else if (ut.CompletedThisWeek < task.TargetPerWeek)
							status = "InProgress";
						else
							status = "Completed";

						// ✅ LOGIC KIỂM TRA QUÁ HẠN - FIXED
						var isOverdue = false;
						var isCompletedLate = false;

						if (task.Deadline.HasValue)
						{
							if (status == "Completed")
							{
								// Đã hoàn thành - kiểm tra hoàn thành trước hay sau deadline
								if (ut.UpdatedAt.HasValue && ut.UpdatedAt.Value > task.Deadline.Value)
								{
									// Hoàn thành SAU deadline = HOÀN THÀNH MUỘN
									isCompletedLate = true;
									isOverdue = true; // Vẫn đánh dấu overdue để thống kê
								}
								// Nếu hoàn thành TRƯỚC deadline = OK, không overdue
							}
							else
							{
								// Chưa hoàn thành và đã quá deadline = ĐANG QUÁ HẠN
								if (DateTime.Now > task.Deadline.Value)
								{
									isOverdue = true;
								}
							}
						}

						return new
						{
							taskId = task.TaskId,
							taskName = task.TaskName,
							description = task.Description ?? "",
							platform = task.Platform ?? "",
							targetPerWeek = task.TargetPerWeek ?? 0,
							completedThisWeek = ut.CompletedThisWeek ?? 0,
							reportLink = ut.ReportLink ?? "",
							deadline = task.Deadline.HasValue ? task.Deadline.Value.ToString("dd/MM/yyyy") : "",
							priority = task.Priority ?? "Medium",
							status = status,
							isOverdue = isOverdue,
							isCompletedLate = isCompletedLate,
							updatedAt = ut.UpdatedAt.HasValue ? ut.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""
						};
					})
					.ToList();

				return Json(new
				{
					success = true,
					tasks = tasksSummary,
					totalTasks = tasksSummary.Count,
					completedTasks = tasksSummary.Count(t => t.status == "Completed"),
					inProgressTasks = tasksSummary.Count(t => t.status == "InProgress"),
					overdueTasks = tasksSummary.Count(t => t.isOverdue)
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"VIEW",
					"UserTask",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}


		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetTaskDetail(int userTaskId)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId");

			try
			{
				var userTask = await _context.UserTasks
					.Include(ut => ut.Task)
					.FirstOrDefaultAsync(ut => ut.UserTaskId == userTaskId && ut.UserId == userId);

				if (userTask == null)
				{
					return Json(new { success = false, message = "Không tìm thấy công việc" });
				}

				await _auditHelper.LogViewAsync(
					userId.Value,
					"UserTask",
					userTaskId,
					$"Xem chi tiết: {userTask.Task.TaskName}"
				);

				var task = userTask.Task;

				// ✅ XÁC ĐỊNH TRẠNG THÁI HIỂN THỊ
				string statusText = userTask.Status switch
				{
					"TODO" => "Chưa bắt đầu",
					"InProgress" => "Đang làm",
					"Completed" => "Hoàn thành",
					_ => "Chưa bắt đầu"
				};

				string statusClass = userTask.Status switch
				{
					"TODO" => "secondary",
					"InProgress" => "warning",
					"Completed" => "success",
					_ => "secondary"
				};

				string statusIcon = userTask.Status switch
				{
					"TODO" => "inbox",
					"InProgress" => "spinner fa-spin",
					"Completed" => "check-circle",
					_ => "inbox"
				};

				// ✅ KIỂM TRA QUÁ HẠN
				bool isOverdue = false;
				bool isCompletedLate = false;

				if (task.Deadline.HasValue)
				{
					if (userTask.Status == "Completed")
					{
						// Hoàn thành muộn?
						if (userTask.UpdatedAt.HasValue && userTask.UpdatedAt.Value > task.Deadline.Value)
						{
							isCompletedLate = true;
							statusText = "Hoàn thành muộn";
							statusClass = "warning";
						}
					}
					else
					{
						// Đang quá hạn?
						if (DateTime.Now > task.Deadline.Value)
						{
							isOverdue = true;
						}
					}
				}

				return Json(new
				{
					success = true,
					task = new
					{
						userTaskId = userTask.UserTaskId,
						taskId = task.TaskId,
						taskName = task.TaskName,
						description = task.Description ?? "Không có mô tả",
						platform = task.Platform ?? "N/A",
						reportLink = userTask.ReportLink ?? "",
						deadline = task.Deadline.HasValue ? task.Deadline.Value.ToString("dd/MM/yyyy HH:mm") : "Không có deadline",
						priority = task.Priority ?? "Medium",
						status = userTask.Status,
						statusText = statusText,
						statusClass = statusClass,
						statusIcon = statusIcon,
						isOverdue = isOverdue,
						isCompletedLate = isCompletedLate,
						createdAt = userTask.CreatedAt.HasValue ? userTask.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm") : "",
						updatedAt = userTask.UpdatedAt.HasValue ? userTask.UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm") : "Chưa cập nhật"
					}
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"VIEW",
					"UserTask",
					$"Exception: {ex.Message}",
					new { UserTaskId = userTaskId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// ATTENDANCE HISTORY
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> AttendanceHistory()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			if (!IsStaffOrAdmin())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			await _auditHelper.LogViewAsync(
				userId.Value,
				"Attendance",
				userId.Value,
				"Xem lịch sử chấm công cá nhân (Staff)"
			);

			// ✅ FIX: Include đầy đủ User và Department như Admin version
			var attendances = await _context.Attendances
				.Include(a => a.User)
					.ThenInclude(u => u.Department)
				.Where(a => a.UserId == userId)
				.OrderByDescending(a => a.WorkDate)
				.ThenByDescending(a => a.CheckInTime)
				.ToListAsync();

			// ✅ TÍNH TOÁN STATISTICS GIỐNG ADMIN
			ViewBag.TotalRecords = attendances.Count;
			ViewBag.TotalCheckIns = attendances.Count(a => a.CheckInTime != null);
			ViewBag.TotalCheckOuts = attendances.Count(a => a.CheckOutTime != null);
			ViewBag.CompletedDays = attendances.Count(a => a.CheckInTime != null && a.CheckOutTime != null);
			ViewBag.OnTimeCount = attendances.Count(a => a.IsLate == false);
			ViewBag.LateCount = attendances.Count(a => a.IsLate == true);
			ViewBag.TotalWorkHours = attendances.Sum(a => a.TotalHours ?? 0);
			ViewBag.OutsideGeofence = attendances.Count(a => a.IsWithinGeofence == false);

			return View(attendances);
		}


		// ============================================
		// GET ADDRESS FROM COORDINATES API
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetAddressFromCoordinatesApi(decimal latitude, decimal longitude)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
			}

			try
			{
				var address = await GetAddressFromCoordinates(latitude, longitude);
				return Json(new
				{
					success = true,
					address = address
				});
			}
			catch (Exception ex)
			{
				return Json(new
				{
					success = false,
					address = $"Lat: {latitude:F6}, Long: {longitude:F6}",
					error = ex.Message
				});
			}
		}

		// ============================================
		// ✅ IMPROVED OVERTIME REQUEST - CHỈ CHO PHÉP XIN TRONG 3 NGÀY GẦN NHẤT
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> CreateOvertimeRequest([FromBody] JsonElement payload)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			string workDateStr = payload.TryGetProperty("WorkDate", out var wdProp) ? wdProp.GetString() ?? "" : "";
			string reason = payload.TryGetProperty("Reason", out var rProp) ? rProp.GetString() ?? "" : "";
			string taskDesc = payload.TryGetProperty("TaskDescription", out var tdProp) ? tdProp.GetString() ?? "" : "";

			if (string.IsNullOrWhiteSpace(workDateStr) || string.IsNullOrWhiteSpace(reason))
				return Json(new { success = false, message = "Vui lòng chọn ngày và nhập lý do" });

			try
			{
				if (!DateOnly.TryParse(workDateStr, out var workDateDo))
				{
					if (!DateTime.TryParse(workDateStr, out var workDt))
						return Json(new { success = false, message = "Ngày tăng ca không hợp lệ" });
					workDateDo = DateOnly.FromDateTime(workDt);
				}

				var today = DateOnly.FromDateTime(DateTime.Now);
				var threeDaysAgo = today.AddDays(-3);

				if (workDateDo < threeDaysAgo)
				{
					return Json(new
					{
						success = false,
						message = $"Chỉ được xin tăng ca trong vòng 3 ngày gần nhất\nTừ {threeDaysAgo:dd/MM/yyyy} đến {today:dd/MM/yyyy}"
					});
				}

				if (workDateDo > today)
					return Json(new { success = false, message = "Không thể xin tăng ca cho ngày trong tương lai" });

				var attendance = await _context.Attendances
					.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == workDateDo);

				if (attendance == null || !attendance.CheckInTime.HasValue)
					return Json(new { success = false, message = "Không tìm thấy bản ghi chấm công cho ngày này" });

				if (attendance.HasOvertimeRequest == true)
				{
					var existingOT = await _context.OvertimeRequests
						.FirstOrDefaultAsync(ot => ot.UserId == userId && ot.WorkDate == workDateDo);

					if (existingOT != null)
					{
						return Json(new
						{
							success = false,
							message = $"Đã có yêu cầu tăng ca cho ngày này\nTrạng thái: {existingOT.Status}"
						});
					}
				}

				var configs = await _context.SalaryConfigurations
					.Where(c => c.IsActive == true)
					.ToDictionaryAsync(c => c.ConfigCode, c => c.Value);

				var standardEndTime = TimeOnly.Parse(configs.GetValueOrDefault("CHECK_OUT_STANDARD_TIME", "17:00"));
				var standardHoursPerDay = decimal.Parse(configs.GetValueOrDefault("STANDARD_HOURS_PER_DAY", "8"));

				DateTime effectiveCheckOutTime;
				decimal overtimeHours = 0;

				if (attendance.CheckOutTime.HasValue)
				{
					effectiveCheckOutTime = attendance.CheckOutTime.Value;
					var checkOutTime = TimeOnly.FromDateTime(effectiveCheckOutTime);

					if (checkOutTime <= standardEndTime)
					{
						return Json(new
						{
							success = false,
							message = $"Không có giờ tăng ca\nCheckout: {checkOutTime:HH:mm}\nGiờ chuẩn: {standardEndTime:HH:mm}"
						});
					}

					var overtimeSpan = checkOutTime - standardEndTime;
					overtimeHours = (decimal)overtimeSpan.TotalHours;
				}
				else
				{
					var serverNow = DateTime.Now;
					var currentTime = new TimeOnly(serverNow.Hour, serverNow.Minute, serverNow.Second);

					if (workDateDo == today && currentTime < standardEndTime)
					{
						return Json(new
						{
							success = false,
							message = $"Chưa đến giờ checkout chuẩn ({standardEndTime:HH:mm})"
						});
					}

					if (workDateDo == today)
					{
						if (currentTime > standardEndTime)
						{
							var overtimeSpan = currentTime - standardEndTime;
							overtimeHours = (decimal)overtimeSpan.TotalHours;
							effectiveCheckOutTime = serverNow;
						}
						else
						{
							return Json(new { success = false, message = "Chưa có giờ tăng ca" });
						}
					}
					else
					{
						return Json(new
						{
							success = false,
							message = $"Ngày {workDateDo:dd/MM/yyyy} chưa checkout\nKhông thể xác định giờ tăng ca"
						});
					}

					if (attendance.TotalHours == null || attendance.TotalHours == 0)
					{
						attendance.TotalHours = standardHoursPerDay;
						attendance.ActualWorkHours = standardHoursPerDay;
						await _context.SaveChangesAsync();
					}
				}

				var ot = new OvertimeRequest
				{
					UserId = userId,
					WorkDate = workDateDo,
					ActualCheckOutTime = effectiveCheckOutTime,
					OvertimeHours = overtimeHours,
					Reason = reason,
					TaskDescription = taskDesc,
					Status = "Pending",
					ExpiryDate = DateTime.Now.AddDays(7),
					CreatedAt = DateTime.Now
				};

				_context.OvertimeRequests.Add(ot);
				await _context.SaveChangesAsync();

				attendance.HasOvertimeRequest = true;
				attendance.OvertimeRequestId = ot.OvertimeRequestId;
				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					userId, "CREATE", "OvertimeRequest", ot.OvertimeRequestId, null, ot,
					$"Gửi yêu cầu tăng ca {workDateDo:dd/MM/yyyy} - {overtimeHours:F2}h",
					new Dictionary<string, object> {
				{ "OvertimeHours", $"{overtimeHours:F2}h" },
				{ "HasCheckout", attendance.CheckOutTime.HasValue }
					}
				);

				// GỬI THÔNG BÁO CHO ADMIN
				await _hubContext.Clients.Group("Admins").SendAsync(
					"ReceiveMessage",
					"Yêu cầu tăng ca mới",
					$"Nhân viên vừa gửi yêu cầu tăng ca {overtimeHours:F2}h cho ngày {workDateDo:dd/MM/yyyy}",
					"info",
					"/Admin/PendingRequests?type=Overtime&status=Pending"
				);

				return Json(new
				{
					success = true,
					message = $"Gửi yêu cầu tăng ca thành công!\n\nNgày: {workDateDo:dd/MM/yyyy}\nGiờ tăng ca: {overtimeHours:F2}h\nTrạng thái: Chờ duyệt"
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		public async System.Threading.Tasks.Task AutoFillMissingCheckouts()
		{
			try
			{
				var yesterday = DateOnly.FromDateTime(DateTime.Now.AddDays(-1));

				// Lấy tất cả attendance có check-in nhưng chưa checkout
				var missingCheckouts = await _context.Attendances
					.Where(a => a.WorkDate < yesterday
						&& a.CheckInTime.HasValue
						&& !a.CheckOutTime.HasValue
						&& (a.TotalHours == null || a.TotalHours == 0))
					.ToListAsync();

				if (!missingCheckouts.Any())
					return;

				var configs = await _context.SalaryConfigurations
					.Where(c => c.IsActive == true)
					.ToDictionaryAsync(c => c.ConfigCode, c => c.Value);

				var standardHoursPerDay = decimal.Parse(configs.GetValueOrDefault("STANDARD_HOURS_PER_DAY", "8"));

				foreach (var att in missingCheckouts)
				{
					// ✅ GHI NHẬN ĐỦ 8 GIỜ CHUẨN
					att.TotalHours = standardHoursPerDay;
					att.ActualWorkHours = standardHoursPerDay;
					att.UpdatedAt = DateTime.Now;
				}

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					null,
					"SYSTEM_AUTO",
					"Attendance",
					null,
					null,
					new { Count = missingCheckouts.Count, StandardHours = standardHoursPerDay },
					$"Tự động ghi nhận {standardHoursPerDay}h cho {missingCheckouts.Count} bản ghi chưa checkout"
				);
			}
			catch (Exception ex)
			{
				// Log error
				Console.WriteLine($"AutoFillMissingCheckouts Error: {ex.Message}");
			}
		}
		// ============================================
		// LEAVE REQUEST
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> CreateLeaveRequest([FromBody] CreateLeaveRequestViewModel model)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			if (string.IsNullOrEmpty(model.LeaveType) || model.StartDate == default || model.EndDate == default || string.IsNullOrEmpty(model.Reason))
				return Json(new { success = false, message = "Vui lòng điền đầy đủ thông tin" });

			if (model.EndDate < model.StartDate)
				return Json(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

			try
			{
				var totalDays = (model.EndDate - model.StartDate).Days + 1;

				var leave = new LeaveRequest
				{
					UserId = userId,
					LeaveType = model.LeaveType,
					StartDate = DateOnly.FromDateTime(model.StartDate),
					EndDate = DateOnly.FromDateTime(model.EndDate),
					TotalDays = totalDays,
					Reason = model.Reason,
					ProofDocument = model.ProofDocument,
					Status = "Pending",
					CreatedAt = DateTime.Now
				};

				_context.LeaveRequests.Add(leave);
				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					userId, "CREATE", "LeaveRequest", leave.LeaveRequestId, null, leave,
					$"Gửi yêu cầu nghỉ phép từ {leave.StartDate:dd/MM/yyyy} đến {leave.EndDate:dd/MM/yyyy}",
					new Dictionary<string, object> { { "TotalDays", totalDays }, { "LeaveType", model.LeaveType } }
				);

				// GỬI THÔNG BÁO CHO ADMIN
				await _hubContext.Clients.Group("Admins").SendAsync(
					"ReceiveMessage",
					"Yêu cầu nghỉ phép mới",
					$"Nhân viên vừa gửi yêu cầu nghỉ phép {totalDays} ngày ({model.LeaveType})",
					"info",
					"/Admin/PendingRequests?type=Leave&status=Pending"
				);

				return Json(new { success = true, message = $"Gửi yêu cầu nghỉ phép thành công!\nSố ngày: {totalDays}" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// LATE REQUEST
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> CreateLateRequest([FromBody] CreateLateRequestViewModel model)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			if (!ModelState.IsValid)
			{
				var errors = string.Join(", ", ModelState.Values
					.SelectMany(v => v.Errors)
					.Select(e => e.ErrorMessage));
				return Json(new { success = false, message = errors });
			}

			try
			{
				var late = new LateRequest
				{
					UserId = userId,
					RequestDate = DateOnly.FromDateTime(model.RequestDate),
					ExpectedArrivalTime = TimeOnly.FromTimeSpan(model.ExpectedArrivalTime),
					Reason = model.Reason,
					ProofDocument = model.ProofDocument,
					Status = "Pending",
					CreatedAt = DateTime.Now
				};

				_context.LateRequests.Add(late);
				await _context.SaveChangesAsync();

				var attendance = await _context.Attendances
					.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == DateOnly.FromDateTime(model.RequestDate));

				if (attendance != null)
				{
					attendance.HasLateRequest = true;
					attendance.LateRequestId = late.LateRequestId;
					await _context.SaveChangesAsync();
				}

				await _auditHelper.LogDetailedAsync(
					userId, "CREATE", "LateRequest", late.LateRequestId,
					null, late,
					$"Gửi yêu cầu đi trễ ngày {late.RequestDate:dd/MM/yyyy}"
				);

				// GỬI THÔNG BÁO CHO ADMIN
				await _hubContext.Clients.Group("Admins").SendAsync(
					"ReceiveMessage",
					"Yêu cầu đi trễ mới",
					$"Nhân viên vừa gửi yêu cầu đi trễ cho ngày {late.RequestDate:dd/MM/yyyy}",
					"info",
					"/Admin/PendingRequests?type=Late&status=Pending"
				);

				return Json(new { success = true, message = "Gửi yêu cầu đi trễ thành công!" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// GET MY REQUESTS
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetMyRequests(string? type, string? status, DateTime? from, DateTime? to, string? keyword)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			try
			{
				var otQuery = _context.OvertimeRequests.Where(r => r.UserId == userId).AsQueryable();
				var leaveQuery = _context.LeaveRequests.Where(r => r.UserId == userId).AsQueryable();
				var lateQuery = _context.LateRequests.Where(r => r.UserId == userId).AsQueryable();

				if (!string.IsNullOrEmpty(status))
				{
					otQuery = otQuery.Where(r => r.Status == status);
					leaveQuery = leaveQuery.Where(r => r.Status == status);
					lateQuery = lateQuery.Where(r => r.Status == status);
				}

				if (from.HasValue)
				{
					var fromDate = DateOnly.FromDateTime(from.Value.Date);
					otQuery = otQuery.Where(r => r.WorkDate >= fromDate);
					leaveQuery = leaveQuery.Where(r => r.StartDate >= fromDate || r.EndDate >= fromDate);
					lateQuery = lateQuery.Where(r => r.RequestDate >= fromDate);
				}
				if (to.HasValue)
				{
					var toDate = DateOnly.FromDateTime(to.Value.Date);
					otQuery = otQuery.Where(r => r.WorkDate <= toDate);
					leaveQuery = leaveQuery.Where(r => r.StartDate <= toDate || r.EndDate <= toDate);
					lateQuery = lateQuery.Where(r => r.RequestDate <= toDate);
				}

				if (!string.IsNullOrWhiteSpace(keyword))
				{
					var kw = keyword.Trim().ToLower();
					otQuery = otQuery.Where(r => (r.Reason ?? "").ToLower().Contains(kw) || (r.TaskDescription ?? "").ToLower().Contains(kw));
					leaveQuery = leaveQuery.Where(r => (r.Reason ?? "").ToLower().Contains(kw) || (r.ProofDocument ?? "").ToLower().Contains(kw));
					lateQuery = lateQuery.Where(r => (r.Reason ?? "").ToLower().Contains(kw) || (r.ProofDocument ?? "").ToLower().Contains(kw));
				}

				List<object> ot = new List<object>();
				List<object> leave = new List<object>();
				List<object> late = new List<object>();

				if (string.IsNullOrEmpty(type) || type == "Overtime")
				{
					ot = await otQuery
						.OrderByDescending(r => r.CreatedAt)
						.Select(r => new
						{
							r.OvertimeRequestId,
							r.UserId,
							workDate = r.WorkDate.ToString("yyyy-MM-dd"),
							actualCheckOutTime = r.ActualCheckOutTime == default(DateTime) ? "" : r.ActualCheckOutTime.ToString(),
							overtimeHours = r.OvertimeHours,
							reason = r.Reason ?? "",
							taskDescription = r.TaskDescription ?? "",
							status = r.Status ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""
						})
						.ToListAsync<object>();
				}

				if (string.IsNullOrEmpty(type) || type == "Leave")
				{
					leave = await leaveQuery
						.OrderByDescending(r => r.CreatedAt)
						.Select(r => new
						{
							r.LeaveRequestId,
							r.UserId,
							leaveType = r.LeaveType ?? "",
							startDate = r.StartDate.ToString("yyyy-MM-dd"),
							endDate = r.EndDate.ToString("yyyy-MM-dd"),
							totalDays = r.TotalDays,
							reason = r.Reason ?? "",
							proofDocument = r.ProofDocument ?? "",
							status = r.Status ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""
						})
						.ToListAsync<object>();
				}

				if (string.IsNullOrEmpty(type) || type == "Late")
				{
					late = await lateQuery
						.OrderByDescending(r => r.CreatedAt)
						.Select(r => new
						{
							r.LateRequestId,
							r.UserId,
							requestDate = r.RequestDate.ToString("yyyy-MM-dd"),
							expectedArrivalTime = r.ExpectedArrivalTime.ToString("HH:mm"),
							reason = r.Reason ?? "",
							proofDocument = r.ProofDocument ?? "",
							status = r.Status ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""
						})
						.ToListAsync<object>();
				}

				return Json(new { success = true, overtime = ot, leave = leave, late = late });
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(userId, "VIEW", "Request", $"Exception: {ex.Message}", new { Error = ex.ToString() });
				return Json(new { success = false, message = $"Có lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// GET REQUEST DETAIL
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetRequestDetail(string type, int id)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			try
			{
				if (type == "Overtime")
				{
					var r = await _context.OvertimeRequests.Include(x => x.User)
						.FirstOrDefaultAsync(x => x.OvertimeRequestId == id && x.UserId == userId);

					if (r == null)
						return Json(new { success = false, message = "Không tìm thấy request" });

					string checkOutTimeStr = "N/A";
					if (r.ActualCheckOutTime != default(DateTime))
					{
						checkOutTimeStr = r.ActualCheckOutTime.ToString("HH:mm:ss");
					}

					return Json(new
					{
						success = true,
						request = new
						{
							overtimeRequestId = r.OvertimeRequestId,
							userId = r.UserId,
							workDate = r.WorkDate.ToString("dd/MM/yyyy"),
							actualCheckOutTime = checkOutTimeStr,
							overtimeHours = $"{r.OvertimeHours:F2}h",
							reason = r.Reason ?? "",
							taskDescription = r.TaskDescription ?? "",
							status = r.Status ?? "",
							reviewedBy = r.ReviewedBy,
							reviewedAt = r.ReviewedAt.HasValue ? r.ReviewedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							reviewNote = r.ReviewNote ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							updatedAt = r.UpdatedAt.HasValue ? r.UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							userName = r.User?.FullName ?? "",
							userEmail = r.User?.Email ?? ""
						}
					});
				}

				if (type == "Leave")
				{
					var r = await _context.LeaveRequests.Include(x => x.User)
						.FirstOrDefaultAsync(x => x.LeaveRequestId == id && x.UserId == userId);

					if (r == null)
						return Json(new { success = false, message = "Không tìm thấy request" });

					return Json(new
					{
						success = true,
						request = new
						{
							leaveRequestId = r.LeaveRequestId,
							userId = r.UserId,
							leaveType = r.LeaveType ?? "",
							startDate = r.StartDate.ToString("dd/MM/yyyy"),
							endDate = r.EndDate.ToString("dd/MM/yyyy"),
							totalDays = $"{r.TotalDays} ngày",
							reason = r.Reason ?? "",
							proofDocument = r.ProofDocument ?? "",
							status = r.Status ?? "",
							reviewedBy = r.ReviewedBy,
							reviewedAt = r.ReviewedAt.HasValue ? r.ReviewedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							reviewNote = r.ReviewNote ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							updatedAt = r.UpdatedAt.HasValue ? r.UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							userName = r.User?.FullName ?? "",
							userEmail = r.User?.Email ?? ""
						}
					});
				}

				if (type == "Late")
				{
					var r = await _context.LateRequests.Include(x => x.User)
						.FirstOrDefaultAsync(x => x.LateRequestId == id && x.UserId == userId);

					if (r == null)
						return Json(new { success = false, message = "Không tìm thấy request" });

					return Json(new
					{
						success = true,
						request = new
						{
							lateRequestId = r.LateRequestId,
							userId = r.UserId,
							requestDate = r.RequestDate.ToString("dd/MM/yyyy"),
							expectedArrivalTime = r.ExpectedArrivalTime.ToString("HH:mm:ss"),
							reason = r.Reason ?? "",
							proofDocument = r.ProofDocument ?? "",
							status = r.Status ?? "",
							reviewedBy = r.ReviewedBy,
							reviewedAt = r.ReviewedAt.HasValue ? r.ReviewedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							reviewNote = r.ReviewNote ?? "",
							createdAt = r.CreatedAt.HasValue ? r.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
							updatedAt = r.UpdatedAt.HasValue ? r.UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm:ss") : "",
							userName = r.User?.FullName ?? "",
							userEmail = r.User?.Email ?? ""
						}
					});
				}

				return Json(new { success = false, message = "Loại request không hợp lệ" });
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(userId, "VIEW", "Request", $"Exception: {ex.Message}", new { Error = ex.ToString() });
				return Json(new { success = false, message = $"Có lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// CANCEL REQUEST
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> CancelRequest([FromBody] ReviewRequestViewModel model)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			try
			{
				if (model.RequestType == "Overtime")
				{
					var r = await _context.OvertimeRequests.FirstOrDefaultAsync(x => x.OvertimeRequestId == model.RequestId && x.UserId == userId);
					if (r == null) return Json(new { success = false, message = "Không tìm thấy request" });
					if (r.Status != "Pending") return Json(new { success = false, message = "Chỉ có thể hủy request đang ở trạng thái Pending" });

					r.Status = "Cancelled";
					r.UpdatedAt = DateTime.Now;
					await _context.SaveChangesAsync();

					await _auditHelper.LogAsync(userId, "UPDATE", "OvertimeRequest", r.OvertimeRequestId, null, new { Status = "Cancelled" }, "User cancelled overtime request");
					return Json(new { success = true, message = "Đã hủy request" });
				}

				if (model.RequestType == "Leave")
				{
					var r = await _context.LeaveRequests.FirstOrDefaultAsync(x => x.LeaveRequestId == model.RequestId && x.UserId == userId);
					if (r == null) return Json(new { success = false, message = "Không tìm thấy request" });
					if (r.Status != "Pending") return Json(new { success = false, message = "Chỉ có thể hủy request đang ở trạng thái Pending" });

					r.Status = "Cancelled";
					r.UpdatedAt = DateTime.Now;
					await _context.SaveChangesAsync();

					await _auditHelper.LogAsync(userId, "UPDATE", "LeaveRequest", r.LeaveRequestId, null, new { Status = "Cancelled" }, "User cancelled leave request");
					return Json(new { success = true, message = "Đã hủy request" });
				}

				if (model.RequestType == "Late")
				{
					var r = await _context.LateRequests.FirstOrDefaultAsync(x => x.LateRequestId == model.RequestId && x.UserId == userId);
					if (r == null) return Json(new { success = false, message = "Không tìm thấy request" });
					if (r.Status != "Pending") return Json(new { success = false, message = "Chỉ có thể hủy request đang ở trạng thái Pending" });

					r.Status = "Cancelled";
					r.UpdatedAt = DateTime.Now;
					await _context.SaveChangesAsync();

					await _auditHelper.LogAsync(userId, "UPDATE", "LateRequest", r.LateRequestId, null, new { Status = "Cancelled" }, "User cancelled late request");
					return Json(new { success = true, message = "Đã hủy request" });
				}

				return Json(new { success = false, message = "Loại request không hợp lệ" });
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(userId, "UPDATE", "Request", $"Exception: {ex.Message}", new { Error = ex.ToString() });
				return Json(new { success = false, message = $"Có lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// MY REQUESTS PAGE
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> MyRequests()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");
			if (userId == null)
				return RedirectToAction("Login", "Account");

			await _auditHelper.LogViewAsync(userId.Value, "Request", userId.Value, "Xem trang MyRequests");

			var overtime = await _context.OvertimeRequests.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync();
			var leave = await _context.LeaveRequests.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync();
			var late = await _context.LateRequests.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync();

			var model = new
			{
				Overtime = overtime,
				Leave = leave,
				Late = late
			};

			return View(model);
		}
		// ============================================
		// THÊM METHOD NÀY VÀO StaffController.cs
		// ============================================

		[HttpPost]
		[RequestSizeLimit(5_242_880)] // 5MB limit
		public async Task<IActionResult> UploadAvatar(IFormFile avatar)
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId").Value;

			// Validate file
			if (avatar == null || avatar.Length == 0)
				return Json(new { success = false, message = "Vui lòng chọn ảnh để tải lên" });

			// Validate file type
			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
			var extension = Path.GetExtension(avatar.FileName).ToLower();
			if (!allowedExtensions.Contains(extension))
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG" });

			// Validate file size (max 5MB)
			if (avatar.Length > 5 * 1024 * 1024)
				return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 5MB" });

			try
			{
				var user = await _context.Users.FindAsync(userId);
				if (user == null)
					return Json(new { success = false, message = "Không tìm thấy người dùng" });

				// Create uploads folder if not exists
				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "avatars");
				if (!Directory.Exists(uploadsFolder))
					Directory.CreateDirectory(uploadsFolder);

				// Delete old avatar if exists
				if (!string.IsNullOrEmpty(user.Avatar) && user.Avatar != "/images/default-avatar.png")
				{
					var oldAvatarPath = Path.Combine(_env.WebRootPath, user.Avatar.TrimStart('/'));
					if (System.IO.File.Exists(oldAvatarPath))
					{
						try
						{
							System.IO.File.Delete(oldAvatarPath);
						}
						catch (Exception ex)
						{
							// Log but continue
							Console.WriteLine($"Failed to delete old avatar: {ex.Message}");
						}
					}
				}

				// Generate unique filename
				var uniqueFileName = $"{userId}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
				var filePath = Path.Combine(uploadsFolder, uniqueFileName);

				// Save file
				using (var fileStream = new FileStream(filePath, FileMode.Create))
				{
					await avatar.CopyToAsync(fileStream);
				}

				// Update user avatar path
				var avatarUrl = $"/uploads/avatars/{uniqueFileName}";
				var oldAvatar = user.Avatar;
				user.Avatar = avatarUrl;
				user.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				// Update session
				HttpContext.Session.SetString("Avatar", avatarUrl);

				// Log audit
				await _auditHelper.LogDetailedAsync(
					userId,
					"UPDATE",
					"User",
					userId,
					new { Avatar = oldAvatar },
					new { Avatar = avatarUrl },
					"Cập nhật ảnh đại diện",
					new Dictionary<string, object>
					{
				{ "FileName", uniqueFileName },
				{ "FileSize", $"{avatar.Length / 1024.0:F2} KB" },
				{ "Extension", extension }
					}
				);

				return Json(new
				{
					success = true,
					message = "Cập nhật ảnh đại diện thành công!",
					avatarUrl = avatarUrl
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"UPLOAD_AVATAR",
					"User",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new
				{
					success = false,
					message = $"Có lỗi xảy ra: {ex.Message}"
				});
			}
		}
		// ============================================
		// REQUEST MODELS
		// ============================================


		public class CheckInRequest
		{
			public decimal Latitude { get; set; }
			public decimal Longitude { get; set; }
			public string? Address { get; set; }
			public string? Notes { get; set; }
			public IFormFile Photo { get; set; }
		}
		public class UpdateTaskProgressRequest
		{
			public int UserTaskId { get; set; }
			public string Status { get; set; } = "TODO"; // ✅ THAY ĐỔI Ở ĐÂY
			public string? ReportLink { get; set; }
		}
		public class CheckOutRequest
		{
			public decimal Latitude { get; set; }
			public decimal Longitude { get; set; }
			public string? Address { get; set; }
			public string? Notes { get; set; }
			public IFormFile Photo { get; set; }
		}
	}
}