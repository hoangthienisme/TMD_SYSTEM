using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Models.ViewModels;
using TMDSystem.Helpers;
using BCrypt.Net;
using TMD.Models;
using Task = System.Threading.Tasks.Task;

namespace TMDSystem.Controllers
{
	public class AccountController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;

		public AccountController(TmdContext context, AuditHelper auditHelper)
		{
			_context = context;
			_auditHelper = auditHelper;
		}

		// GET: Login
		[HttpGet]
		public IActionResult Login()
		{
			if (HttpContext.Session.GetInt32("UserId") != null)
			{
				return RedirectToAction("Index", "Dashboard");
			}
			return View();
		}

		// POST: Login - JSON Response
		[HttpPost]
		public async Task<IActionResult> LoginJson([FromBody] LoginViewModel model)
		{
			if (!ModelState.IsValid)
			{
				var errors = string.Join(", ", ModelState.Values
					.SelectMany(v => v.Errors)
					.Select(e => e.ErrorMessage));

				// ✅ LOG: Invalid model state
				await _auditHelper.LogFailedAttemptAsync(
					null,
					"LOGIN",
					"User",
					$"Invalid model state: {errors}",
					new { Username = model.Username }
				);

				return Json(new { success = false, message = errors });
			}

			var user = await _context.Users
				.Include(u => u.Role)
				.Include(u => u.Department)
				.FirstOrDefaultAsync(u => u.Username == model.Username);

			// Log failed login - Username not found
			if (user == null)
			{
				await LogLoginHistory(null, model.Username, false, "Tên đăng nhập không tồn tại");

				// ✅ LOG: Username not found
				await _auditHelper.LogFailedAttemptAsync(
					null,
					"LOGIN",
					"User",
					"Tên đăng nhập không tồn tại",
					new
					{
						Username = model.Username,
						IP = HttpContext.Connection.RemoteIpAddress?.ToString(),
						UserAgent = Request.Headers["User-Agent"].ToString()
					}
				);

				return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng" });
			}

			// Log failed login - Wrong password
			if (!BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
			{
				await LogLoginHistory(user.UserId, model.Username, false, "Sai mật khẩu");

				// ✅ LOG: Wrong password
				await _auditHelper.LogFailedAttemptAsync(
					user.UserId,
					"LOGIN",
					"User",
					"Mật khẩu không đúng",
					new
					{
						Username = model.Username,
						IP = HttpContext.Connection.RemoteIpAddress?.ToString()
					}
				);

				return Json(new { success = false, message = "Tên đăng nhập hoặc mật khẩu không đúng" });
			}

			// Check if user is active
			if (user.IsActive != true)
			{
				await LogLoginHistory(user.UserId, model.Username, false, "Tài khoản đã bị khóa");

				// ✅ LOG: Account locked
				await _auditHelper.LogFailedAttemptAsync(
					user.UserId,
					"LOGIN",
					"User",
					"Tài khoản đã bị khóa",
					new { Username = model.Username }
				);

				return Json(new { success = false, message = "Tài khoản đã bị khóa. Vui lòng liên hệ Admin" });
			}

			// Success - Set session
			HttpContext.Session.SetInt32("UserId", user.UserId);
			HttpContext.Session.SetString("Username", user.Username);
			HttpContext.Session.SetString("FullName", user.FullName);
			HttpContext.Session.SetString("RoleName", user.Role.RoleName);
			HttpContext.Session.SetString("Avatar", user.Avatar ?? "/images/default-avatar.png");

			// Update last login
			user.LastLoginAt = DateTime.Now;
			await _context.SaveChangesAsync();

			// Log successful login
			await LogLoginHistory(user.UserId, user.Username, true, null);

			// ✅ LOG: Successful login với nhiều thông tin hơn
			await _auditHelper.LogDetailedAsync(
				user.UserId,
				"LOGIN",
				"User",
				user.UserId,
				null,
				null,
				$"Đăng nhập thành công - Role: {user.Role.RoleName}",
				new Dictionary<string, object>
				{
					{ "Browser", GetBrowserName(Request.Headers["User-Agent"].ToString()) },
					{ "Device", GetDeviceType(Request.Headers["User-Agent"].ToString()) },
					{ "IP", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" },
					{ "LoginTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
				}
			);

			// ✅ REDIRECT THEO ROLE
			string redirectUrl = user.Role.RoleName == "Admin"
				? "/Admin/Dashboard"
				: "/Staff/Dashboard";

			return Json(new { success = true, message = "Đăng nhập thành công!", redirectUrl = redirectUrl });
		}

		// GET: Register - ADMIN ONLY
		[HttpGet]
		public async Task<IActionResult> Register()
		{
			var roleName = HttpContext.Session.GetString("RoleName");
			if (roleName != "Admin")
			{
				TempData["Error"] = "Chỉ Admin mới có quyền tạo tài khoản mới!";
				return RedirectToAction("Index", "Dashboard");
			}

			ViewBag.Departments = await _context.Departments
				.Where(d => d.IsActive == true)
				.ToListAsync();

			ViewBag.Roles = await _context.Roles.ToListAsync();

			return View();
		}

		// POST: Register - ADMIN ONLY - JSON Response
		[HttpPost]
		public async Task<IActionResult> RegisterJson([FromBody] RegisterViewModel model)
		{
			var roleName = HttpContext.Session.GetString("RoleName");
			var adminId = HttpContext.Session.GetInt32("UserId");

			if (roleName != "Admin")
			{
				// ✅ LOG: Unauthorized attempt
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					"Không có quyền tạo tài khoản",
					new { AttemptedBy = HttpContext.Session.GetString("Username") }
				);

				return Json(new { success = false, message = "Chỉ Admin mới có quyền tạo tài khoản mới!" });
			}

			if (!ModelState.IsValid)
			{
				var errors = string.Join(", ", ModelState.Values
					.SelectMany(v => v.Errors)
					.Select(e => e.ErrorMessage));

				// ✅ LOG: Invalid data
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					$"Dữ liệu không hợp lệ: {errors}",
					new { Username = model.Username }
				);

				return Json(new { success = false, message = errors });
			}

			// Check username exists
			if (await _context.Users.AnyAsync(u => u.Username == model.Username))
			{
				// ✅ LOG: Duplicate username
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					"Tên đăng nhập đã tồn tại",
					new { Username = model.Username }
				);

				return Json(new { success = false, message = "Tên đăng nhập đã tồn tại" });
			}

			// Check email exists
			if (!string.IsNullOrEmpty(model.Email) &&
				await _context.Users.AnyAsync(u => u.Email == model.Email))
			{
				// ✅ LOG: Duplicate email
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					"Email đã được sử dụng",
					new { Email = model.Email }
				);

				return Json(new { success = false, message = "Email đã được sử dụng" });
			}

