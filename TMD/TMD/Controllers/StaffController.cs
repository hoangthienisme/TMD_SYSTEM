using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Models.ViewModels;
using TMDSystem.Helpers;
using BCrypt.Net;
using TMD.Models;
using System.Text.Json;

namespace TMDSystem.Controllers
{
	public class StaffController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;
		private readonly IWebHostEnvironment _env;
		private readonly HttpClient _httpClient;

		public StaffController(TmdContext context, AuditHelper auditHelper, IWebHostEnvironment env, IHttpClientFactory httpClientFactory)
		{
			_context = context;
			_auditHelper = auditHelper;
			_env = env;
			_httpClient = httpClientFactory.CreateClient();
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
		// REVERSE GEOCODING - LẤY ĐỊA CHỈ TỪ TỌA ĐỘ
		// ============================================
		private async Task<string> GetAddressFromCoordinates(decimal latitude, decimal longitude)
		{
			try
			{
				var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}&addressdetails=1";
				_httpClient.DefaultRequestHeaders.Clear();
				_httpClient.DefaultRequestHeaders.Add("User-Agent", "TMDSystem/1.0");

				var response = await _httpClient.GetStringAsync(url);
				var jsonDoc = JsonDocument.Parse(response);

				var address = jsonDoc.RootElement.GetProperty("display_name").GetString();
				return address ?? $"Lat: {latitude:F6}, Long: {longitude:F6}";
			}
			catch
			{
				return $"Lat: {latitude:F6}, Long: {longitude:F6}";
			}
		}

