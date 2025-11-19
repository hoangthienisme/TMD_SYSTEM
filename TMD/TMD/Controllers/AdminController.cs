using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Helpers;
using TMD.Models;
using Microsoft.AspNetCore.Identity.Data;

namespace TMDSystem.Controllers
{
	public class AdminController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;

		public AdminController(TmdContext context, AuditHelper auditHelper)
		{
			_context = context;
			_auditHelper = auditHelper;
		}

		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("RoleName") == "Admin";
		}

		// ============================================
		// ADMIN DASHBOARD
		// ============================================
		public async Task<IActionResult> Dashboard()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			ViewBag.TotalUsers = await _context.Users.CountAsync();
			ViewBag.ActiveUsers = await _context.Users.CountAsync(u => u.IsActive == true);
			ViewBag.TotalDepartments = await _context.Departments.CountAsync();

			var allTasks = await _context.Tasks
				.Include(t => t.UserTasks)
				.Where(t => t.IsActive == true)
				.ToListAsync();

			ViewBag.TotalTasks = allTasks.Count;
			ViewBag.CompletedTasks = allTasks.Count(t => t.UserTasks.Any() &&
				t.UserTasks.All(ut => ut.CompletedThisWeek >= t.TargetPerWeek));
			ViewBag.InProgressTasks = allTasks.Count - ViewBag.CompletedTasks;
			ViewBag.OverdueTasks = allTasks.Count(t => t.Deadline.HasValue && t.Deadline.Value < DateTime.Now);

			var taskCompletionRate = ViewBag.TotalTasks > 0
				? Math.Round((double)ViewBag.CompletedTasks / ViewBag.TotalTasks * 100, 1)
				: 0;
			ViewBag.TaskCompletionRate = taskCompletionRate;

			var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
			var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

			var monthlyAttendances = await _context.Attendances
				.Include(a => a.User)
				.Where(a => a.WorkDate >= DateOnly.FromDateTime(startOfMonth) &&
							a.WorkDate <= DateOnly.FromDateTime(endOfMonth))
				.ToListAsync();

			ViewBag.TotalAttendances = monthlyAttendances.Count;
			ViewBag.OnTimeCount = monthlyAttendances.Count(a => a.IsLate == false);
			ViewBag.LateCount = monthlyAttendances.Count(a => a.IsLate == true);
			ViewBag.OnTimeRate = monthlyAttendances.Count > 0
				? Math.Round((double)ViewBag.OnTimeCount / monthlyAttendances.Count * 100, 1)
				: 0;

			var topPerformers = await _context.Users
				.Include(u => u.Department)
				.Include(u => u.UserTasks)
					.ThenInclude(ut => ut.Task)
				.Where(u => u.IsActive == true && u.UserTasks.Any())
				.Select(u => new
				{
					User = u,
					TotalCompleted = u.UserTasks.Sum(ut => ut.CompletedThisWeek),
					TaskCount = u.UserTasks.Count(ut => ut.Task.IsActive == true)
				})
				.OrderByDescending(x => x.TotalCompleted)
				.Take(5)
				.ToListAsync();

			ViewBag.TopPerformers = topPerformers;

			var lateComers = await _context.Attendances
				.Include(a => a.User)
					.ThenInclude(u => u.Department)
				.Where(a => a.IsLate == true &&
							a.WorkDate >= DateOnly.FromDateTime(startOfMonth) &&
							a.WorkDate <= DateOnly.FromDateTime(endOfMonth))
				.GroupBy(a => a.UserId)
				.Select(g => new
				{
					UserId = g.Key,
					User = g.First().User,
					LateCount = g.Count()
				})
				.OrderByDescending(x => x.LateCount)
				.Take(5)
				.ToListAsync();

			ViewBag.LateComers = lateComers;

			var punctualStaff = await _context.Attendances
				.Include(a => a.User)
					.ThenInclude(u => u.Department)
				.Where(a => a.IsLate == false &&
							a.WorkDate >= DateOnly.FromDateTime(startOfMonth) &&
							a.WorkDate <= DateOnly.FromDateTime(endOfMonth))
				.GroupBy(a => a.UserId)
				.Select(g => new
				{
					UserId = g.Key,
					User = g.First().User,
					OnTimeCount = g.Count()
				})
				.OrderByDescending(x => x.OnTimeCount)
				.Take(5)
				.ToListAsync();

			ViewBag.PunctualStaff = punctualStaff;

			var tasksByPriority = allTasks
				.GroupBy(t => t.Priority ?? "Medium")
				.Select(g => new
				{
					Priority = g.Key,
					Total = g.Count(),
					Completed = g.Count(t => t.UserTasks.Any() &&
						t.UserTasks.All(ut => ut.CompletedThisWeek >= t.TargetPerWeek))
				})
				.OrderBy(x => x.Priority == "High" ? 1 : x.Priority == "Medium" ? 2 : 3)
				.ToList();

			ViewBag.TasksByPriority = tasksByPriority;

			var upcomingTasksData = await _context.Tasks
				.Include(t => t.UserTasks)
					.ThenInclude(ut => ut.User)
				.Where(t => t.IsActive == true && t.Deadline.HasValue)
				.OrderBy(t => t.Deadline)
				.Take(10)
				.ToListAsync();

			var upcomingTasks = upcomingTasksData.Select(t => new
			{
				Task = t,
				AssignedCount = t.UserTasks.Count,
				CompletedCount = t.UserTasks.Count(ut => ut.CompletedThisWeek >= t.TargetPerWeek),
				ProgressPercent = t.UserTasks.Count > 0
					? Math.Round((double)t.UserTasks.Count(ut => ut.CompletedThisWeek >= t.TargetPerWeek) / t.UserTasks.Count * 100, 1)
					: 0
			}).ToList();

			ViewBag.UpcomingTasks = upcomingTasks;

			var recentAudits = await _context.AuditLogs
				.Include(a => a.User)
				.OrderByDescending(a => a.Timestamp)
				.Take(5)
				.ToListAsync();

			ViewBag.RecentAudits = recentAudits;

			return View();
		}

		// ============================================
		// USER MANAGEMENT
		// ============================================

		public async Task<IActionResult> UserList()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var users = await _context.Users
				.Include(u => u.Role)
				.Include(u => u.Department)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			return View(users);
		}

		[HttpPost]
		public async Task<IActionResult> ToggleUserStatus([FromBody] ToggleUserRequest request)
		{
			if (!IsAdmin())
			{
				// ✅ LOG: Unauthorized
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"User",
					"Không có quyền thực hiện",
					new { UserId = request.UserId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var user = await _context.Users.FindAsync(request.UserId);
			if (user == null)
			{
				// ✅ LOG: Not found
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"User",
					"Không tìm thấy người dùng",
					new { UserId = request.UserId }
				);

				return Json(new { success = false, message = "Không tìm thấy người dùng" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");
				user.IsActive = !user.IsActive;
				user.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"UPDATE",
					"User",
					user.UserId,
					new { IsActive = !user.IsActive },
					new { IsActive = user.IsActive },
					$"Admin {(user.IsActive == true ? "kích hoạt" : "vô hiệu hóa")} tài khoản: {user.Username}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã {(user.IsActive == true ? "kích hoạt" : "vô hiệu hóa")} tài khoản {user.FullName}",
					isActive = user.IsActive
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"User",
					$"Exception: {ex.Message}",
					new { UserId = request.UserId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// RESET PASSWORD
		// ============================================

		[HttpGet]
		public async Task<IActionResult> ResetUserPassword(int id)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var user = await _context.Users.FindAsync(id);
			if (user == null)
				return NotFound();

			ViewBag.User = user;
			return View();
		}

		[HttpPost]
		public async Task<IActionResult> ResetUserPasswordJson([FromBody] ResetPasswordRequest request)
		{
			if (!IsAdmin())
			{
				// ✅ LOG: Unauthorized
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"PASSWORD_RESET",
					"User",
					"Không có quyền reset mật khẩu",
					new { TargetUserId = request.UserId }
				);

				return Json(new { success = false, message = "Chỉ Admin mới có quyền reset mật khẩu!" });
			}

			if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < 6)
			{
				// ✅ LOG: Invalid password
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"PASSWORD_RESET",
					"User",
					"Mật khẩu không hợp lệ",
					new { UserId = request.UserId }
				);

				return Json(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự" });
			}

			if (string.IsNullOrEmpty(request.Reason))
			{
				// ✅ LOG: Missing reason
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"PASSWORD_RESET",
					"User",
					"Thiếu lý do reset",
					new { UserId = request.UserId }
				);

				return Json(new { success = false, message = "Vui lòng nhập lý do reset mật khẩu" });
			}

			var user = await _context.Users.FindAsync(request.UserId);
			if (user == null)
			{
				// ✅ LOG: User not found
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"PASSWORD_RESET",
					"User",
					"User không tồn tại",
					new { UserId = request.UserId }
				);

				return Json(new { success = false, message = "Không tìm thấy người dùng" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");
				var oldHash = user.PasswordHash;

				user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
				user.UpdatedAt = DateTime.Now;

				var resetHistory = new PasswordResetHistory
				{
					UserId = user.UserId,
					ResetByUserId = adminId,
					OldPasswordHash = oldHash,
					ResetTime = DateTime.Now,
					ResetReason = request.Reason,
					Ipaddress = HttpContext.Connection.RemoteIpAddress?.ToString()
				};

				_context.PasswordResetHistories.Add(resetHistory);
				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"PASSWORD_RESET",
					"User",
					user.UserId,
					null,
					null,
					$"Admin reset mật khẩu cho user: {user.Username}. Lý do: {request.Reason}"
				);

				return Json(new
				{
					success = true,
					message = $"Reset mật khẩu thành công cho {user.FullName}!"
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"PASSWORD_RESET",
					"User",
					$"Exception: {ex.Message}",
					new { UserId = request.UserId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}
		[HttpGet]
		public async Task<IActionResult> GetUserTasks(int userId)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			try
			{
				// ✅ LOG: View user tasks
				await _auditHelper.LogViewAsync(
					HttpContext.Session.GetInt32("UserId").Value,
					"UserTask",
					userId,
					$"Xem danh sách task của user ID: {userId}"
				);

				var user = await _context.Users
					.Include(u => u.Department)
					.FirstOrDefaultAsync(u => u.UserId == userId);

				if (user == null)
					return Json(new { success = false, message = "Không tìm thấy người dùng!" });

				var userTasks = await _context.UserTasks
					.Include(ut => ut.Task)
					.Where(ut => ut.UserId == userId && ut.Task.IsActive == true)
					.ToListAsync();

				var tasks = userTasks.Select(ut => new
				{
					taskId = ut.TaskId,
					taskName = ut.Task.TaskName,
					description = ut.Task.Description ?? "",
					platform = ut.Task.Platform ?? "",
					targetPerWeek = ut.Task.TargetPerWeek ?? 0,
					completedThisWeek = ut.CompletedThisWeek ?? 0,
					reportLink = ut.ReportLink ?? "",
					startDate = ut.Task.CreatedAt?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
					deadline = ut.Task.Deadline?.ToString("yyyy-MM-dd") ?? DateTime.Now.AddDays(30).ToString("yyyy-MM-dd"),
					priority = ut.Task.Priority ?? "Medium",
					status = (ut.CompletedThisWeek ?? 0) >= (ut.Task.TargetPerWeek ?? 0) ? "Completed" : "InProgress",
					isOverdue = ut.Task.Deadline.HasValue && ut.Task.Deadline.Value < DateTime.Now
				}).ToList();

				return Json(new
				{
					success = true,
					tasks = tasks,
					user = new
					{
						userId = user.UserId,
						fullName = user.FullName,
						username = user.Username,
						departmentName = user.Department?.DepartmentName ?? "N/A"
					}
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"VIEW",
					"UserTask",
					$"Exception: {ex.Message}",
					new { UserId = userId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// GET USER DETAILS
		// ============================================

		[HttpGet]
		public async Task<IActionResult> GetUserDetails(int id)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			// ✅ LOG: View user details
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"User",
				id,
				"Xem chi tiết thông tin người dùng"
			);

			var user = await _context.Users
				.Include(u => u.Role)
				.Include(u => u.Department)
				.Include(u => u.UserTasks)
					.ThenInclude(ut => ut.Task)
				.FirstOrDefaultAsync(u => u.UserId == id);

			if (user == null)
				return Json(new { success = false, message = "Không tìm thấy người dùng!" });

			var totalLogins = await _context.LoginHistories
				.CountAsync(l => l.UserId == id && l.IsSuccess == true);

			var activeTasks = user.UserTasks?.Count(ut => ut.Task.IsActive == true) ?? 0;
			var completedTasks = user.UserTasks?.Sum(ut => ut.CompletedThisWeek) ?? 0;

			var recentActivities = await _context.AuditLogs
				.Where(a => a.UserId == id)
				.OrderByDescending(a => a.Timestamp)
				.Take(5)
				.Select(a => new
				{
					action = a.Action,
					timestamp = a.Timestamp,
					description = a.Description
				})
				.ToListAsync();

			var result = new
			{
				success = true,
				user = new
				{
					user.UserId,
					user.Username,
					user.FullName,
					user.Email,
					user.PhoneNumber,
					user.Avatar,
					departmentName = user.Department?.DepartmentName,
					roleName = user.Role?.RoleName,
					user.IsActive,
					user.CreatedAt,
					user.UpdatedAt,
					user.LastLoginAt,
					totalLogins = totalLogins,
					activeTasks = activeTasks,
					completedTasks = completedTasks,
					recentActivities = recentActivities
				}
			};

			return Json(result);
		}

		// ============================================
		// DEPARTMENT MANAGEMENT
		// ============================================

		public async Task<IActionResult> DepartmentList()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var departments = await _context.Departments
				.Include(d => d.Users)
				.OrderBy(d => d.DepartmentName)
				.ToListAsync();

			return View(departments);
		}

		[HttpGet]
		public async Task<IActionResult> DepartmentDetail(int id)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			// ✅ LOG: View department details
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"Department",
				id,
				"Xem chi tiết phòng ban"
			);

			var department = await _context.Departments
				.Include(d => d.Users)
					.ThenInclude(u => u.Role)
				.FirstOrDefaultAsync(d => d.DepartmentId == id);

			if (department == null)
				return NotFound();

			return View(department);
		}

		[HttpPost]
		public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentRequest request)
		{
			if (!IsAdmin())
			{
				// ✅ LOG: Unauthorized
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Department",
					"Không có quyền tạo phòng ban",
					new { DepartmentName = request.DepartmentName }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			if (string.IsNullOrWhiteSpace(request.DepartmentName))
			{
				// ✅ LOG: Invalid data
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Department",
					"Tên phòng ban rỗng",
					null
				);

				return Json(new { success = false, message = "Tên phòng ban không được để trống!" });
			}

			var existingDept = await _context.Departments
				.FirstOrDefaultAsync(d => d.DepartmentName.ToLower() == request.DepartmentName.Trim().ToLower());

			if (existingDept != null)
			{
				// ✅ LOG: Duplicate
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Department",
					"Tên phòng ban đã tồn tại",
					new { DepartmentName = request.DepartmentName }
				);

				return Json(new { success = false, message = "Tên phòng ban đã tồn tại!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				var department = new Department
				{
					DepartmentName = request.DepartmentName.Trim(),
					Description = request.Description?.Trim(),
					IsActive = request.IsActive,
					CreatedAt = DateTime.Now,
					UpdatedAt = DateTime.Now
				};

				_context.Departments.Add(department);
				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"CREATE",
					"Department",
					department.DepartmentId,
					null,
					new { department.DepartmentName, department.Description, department.IsActive },
					$"Tạo phòng ban mới: {department.DepartmentName}"
				);

				return Json(new
				{
					success = true,
					message = $"Tạo phòng ban '{department.DepartmentName}' thành công!",
					departmentId = department.DepartmentId
				});
			}
			catch (Exception ex)
			{
				// ✅ LOG: Exception
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Department",
					$"Exception: {ex.Message}",
					new { DepartmentName = request.DepartmentName, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}// ============================================
		 // DEPARTMENT MANAGEMENT - CONTINUED
		 // ============================================

		[HttpPost]
		public async Task<IActionResult> UpdateDepartment([FromBody] UpdateDepartmentRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Không có quyền cập nhật",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			if (string.IsNullOrWhiteSpace(request.DepartmentName))
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Tên phòng ban rỗng",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Tên phòng ban không được để trống!" });
			}

			var department = await _context.Departments.FindAsync(request.DepartmentId);

			if (department == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Phòng ban không tồn tại",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không tìm thấy phòng ban!" });
			}

			var existingDept = await _context.Departments
				.FirstOrDefaultAsync(d => d.DepartmentId != request.DepartmentId &&
										  d.DepartmentName.ToLower() == request.DepartmentName.Trim().ToLower());

			if (existingDept != null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Tên phòng ban đã tồn tại",
					new { DepartmentId = request.DepartmentId, DepartmentName = request.DepartmentName }
				);

				return Json(new { success = false, message = "Tên phòng ban đã tồn tại!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				var oldValues = new
				{
					department.DepartmentName,
					department.Description,
					department.IsActive
				};

				department.DepartmentName = request.DepartmentName.Trim();
				department.Description = request.Description?.Trim();
				department.IsActive = request.IsActive;
				department.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				var newValues = new
				{
					department.DepartmentName,
					department.Description,
					department.IsActive
				};

				await _auditHelper.LogAsync(
					adminId,
					"UPDATE",
					"Department",
					department.DepartmentId,
					oldValues,
					newValues,
					$"Cập nhật phòng ban: {department.DepartmentName}"
				);

				return Json(new
				{
					success = true,
					message = $"Cập nhật phòng ban '{department.DepartmentName}' thành công!"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					$"Exception: {ex.Message}",
					new { DepartmentId = request.DepartmentId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpPost]
		public async Task<IActionResult> ToggleDepartmentStatus([FromBody] ToggleDepartmentRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Không có quyền thực hiện",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var department = await _context.Departments
				.Include(d => d.Users)
				.FirstOrDefaultAsync(d => d.DepartmentId == request.DepartmentId);

			if (department == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					"Phòng ban không tồn tại",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không tìm thấy phòng ban!" });
			}

			if (department.IsActive == true && department.Users != null && department.Users.Any(u => u.IsActive == true))
			{
				var activeUserCount = department.Users.Count(u => u.IsActive == true);
				if (activeUserCount > 0)
				{
					await _auditHelper.LogFailedAttemptAsync(
						HttpContext.Session.GetInt32("UserId"),
						"UPDATE",
						"Department",
						"Phòng ban có nhân viên đang hoạt động",
						new { DepartmentId = request.DepartmentId, ActiveUsers = activeUserCount }
					);

					return Json(new
					{
						success = false,
						message = $"Không thể vô hiệu hóa phòng ban có {activeUserCount} nhân viên đang hoạt động!"
					});
				}
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				department.IsActive = !department.IsActive;
				department.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"UPDATE",
					"Department",
					department.DepartmentId,
					new { IsActive = !department.IsActive },
					new { IsActive = department.IsActive },
					$"Thay đổi trạng thái phòng ban: {department.DepartmentName} - {(department.IsActive == true ? "Kích hoạt" : "Vô hiệu hóa")}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã {(department.IsActive == true ? "kích hoạt" : "vô hiệu hóa")} phòng ban: {department.DepartmentName}",
					isActive = department.IsActive
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Department",
					$"Exception: {ex.Message}",
					new { DepartmentId = request.DepartmentId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpPost]
		public async Task<IActionResult> DeleteDepartment([FromBody] DeleteDepartmentRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Department",
					"Không có quyền xóa",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var department = await _context.Departments
				.Include(d => d.Users)
				.FirstOrDefaultAsync(d => d.DepartmentId == request.DepartmentId);

			if (department == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Department",
					"Phòng ban không tồn tại",
					new { DepartmentId = request.DepartmentId }
				);

				return Json(new { success = false, message = "Không tìm thấy phòng ban!" });
			}

			if (department.Users != null && department.Users.Any())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Department",
					"Phòng ban có nhân viên",
					new { DepartmentId = request.DepartmentId, UserCount = department.Users.Count }
				);

				return Json(new
				{
					success = false,
					message = $"Không thể xóa phòng ban có {department.Users.Count} nhân viên! Vui lòng chuyển họ sang phòng ban khác trước."
				});
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				department.IsActive = false;
				department.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"DELETE",
					"Department",
					department.DepartmentId,
					new { IsActive = true },
					new { IsActive = false },
					$"Xóa phòng ban: {department.DepartmentName}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã xóa phòng ban: {department.DepartmentName}"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Department",
					$"Exception: {ex.Message}",
					new { DepartmentId = request.DepartmentId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpGet]
		public async Task<IActionResult> GetDepartmentDetails(int id)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			// ✅ LOG: View department details
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"Department",
				id,
				"Xem chi tiết phòng ban (AJAX)"
			);

			var department = await _context.Departments
				.Include(d => d.Users)
					.ThenInclude(u => u.Role)
				.FirstOrDefaultAsync(d => d.DepartmentId == id);

			if (department == null)
				return Json(new { success = false, message = "Không tìm thấy phòng ban!" });

			var result = new
			{
				success = true,
				department = new
				{
					department.DepartmentId,
					department.DepartmentName,
					department.Description,
					department.IsActive,
					department.CreatedAt,
					department.UpdatedAt,
					TotalUsers = department.Users?.Count ?? 0,
					ActiveUsers = department.Users?.Count(u => u.IsActive == true) ?? 0,
					InactiveUsers = department.Users?.Count(u => u.IsActive == false) ?? 0,
					Users = department.Users?.Select(u => new
					{
						u.UserId,
						u.Username,
						u.FullName,
						u.Email,
						u.Avatar,
						RoleName = u.Role?.RoleName,
						u.IsActive
					}).OrderBy(u => u.FullName).ToList()
				}
			};

			return Json(result);
		}

		// ============================================
		// TASK MANAGEMENT - CRUD
		// ============================================

		public async Task<IActionResult> TaskList()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var tasks = await _context.Tasks
				.Include(t => t.UserTasks)
					.ThenInclude(ut => ut.User)
				.OrderByDescending(t => t.CreatedAt)
				.ToListAsync();

			ViewBag.TotalTasks = tasks.Count;
			ViewBag.ActiveTasks = tasks.Count(t => t.IsActive == true);
			ViewBag.InactiveTasks = tasks.Count(t => t.IsActive == false);
			ViewBag.OverdueTasks = tasks.Count(t => t.Deadline.HasValue && t.Deadline.Value < DateTime.Now);

			return View(tasks);
		}

		[HttpGet]
		public async Task<IActionResult> CreateTask()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			ViewBag.Users = await _context.Users
				.Include(u => u.Department)
				.Where(u => u.IsActive == true)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			return View();
		}

		[HttpPost]
		public async Task<IActionResult> CreateTaskPost([FromBody] CreateTaskRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Task",
					"Không có quyền tạo task",
					new { TaskName = request.TaskName }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			if (string.IsNullOrWhiteSpace(request.TaskName))
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Task",
					"Tên task rỗng",
					null
				);

				return Json(new { success = false, message = "Tên task không được để trống!" });
			}

			if (request.TargetPerWeek < 0)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Task",
					"Target không hợp lệ",
					new { TaskName = request.TaskName, Target = request.TargetPerWeek }
				);

				return Json(new { success = false, message = "Target phải >= 0!" });
			}

			if (request.Deadline.HasValue && request.Deadline.Value < DateTime.Now)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Task",
					"Deadline không hợp lệ",
					new { TaskName = request.TaskName, Deadline = request.Deadline }
				);

				return Json(new { success = false, message = "Deadline không được là thời điểm trong quá khứ!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				var task = new TMD.Models.Task
				{
					TaskName = request.TaskName.Trim(),
					Description = request.Description?.Trim(),
					Platform = request.Platform?.Trim(),
					TargetPerWeek = request.TargetPerWeek,
					Deadline = request.Deadline,
					Priority = request.Priority ?? "Medium",
					IsActive = true,
					CreatedAt = DateTime.Now,
					UpdatedAt = DateTime.Now
				};

				_context.Tasks.Add(task);
				await _context.SaveChangesAsync();

				if (request.AssignedUserIds != null && request.AssignedUserIds.Count > 0)
				{
					foreach (var userId in request.AssignedUserIds)
					{
						var userTask = new UserTask
						{
							UserId = userId,
							TaskId = task.TaskId,
							CompletedThisWeek = 0,
							WeekStartDate = DateOnly.FromDateTime(DateTime.Today),
							CreatedAt = DateTime.Now,
							UpdatedAt = DateTime.Now
						};
						_context.UserTasks.Add(userTask);
					}
					await _context.SaveChangesAsync();
				}

				await _auditHelper.LogDetailedAsync(
					adminId,
					"CREATE",
					"Task",
					task.TaskId,
					null,
					new { task.TaskName, task.Platform, task.Priority, task.TargetPerWeek },
					$"Tạo task mới: {task.TaskName}",
					new Dictionary<string, object>
					{
						{ "AssignedUsers", request.AssignedUserIds?.Count ?? 0 },
						{ "CreatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = "Tạo task thành công!",
					taskId = task.TaskId
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"CREATE",
					"Task",
					$"Exception: {ex.Message}",
					new { TaskName = request.TaskName, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpGet]
		public async Task<IActionResult> EditTask(int id)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var task = await _context.Tasks
				.Include(t => t.UserTasks)
					.ThenInclude(ut => ut.User)
						.ThenInclude(u => u.Department)
				.FirstOrDefaultAsync(t => t.TaskId == id);

			if (task == null)
				return NotFound();

			ViewBag.Users = await _context.Users
				.Include(u => u.Department)
				.Where(u => u.IsActive == true)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			ViewBag.AssignedUserIds = task.UserTasks.Select(ut => ut.UserId).ToList();

			return View(task);
		}

		[HttpPost]
		public async Task<IActionResult> UpdateTask([FromBody] UpdateTaskRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Không có quyền cập nhật",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			if (string.IsNullOrWhiteSpace(request.TaskName))
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Tên task rỗng",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Tên task không được để trống!" });
			}

			if (request.TargetPerWeek < 0)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Target không hợp lệ",
					new { TaskId = request.TaskId, Target = request.TargetPerWeek }
				);

				return Json(new { success = false, message = "Target phải >= 0!" });
			}

			var task = await _context.Tasks
				.Include(t => t.UserTasks)
				.FirstOrDefaultAsync(t => t.TaskId == request.TaskId);

			if (task == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Task không tồn tại",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không tìm thấy task!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				var oldValues = new
				{
					task.TaskName,
					task.Description,
					task.Platform,
					task.TargetPerWeek,
					task.Deadline,
					task.Priority
				};

				task.TaskName = request.TaskName.Trim();
				task.Description = request.Description?.Trim();
				task.Platform = request.Platform?.Trim();
				task.TargetPerWeek = request.TargetPerWeek;
				task.Deadline = request.Deadline;
				task.Priority = request.Priority ?? "Medium";
				task.UpdatedAt = DateTime.Now;

				var oldAssignments = task.UserTasks.ToList();
				_context.UserTasks.RemoveRange(oldAssignments);

				if (request.AssignedUserIds != null && request.AssignedUserIds.Count > 0)
				{
					foreach (var userId in request.AssignedUserIds)
					{
						var userTask = new UserTask
						{
							UserId = userId,
							TaskId = task.TaskId,
							CompletedThisWeek = 0,
							WeekStartDate = DateOnly.FromDateTime(DateTime.Today),
							CreatedAt = DateTime.Now,
							UpdatedAt = DateTime.Now
						};
						_context.UserTasks.Add(userTask);
					}
				}

				await _context.SaveChangesAsync();

				var newValues = new
				{
					task.TaskName,
					task.Description,
					task.Platform,
					task.TargetPerWeek,
					task.Deadline,
					task.Priority
				};

				await _auditHelper.LogAsync(
					adminId,
					"UPDATE",
					"Task",
					task.TaskId,
					oldValues,
					newValues,
					$"Cập nhật task: {task.TaskName}"
				);

				return Json(new
				{
					success = true,
					message = "Cập nhật task thành công!"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					$"Exception: {ex.Message}",
					new { TaskId = request.TaskId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpPost]
		public async Task<IActionResult> DeleteTask([FromBody] DeleteTaskRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Task",
					"Không có quyền xóa",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var task = await _context.Tasks.FindAsync(request.TaskId);

			if (task == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Task",
					"Task không tồn tại",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không tìm thấy task!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				task.IsActive = false;
				task.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"DELETE",
					"Task",
					task.TaskId,
					new { IsActive = true },
					new { IsActive = false },
					$"Xóa task: {task.TaskName}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã xóa task: {task.TaskName}"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"Task",
					$"Exception: {ex.Message}",
					new { TaskId = request.TaskId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpPost]
		public async Task<IActionResult> ToggleTaskStatus([FromBody] ToggleTaskStatusRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Không có quyền thực hiện",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var task = await _context.Tasks.FindAsync(request.TaskId);

			if (task == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					"Task không tồn tại",
					new { TaskId = request.TaskId }
				);

				return Json(new { success = false, message = "Không tìm thấy task!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");

				task.IsActive = !task.IsActive;
				task.UpdatedAt = DateTime.Now;

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"UPDATE",
					"Task",
					task.TaskId,
					new { IsActive = !task.IsActive },
					new { IsActive = task.IsActive },
					$"Thay đổi trạng thái task: {task.TaskName} - {(task.IsActive == true ? "Kích hoạt" : "Vô hiệu hóa")}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã {(task.IsActive == true ? "kích hoạt" : "vô hiệu hóa")} task: {task.TaskName}",
					isActive = task.IsActive
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"Task",
					$"Exception: {ex.Message}",
					new { TaskId = request.TaskId, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		[HttpGet]
		public async Task<IActionResult> GetTaskDetails(int id)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			// ✅ LOG: View task details
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"Task",
				id,
				"Xem chi tiết task"
			);

			var task = await _context.Tasks
				.Include(t => t.UserTasks)
					.ThenInclude(ut => ut.User)
						.ThenInclude(u => u.Department)
				.FirstOrDefaultAsync(t => t.TaskId == id);

			if (task == null)
				return Json(new { success = false, message = "Không tìm thấy task!" });

			var result = new
			{
				success = true,
				task = new
				{
					task.TaskId,
					task.TaskName,
					task.Description,
					task.Platform,
					task.TargetPerWeek,
					task.Deadline,
					task.Priority,
					task.IsActive,
					task.CreatedAt,
					task.UpdatedAt,
					AssignedUsers = task.UserTasks.Select(ut => new
					{
						ut.User.UserId,
						ut.User.FullName,
						ut.User.Avatar,
						DepartmentName = ut.User.Department?.DepartmentName,
						ut.CompletedThisWeek,
						ut.ReportLink
					}).ToList()
				}
			};

			return Json(result);
		}

		// ============================================
		// ATTENDANCE MANAGEMENT
		// ============================================

		public async Task<IActionResult> AttendanceList(DateTime? date)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var selectedDate = date ?? DateTime.Today;

			// ✅ LOG: View attendance list
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"Attendance",
				0,
				$"Xem danh sách chấm công ngày {selectedDate:dd/MM/yyyy}"
			);

			var attendances = await _context.Attendances
				.Include(a => a.User)
					.ThenInclude(u => u.Department)
				.Where(a => a.WorkDate == DateOnly.FromDateTime(selectedDate))
				.OrderByDescending(a => a.CheckInTime)
				.ToListAsync();

			ViewBag.SelectedDate = selectedDate;
			return View(attendances);
		}

		public async Task<IActionResult> AttendanceHistory(int? userId, DateTime? fromDate, DateTime? toDate, int? departmentId)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var from = fromDate ?? DateTime.Today.AddDays(-30);
			var to = toDate ?? DateTime.Today;

			// ✅ LOG: View attendance history
			await _auditHelper.LogDetailedAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"VIEW",
				"Attendance",
				null,
				null,
				null,
				"Xem lịch sử chấm công tổng hợp",
				new Dictionary<string, object>
				{
					{ "FilterUserId", userId ?? 0 },
					{ "FilterDepartment", departmentId ?? 0 },
					{ "FromDate", from.ToString("yyyy-MM-dd") },
					{ "ToDate", to.ToString("yyyy-MM-dd") }
				}
			);

			var query = _context.Attendances
				.Include(a => a.User)
					.ThenInclude(u => u.Department)
				.AsQueryable();

			if (userId.HasValue && userId.Value > 0)
			{
				query = query.Where(a => a.UserId == userId.Value);
			}

			if (departmentId.HasValue && departmentId.Value > 0)
			{
				query = query.Where(a => a.User.DepartmentId == departmentId.Value);
			}

			query = query.Where(a =>
				a.WorkDate >= DateOnly.FromDateTime(from) &&
				a.WorkDate <= DateOnly.FromDateTime(to)
			);

			var attendances = await query
				.OrderByDescending(a => a.WorkDate)
				.ThenByDescending(a => a.CheckInTime)
				.ToListAsync();

			ViewBag.TotalRecords = attendances.Count;
			ViewBag.TotalCheckIns = attendances.Count(a => a.CheckInTime != null);
			ViewBag.TotalCheckOuts = attendances.Count(a => a.CheckOutTime != null);
			ViewBag.CompletedDays = attendances.Count(a => a.CheckInTime != null && a.CheckOutTime != null);
			ViewBag.OnTimeCount = attendances.Count(a => a.IsLate == false);
			ViewBag.LateCount = attendances.Count(a => a.IsLate == true);
			ViewBag.TotalWorkHours = attendances.Sum(a => a.TotalHours ?? 0);
			ViewBag.WithinGeofence = attendances.Count(a => a.IsWithinGeofence == true);
			ViewBag.OutsideGeofence = attendances.Count(a => a.IsWithinGeofence == false);

			ViewBag.Users = await _context.Users
				.Where(u => u.IsActive == true)
				.OrderBy(u => u.FullName)
				.ToListAsync();

			ViewBag.Departments = await _context.Departments
				.OrderBy(d => d.DepartmentName)
				.ToListAsync();

			ViewBag.SelectedUserId = userId;
			ViewBag.SelectedDepartmentId = departmentId;
			ViewBag.FromDate = from;
			ViewBag.ToDate = to;

			return View(attendances);
		}

		// ============================================
		// AUDIT LOGS
		// ============================================

		public async Task<IActionResult> AuditLogs(string? action, DateTime? fromDate, DateTime? toDate)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			// ✅ LOG: View audit logs (meta!)
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"AuditLog",
				0,
				$"Xem nhật ký hoạt động - Filter: {action ?? "All"}"
			);

			var query = _context.AuditLogs
				.Include(a => a.User)
				.AsQueryable();

			if (!string.IsNullOrEmpty(action))
				query = query.Where(a => a.Action == action);

			if (fromDate.HasValue)
				query = query.Where(a => a.Timestamp.HasValue && a.Timestamp.Value >= fromDate.Value);

			if (toDate.HasValue)
				query = query.Where(a => a.Timestamp.HasValue && a.Timestamp.Value <= toDate.Value.AddDays(1));

			var logs = await query
				.OrderByDescending(a => a.Timestamp)
				.Take(1000)
				.ToListAsync();

			ViewBag.Actions = await _context.AuditLogs
				.Select(a => a.Action)
				.Distinct()
				.ToListAsync();

			ViewBag.SelectedAction = action;
			ViewBag.FromDate = fromDate;
			ViewBag.ToDate = toDate;

			return View(logs);
		}

		// ============================================
		// LOGIN HISTORY
		// ============================================

		public async Task<IActionResult> LoginHistory(DateTime? fromDate, DateTime? toDate, bool? isSuccess)
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			// ✅ LOG: View login history
			await _auditHelper.LogDetailedAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"VIEW",
				"LoginHistory",
				null,
				null,
				null,
				"Xem lịch sử đăng nhập hệ thống",
				new Dictionary<string, object>
				{
					{ "FromDate", fromDate?.ToString("yyyy-MM-dd") ?? "All" },
					{ "ToDate", toDate?.ToString("yyyy-MM-dd") ?? "All" },
					{ "FilterSuccess", isSuccess?.ToString() ?? "All" }
				}
			);

			var query = _context.LoginHistories
				.Include(l => l.User)
				.AsQueryable();

			if (fromDate.HasValue)
				query = query.Where(l => l.LoginTime.HasValue && l.LoginTime.Value >= fromDate.Value);

			if (toDate.HasValue)
				query = query.Where(l => l.LoginTime.HasValue && l.LoginTime.Value <= toDate.Value.AddDays(1));

			if (isSuccess.HasValue)
				query = query.Where(l => l.IsSuccess == isSuccess.Value);

			var history = await query
				.OrderByDescending(l => l.LoginTime)
				.Take(1000)
				.ToListAsync();

			ViewBag.FromDate = fromDate;
			ViewBag.ToDate = toDate;
			ViewBag.IsSuccess = isSuccess;

			return View(history);
		}
		// Thêm vào AdminController.cs
		[HttpGet]
		public async Task<IActionResult> GetAllUsers()
		{
			try
			{
				var users = await _context.Users
					.Include(u => u.Department)
					.Where(u => u.IsActive == true)
					.OrderBy(u => u.FullName)
					.Select(u => new
					{
						userId = u.UserId,
						fullName = u.FullName,
						email = u.Email,
						departmentName = u.Department != null ? u.Department.DepartmentName : "N/A"
					})
					.ToListAsync();

				return Json(new { success = true, users = users });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = ex.Message });
			}
		}
		// ============================================
		// PASSWORD RESET HISTORY
		// ============================================

		public async Task<IActionResult> PasswordResetHistory()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			// ✅ LOG: View password reset history
			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"PasswordResetHistory",
				0,
				"Xem lịch sử reset mật khẩu"
			);

			var history = await _context.PasswordResetHistories
				.Include(p => p.User)
				.Include(p => p.ResetByUser)
				.OrderByDescending(p => p.ResetTime)
				.ToListAsync();

			return View(history);
		}

		// ============================================
		// REQUEST MODELS
		// ============================================

		public class CreateDepartmentRequest
		{
			public string DepartmentName { get; set; } = string.Empty;
			public string? Description { get; set; }
			public bool IsActive { get; set; } = true;
		}

		public class UpdateDepartmentRequest
		{
			public int DepartmentId { get; set; }
			public string DepartmentName { get; set; } = string.Empty;
			public string? Description { get; set; }
			public bool IsActive { get; set; }
		}

		public class DeleteDepartmentRequest
		{
			public int DepartmentId { get; set; }
		}

		public class ToggleDepartmentRequest
		{
			public int DepartmentId { get; set; }
		}

		public class ResetPasswordRequest
		{
			public int UserId { get; set; }
			public string NewPassword { get; set; } = string.Empty;
			public string Reason { get; set; } = string.Empty;
		}

		public class ToggleUserRequest
		{
			public int UserId { get; set; }
		}

		public class CreateTaskRequest
		{
			public string TaskName { get; set; } = string.Empty;
			public string? Description { get; set; }
			public string? Platform { get; set; }
			public int TargetPerWeek { get; set; }
			public DateTime? Deadline { get; set; }
			public string? Priority { get; set; }
			public List<int>? AssignedUserIds { get; set; }
		}

		public class UpdateTaskRequest
		{
			public int TaskId { get; set; }
			public string TaskName { get; set; } = string.Empty;
			public string? Description { get; set; }
			public string? Platform { get; set; }
			public int TargetPerWeek { get; set; }
			public DateTime? Deadline { get; set; }
			public string? Priority { get; set; }
			public List<int>? AssignedUserIds { get; set; }
		}

		public class DeleteTaskRequest
		{
			public int TaskId { get; set; }
		}

		public class ToggleTaskStatusRequest
		{
			public int TaskId { get; set; }
		}
	}
}