			// Lấy role từ form
			var selectedRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == model.RoleId);
			if (selectedRole == null)
			{
				// ✅ LOG: Invalid role
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					"Vai trò không hợp lệ",
					new { RoleId = model.RoleId }
				);

				return Json(new { success = false, message = "Vai trò không hợp lệ" });
			}

			try
			{
				// Create new user
				var user = new User
				{
					Username = model.Username,
					PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
					FullName = model.FullName,
					Email = model.Email,
					PhoneNumber = model.PhoneNumber,
					DepartmentId = model.DepartmentId,
					RoleId = selectedRole.RoleId,
					IsActive = true,
					CreatedAt = DateTime.Now,
					CreatedBy = HttpContext.Session.GetInt32("UserId")
				};

				_context.Users.Add(user);
				await _context.SaveChangesAsync();

				// ✅ LOG: Successful creation với đầy đủ thông tin
				await _auditHelper.LogDetailedAsync(
					adminId,
					"CREATE",
					"User",
					user.UserId,
					null,
					new
					{
						user.Username,
						user.FullName,
						user.Email,
						user.PhoneNumber,
						RoleName = selectedRole.RoleName,
						DepartmentId = user.DepartmentId
					},
					$"Admin tạo tài khoản mới: {user.Username} ({user.FullName}) với role {selectedRole.RoleName}",
					new Dictionary<string, object>
					{
						{ "CreatedBy", HttpContext.Session.GetString("FullName") ?? "Admin" },
						{ "CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = $"Tạo tài khoản thành công cho {user.FullName}!",
					redirectUrl = "/Admin/UserList"
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					adminId,
					"CREATE",
					"User",
					$"Exception: {ex.Message}",
					new { Username = model.Username, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// Logout
		public async Task<IActionResult> Logout()
		{
			var userId = HttpContext.Session.GetInt32("UserId");
			var username = HttpContext.Session.GetString("Username");
			var fullName = HttpContext.Session.GetString("FullName");

			if (userId != null)
			{
				// ✅ LOG: Logout với thông tin chi tiết
				await _auditHelper.LogDetailedAsync(
					userId,
					"LOGOUT",
					"User",
					userId,
					null,
					null,
					$"Đăng xuất - {fullName} ({username})",
					new Dictionary<string, object>
					{
						{ "LogoutTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
						{ "IP", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" }
					}
				);

				var lastLogin = await _context.LoginHistories
					.Where(l => l.UserId == userId && l.LogoutTime == null)
					.OrderByDescending(l => l.LoginTime)
					.FirstOrDefaultAsync();

				if (lastLogin != null)
				{
					lastLogin.LogoutTime = DateTime.Now;
					await _context.SaveChangesAsync();
				}
			}

			HttpContext.Session.Clear();
			return RedirectToAction("Login");
		}

		// ============ HELPER METHODS ============

		private async Task LogLoginHistory(int? userId, string username, bool isSuccess, string? failReason)
		{
			var loginHistory = new LoginHistory
			{
				UserId = userId,
				Username = username,
				LoginTime = DateTime.Now,
				Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
				UserAgent = Request.Headers["User-Agent"].ToString(),
				Browser = GetBrowserName(Request.Headers["User-Agent"].ToString()),
				Device = GetDeviceType(Request.Headers["User-Agent"].ToString()),
				IsSuccess = isSuccess,
				FailReason = failReason,
				CreatedAt = DateTime.Now
			};

			_context.LoginHistories.Add(loginHistory);
			await _context.SaveChangesAsync();
		}

		private string GetBrowserName(string userAgent)
		{
			if (userAgent.Contains("Chrome")) return "Chrome";
			if (userAgent.Contains("Firefox")) return "Firefox";
			if (userAgent.Contains("Safari")) return "Safari";
			if (userAgent.Contains("Edge")) return "Edge";
			return "Unknown";
		}

		private string GetDeviceType(string userAgent)
		{
			if (userAgent.Contains("Mobile")) return "Mobile";
			if (userAgent.Contains("Tablet")) return "Tablet";
			return "Desktop";
		}
	}
}