		// ============================================
		// STAFF DASHBOARD
		// ============================================
		public async Task<IActionResult> Dashboard()
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
		public async Task<IActionResult> Profile()
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
		public async Task<IActionResult> UpdateProfileJson([FromBody] UpdateProfileViewModel model)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn. Vui lòng đăng nhập lại." });
			}

			var userId = HttpContext.Session.GetInt32("UserId");

			var user = await _context.Users.FindAsync(userId);
			if (user == null)
			{
				// ✅ LOG: User not found
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
					// ✅ LOG: Email conflict
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

				// ✅ LOG: Profile update với chi tiết thay đổi
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
				// ✅ LOG: Exception
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

		// Helper method để detect changed fields
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
		public async Task<IActionResult> ChangePasswordJson([FromBody] ChangePasswordViewModel model)
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

				// ✅ LOG: Invalid model
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
				// ✅ LOG: User not found
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
				// ✅ LOG: Wrong current password
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
				// ✅ LOG: Same password attempt
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

				// ✅ LOG: Successful password change
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
				// ✅ LOG: Exception
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
		public async Task<IActionResult> MyLoginHistory()
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			// ✅ LOG: View login history
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
		public async Task<IActionResult> MyDepartment()
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
		public async Task<IActionResult> MyTasks()
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
		public async Task<IActionResult> UpdateTaskProgress([FromBody] UpdateTaskProgressRequest request)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
			}

			var userId = HttpContext.Session.GetInt32("UserId");

			var userTask = await _context.UserTasks
				.Include(ut => ut.Task)
				.FirstOrDefaultAsync(ut => ut.UserTaskId == request.UserTaskId && ut.UserId == userId);

			if (userTask == null)
			{
				// ✅ LOG: Task not found
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"UPDATE",
					"UserTask",
					"Không tìm thấy công việc",
					new { UserTaskId = request.UserTaskId }
				);

				return Json(new { success = false, message = "Không tìm thấy công việc" });
			}

			try
			{
				var oldData = new
				{
					userTask.CompletedThisWeek,
					userTask.ReportLink
				};

				userTask.CompletedThisWeek = request.CompletedThisWeek;
				userTask.ReportLink = request.ReportLink;
				userTask.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				var newData = new
				{
					userTask.CompletedThisWeek,
					userTask.ReportLink
				};

				// ✅ LOG: Task progress update với chi tiết
				await _auditHelper.LogDetailedAsync(
					userId,
					"UPDATE",
					"UserTask",
					userTask.UserTaskId,
					oldData,
					newData,
					$"Cập nhật tiến độ công việc: {userTask.Task.TaskName}",
					new Dictionary<string, object>
					{
						{ "TaskName", userTask.Task.TaskName },
						{ "OldProgress", oldData.CompletedThisWeek },
						{ "NewProgress", newData.CompletedThisWeek },
						{ "Target", userTask.Task.TargetPerWeek },
						{ "ProgressPercent", $"{(double)newData.CompletedThisWeek / userTask.Task.TargetPerWeek * 100:F1}%" },
						{ "UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = "Cập nhật tiến độ thành công!"
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"UPDATE",
					"UserTask",
					$"Exception: {ex.Message}",
					new { UserTaskId = request.UserTaskId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// CHECK-IN / CHECK-OUT với UPLOAD ẢNH
		// ============================================

		[HttpGet]
		public async Task<IActionResult> GetTodayAttendance()
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

		[HttpPost]
		[RequestSizeLimit(10_485_760)]
		public async Task<IActionResult> CheckIn([FromForm] CheckInRequest request)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
			}

			var userId = HttpContext.Session.GetInt32("UserId").Value;
			var serverNow = DateTime.Now;
			var today = DateOnly.FromDateTime(serverNow);

			var existingAttendance = await _context.Attendances
				.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == today);

			if (existingAttendance != null)
			{
				if (existingAttendance.CheckOutTime.HasValue)
				{
					return Json(new
					{
						success = false,
						message = "Bạn đã check-out hôm nay rồi! Chúc bạn một ngày vui vẻ! 😊",
						isCompleted = true
					});
				}
				else if (existingAttendance.CheckInTime.HasValue)
				{
					return Json(new { success = false, message = "Bạn đã check-in hôm nay rồi" });
				}
			}

			if (request.Photo == null || request.Photo.Length == 0)
			{
				return Json(new { success = false, message = "Vui lòng chụp ảnh hoặc tải lên ảnh để check-in" });
			}

			if (request.Photo.Length > 10 * 1024 * 1024)
			{
				return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 10MB" });
			}

			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
			var extension = Path.GetExtension(request.Photo.FileName).ToLower();
			if (!allowedExtensions.Contains(extension))
			{
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG" });
			}

			try
			{
				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "attendance");
				if (!Directory.Exists(uploadsFolder))
				{
					Directory.CreateDirectory(uploadsFolder);
				}

				var uniqueFileName = $"{userId}_{serverNow:yyyyMMdd_HHmmss}_checkin{extension}";
				var filePath = Path.Combine(uploadsFolder, uniqueFileName);

				using (var fileStream = new FileStream(filePath, FileMode.Create))
				{
					await request.Photo.CopyToAsync(fileStream);
				}

				var photoPath = $"/uploads/attendance/{uniqueFileName}";

				var checkInTime = new TimeOnly(serverNow.Hour, serverNow.Minute, serverNow.Second);
				var standardTime = new TimeOnly(8, 0, 0);
				var isLate = checkInTime > standardTime;

				var address = await GetAddressFromCoordinates(request.Latitude, request.Longitude);

				var attendance = new Attendance
				{
					UserId = userId,
					WorkDate = today,
					CheckInTime = serverNow,
					CheckInLatitude = request.Latitude,
					CheckInLongitude = request.Longitude,
					CheckInAddress = address,
					CheckInPhotos = photoPath,
					CheckInNotes = request.Notes,
					CheckInIpaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
					IsLate = isLate,
					IsWithinGeofence = true,
					CreatedAt = serverNow
				};

				_context.Attendances.Add(attendance);
				await _context.SaveChangesAsync();

				// ✅ LOG: Check-in với chi tiết
				await _auditHelper.LogDetailedAsync(
					userId,
					"CHECK_IN",
					"Attendance",
					attendance.AttendanceId,
					null,
					new
					{
						CheckInTime = serverNow,
						IsLate = isLate,
						PhotoPath = photoPath,
						Address = address,
						Latitude = request.Latitude,
						Longitude = request.Longitude
					},
					$"Check-in {(isLate ? "muộn" : "đúng giờ")} tại {address}",
					new Dictionary<string, object>
					{
						{ "CheckInTime", serverNow.ToString("HH:mm:ss") },
						{ "IsLate", isLate },
						{ "Location", address }
					}
				);

				var successMessage = $"✅ Check-in thành công!\n⏰ Thời gian: {serverNow:HH:mm:ss}\n📍 Vị trí: {address}";

				if (isLate)
				{
					successMessage += $"\n⚠️ Ghi nhận: Đến sau {standardTime:HH:mm} (chỉ để thống kê)";
				}

				return Json(new
				{
					success = true,
					message = successMessage,
					serverTime = serverNow.ToString("yyyy-MM-dd HH:mm:ss"),
					checkInTime = serverNow.ToString("HH:mm:ss"),
					address = address,
					isLate = isLate
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"CHECK_IN",
					"Attendance",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpPost]
		[RequestSizeLimit(10_485_760)]
		public async Task<IActionResult> CheckOut([FromForm] CheckOutRequest request)
		{
			if (!IsAuthenticated())
			{
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
			}

			var userId = HttpContext.Session.GetInt32("UserId").Value;
			var serverNow = DateTime.Now;
			var today = DateOnly.FromDateTime(serverNow);

			var attendance = await _context.Attendances
				.FirstOrDefaultAsync(a => a.UserId == userId && a.WorkDate == today);

			if (attendance == null || !attendance.CheckInTime.HasValue)
			{
				return Json(new { success = false, message = "Bạn chưa check-in hôm nay" });
			}

			if (attendance.CheckOutTime.HasValue)
			{
				return Json(new
				{
					success = false,
					message = "Bạn đã check-out hôm nay rồi! Chúc bạn một ngày vui vẻ! 😊",
					isCompleted = true
				});
			}

			if (request.Photo == null || request.Photo.Length == 0)
			{
				return Json(new { success = false, message = "Vui lòng chụp ảnh hoặc tải lên ảnh để check-out" });
			}

			if (request.Photo.Length > 10 * 1024 * 1024)
			{
				return Json(new { success = false, message = "Kích thước ảnh không được vượt quá 10MB" });
			}

			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
			var extension = Path.GetExtension(request.Photo.FileName).ToLower();
			if (!allowedExtensions.Contains(extension))
			{
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh định dạng JPG, JPEG, PNG" });
			}

			try
			{
				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "attendance");
				if (!Directory.Exists(uploadsFolder))
				{
					Directory.CreateDirectory(uploadsFolder);
				}

				var uniqueFileName = $"{userId}_{serverNow:yyyyMMdd_HHmmss}_checkout{extension}";
				var filePath = Path.Combine(uploadsFolder, uniqueFileName);

				using (var fileStream = new FileStream(filePath, FileMode.Create))
				{
					await request.Photo.CopyToAsync(fileStream);
				}

				var photoPath = $"/uploads/attendance/{uniqueFileName}";

				var address = await GetAddressFromCoordinates(request.Latitude, request.Longitude);

				attendance.CheckOutTime = serverNow;
				attendance.CheckOutLatitude = request.Latitude;
				attendance.CheckOutLongitude = request.Longitude;
				attendance.CheckOutAddress = address;
				attendance.CheckOutPhotos = photoPath;
				attendance.CheckOutNotes = request.Notes;
				attendance.CheckOutIpaddress = HttpContext.Connection.RemoteIpAddress?.ToString();

				if (attendance.CheckInTime.HasValue)
				{
					var duration = serverNow - attendance.CheckInTime.Value;
					attendance.TotalHours = (decimal)duration.TotalHours;
				}

				await _context.SaveChangesAsync();

				// ✅ LOG: Check-out với chi tiết
				await _auditHelper.LogDetailedAsync(
					userId,
					"CHECK_OUT",
					"Attendance",
					attendance.AttendanceId,
					null,
					new
					{
						CheckOutTime = serverNow,
						TotalHours = attendance.TotalHours,
						PhotoPath = photoPath,
						Address = address,
						Latitude = request.Latitude,
						Longitude = request.Longitude
					},
					$"Check-out tại {address} - Tổng giờ: {attendance.TotalHours:F2}h",
					new Dictionary<string, object>
					{
						{ "CheckOutTime", serverNow.ToString("HH:mm:ss") },
						{ "TotalHours", $"{attendance.TotalHours:F2}h" },
						{ "Location", address }
					}
				);

				return Json(new
				{
					success = true,
					message = $"✅ Check-out thành công!\n⏰ Thời gian: {serverNow:HH:mm:ss}\n⌚ Tổng giờ làm: {attendance.TotalHours:F2}h\n📍 Vị trí: {address}\n\n😊 Chúc bạn một buổi tối vui vẻ!",
					totalHours = attendance.TotalHours,
					serverTime = serverNow.ToString("yyyy-MM-dd HH:mm:ss"),
					checkOutTime = serverNow.ToString("HH:mm:ss"),
					address = address
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					userId,
					"CHECK_OUT",
					"Attendance",
					$"Exception: {ex.Message}",
					new { Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}
		// Thêm method này vào StaffController.cs

		[HttpGet]
		public async Task<IActionResult> GetMyTasksSummary()
		{
			if (!IsAuthenticated())
				return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

			var userId = HttpContext.Session.GetInt32("UserId");

			try
			{
				// ✅ LOG: View tasks summary
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
					.Select(ut => new
					{
						taskId = ut.TaskId,
						taskName = ut.Task.TaskName,
						description = ut.Task.Description ?? "",
						platform = ut.Task.Platform ?? "",
						targetPerWeek = ut.Task.TargetPerWeek ?? 0,
						completedThisWeek = ut.CompletedThisWeek ?? 0,
						reportLink = ut.ReportLink ?? "",
						deadline = ut.Task.Deadline.HasValue ? ut.Task.Deadline.Value.ToString("dd/MM/yyyy") : "",
						priority = ut.Task.Priority ?? "Medium",
						status = (ut.CompletedThisWeek ?? 0) >= (ut.Task.TargetPerWeek ?? 0) ? "Completed" : "InProgress",
						isOverdue = ut.Task.Deadline.HasValue && ut.Task.Deadline.Value < DateTime.Now
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
				// ✅ LOG: Exception
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
		public async Task<IActionResult> GetTaskDetail(int userTaskId)
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
					await _auditHelper.LogFailedAttemptAsync(
						userId,
						"VIEW",
						"UserTask",
						"Không tìm thấy công việc",
						new { UserTaskId = userTaskId }
					);

					return Json(new { success = false, message = "Không tìm thấy công việc" });
				}

				// ✅ LOG: View task detail
				await _auditHelper.LogViewAsync(
					userId.Value,
					"UserTask",
					userTaskId,
					$"Xem chi tiết công việc: {userTask.Task.TaskName}"
				);

				var task = userTask.Task;

				return Json(new
				{
					success = true,
					task = new
					{
						userTaskId = userTask.UserTaskId,
						taskId = task.TaskId,
						taskName = task.TaskName,
						description = task.Description ?? "",
						platform = task.Platform ?? "",
						targetPerWeek = task.TargetPerWeek ?? 0,
						completedThisWeek = userTask.CompletedThisWeek,
						reportLink = userTask.ReportLink ?? "",
						startDate = task.CreatedAt.HasValue ? task.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm") : "",
						deadline = task.Deadline.HasValue ? task.Deadline.Value.ToString("dd/MM/yyyy") : "",
						priority = task.Priority ?? "Medium",
						isOverdue = task.Deadline.HasValue && task.Deadline.Value < DateTime.Now,
						weekStartDate = userTask.WeekStartDate.HasValue ? userTask.WeekStartDate.Value.ToString("dd/MM/yyyy") : "",
						createdAt = userTask.CreatedAt.HasValue ? userTask.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm") : "",
						updatedAt = userTask.UpdatedAt.HasValue ? userTask.UpdatedAt.Value.ToString("dd/MM/yyyy HH:mm") : ""
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
		// LỊCH SỬ CHẤM CÔNG
		// ============================================

		[HttpGet]
		public async Task<IActionResult> AttendanceHistory(int page = 1, int pageSize = 20)
		{
			if (!IsAuthenticated())
				return RedirectToAction("Login", "Account");

			var userId = HttpContext.Session.GetInt32("UserId");

			// ✅ LOG: View attendance history
			await _auditHelper.LogViewAsync(
				userId.Value,
				"Attendance",
				userId.Value,
				$"Xem lịch sử chấm công - Trang {page}"
			);

			var query = _context.Attendances
				.Where(a => a.UserId == userId)
				.OrderByDescending(a => a.WorkDate);

			var totalRecords = await query.CountAsync();
			var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

			var attendances = await query
				.Skip((page - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			ViewBag.CurrentPage = page;
			ViewBag.TotalPages = totalPages;
			ViewBag.TotalRecords = totalRecords;

			return View(attendances);
		}

		/// <summary>
		/// Public API endpoint để lấy địa chỉ từ tọa độ (tránh CORS khi gọi từ JavaScript)
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> GetAddressFromCoordinatesApi(decimal latitude, decimal longitude)
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
		// REQUEST MODELS
		// ============================================

		public class UpdateTaskProgressRequest
		{
			public int UserTaskId { get; set; }
			public int CompletedThisWeek { get; set; }
			public string? ReportLink { get; set; }
		}

		public class CheckInRequest
		{
			public decimal Latitude { get; set; }
			public decimal Longitude { get; set; }
			public string? Address { get; set; }
			public string? Notes { get; set; }
			public IFormFile Photo { get; set; }
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