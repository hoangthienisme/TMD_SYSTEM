using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TMDSystem.Helpers;
using TMD.Models;

namespace TMDSystem.Controllers
{
	public class SettingsController : Controller
	{
		private readonly TmdContext _context;
		private readonly AuditHelper _auditHelper;
		private readonly IWebHostEnvironment _env;

		public SettingsController(TmdContext context, AuditHelper auditHelper, IWebHostEnvironment env)
		{
			_context = context;
			_auditHelper = auditHelper;
			_env = env;
		}

		private bool IsAdmin()
		{
			return HttpContext.Session.GetString("RoleName") == "Admin";
		}

		// ============================================
		// SETTINGS PAGE
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> Index()
		{
			if (!IsAdmin())
				return RedirectToAction("Login", "Account");

			var settings = await _context.SystemSettings
				.Where(s => s.IsActive == true)
				.OrderBy(s => s.Category)
				.ThenBy(s => s.SettingKey)
				.ToListAsync();

			if (!settings.Any())
			{
				await InitializeDefaultSettings();
				settings = await _context.SystemSettings.ToListAsync();
			}

			await _auditHelper.LogViewAsync(
				HttpContext.Session.GetInt32("UserId").Value,
				"SystemSettings",
				0,
				"Xem trang cấu hình hệ thống"
			);

			return View(settings);
		}

		// ============================================
		// GET SETTING BY KEY
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetSetting(string key)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			var setting = await _context.SystemSettings
				.FirstOrDefaultAsync(s => s.SettingKey == key);

			if (setting == null)
				return Json(new { success = false, message = "Không tìm thấy cấu hình!" });

			return Json(new
			{
				success = true,
				setting = new
				{
					setting.SettingId,
					setting.SettingKey,
					setting.SettingValue,
					setting.Description,
					setting.DataType,
					setting.Category,
					setting.IsActive,
					setting.UpdatedAt
				}
			});
		}

		// ============================================
		// UPDATE SINGLE SETTING
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> UpdateSetting([FromBody] UpdateSettingRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"SystemSettings",
					"Không có quyền cập nhật",
					new { SettingKey = request.SettingKey }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			var setting = await _context.SystemSettings
				.FirstOrDefaultAsync(s => s.SettingKey == request.SettingKey);

			if (setting == null)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"SystemSettings",
					"Setting không tồn tại",
					new { SettingKey = request.SettingKey }
				);

				return Json(new { success = false, message = "Không tìm thấy cấu hình!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");
				var oldValue = setting.SettingValue;

				setting.SettingValue = request.SettingValue;
				setting.UpdatedAt = DateTime.Now;
				setting.UpdatedBy = adminId;

				await _context.SaveChangesAsync();

				await _auditHelper.LogDetailedAsync(
					adminId,
					"UPDATE",
					"SystemSettings",
					setting.SettingId,
					new { SettingValue = oldValue },
					new { SettingValue = request.SettingValue },
					$"Cập nhật cấu hình: {setting.SettingKey}",
					new Dictionary<string, object>
					{
						{ "OldValue", oldValue ?? "null" },
						{ "NewValue", request.SettingValue ?? "null" },
						{ "UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = "Cập nhật cấu hình thành công!"
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"SystemSettings",
					$"Exception: {ex.Message}",
					new { SettingKey = request.SettingKey, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// BATCH UPDATE SETTINGS
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> BatchUpdateSettings([FromBody] List<UpdateSettingRequest> requests)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"SystemSettings",
					"Không có quyền cập nhật hàng loạt",
					new { Count = requests?.Count ?? 0 }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			if (requests == null || !requests.Any())
			{
				return Json(new { success = false, message = "Không có dữ liệu để cập nhật!" });
			}

			try
			{
				var adminId = HttpContext.Session.GetInt32("UserId");
				var updatedCount = 0;
				var createdCount = 0;
				var failedKeys = new List<string>();

				foreach (var request in requests)
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingKey == request.SettingKey);

					if (setting == null)
					{
						// Tự động tạo setting mới nếu chưa tồn tại
						setting = new SystemSetting
						{
							SettingKey = request.SettingKey,
							SettingValue = request.SettingValue,
							Description = GetDescriptionForKey(request.SettingKey),
							DataType = GetDataTypeForKey(request.SettingKey),
							Category = GetCategoryForKey(request.SettingKey),
							IsActive = true,
							CreatedAt = DateTime.Now,
							UpdatedBy = adminId
						};

						_context.SystemSettings.Add(setting);
						createdCount++;

						await _auditHelper.LogDetailedAsync(
							adminId,
							"CREATE",
							"SystemSettings",
							null,
							null,
							new { SettingValue = request.SettingValue },
							$"Tạo mới setting: {setting.SettingKey}",
							new Dictionary<string, object>
							{
								{ "NewValue", request.SettingValue ?? "null" }
							}
						);
					}
					else
					{
						var oldValue = setting.SettingValue;

						setting.SettingValue = request.SettingValue;
						setting.UpdatedAt = DateTime.Now;
						setting.UpdatedBy = adminId;

						await _auditHelper.LogDetailedAsync(
							adminId,
							"UPDATE",
							"SystemSettings",
							setting.SettingId,
							new { SettingValue = oldValue },
							new { SettingValue = request.SettingValue },
							$"Batch update: {setting.SettingKey}",
							new Dictionary<string, object>
							{
								{ "OldValue", oldValue ?? "null" },
								{ "NewValue", request.SettingValue ?? "null" }
							}
						);
					}

					updatedCount++;
				}

				await _context.SaveChangesAsync();

				// Log tổng kết
				await _auditHelper.LogDetailedAsync(
					adminId,
					"BATCH_UPDATE",
					"SystemSettings",
					null,
					null,
					null,
					$"Batch update hoàn tất: {updatedCount}/{requests.Count} (Tạo mới: {createdCount})",
					new Dictionary<string, object>
					{
						{ "TotalRequests", requests.Count },
						{ "UpdatedCount", updatedCount },
						{ "CreatedCount", createdCount },
						{ "FailedCount", failedKeys.Count },
						{ "FailedKeys", string.Join(", ", failedKeys) }
					}
				);

				return Json(new
				{
					success = true,
					message = $"Đã cập nhật {updatedCount}/{requests.Count} cấu hình! (Tạo mới: {createdCount})",
					updatedCount = updatedCount,
					createdCount = createdCount,
					totalRequests = requests.Count,
					failedKeys = failedKeys
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"BATCH_UPDATE",
					"SystemSettings",
					$"Exception: {ex.Message}",
					new { RequestCount = requests.Count, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
			}
		}

		// ============================================
		// UPLOAD LOGO
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> UploadLogo(IFormFile file)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền!" });

			if (file == null || file.Length == 0)
				return Json(new { success = false, message = "Không có file được chọn!" });

			// Kiểm tra định dạng file
			var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".svg" };
			var extension = Path.GetExtension(file.FileName).ToLower();

			if (!allowedExtensions.Contains(extension))
				return Json(new { success = false, message = "Chỉ chấp nhận file ảnh (jpg, png, gif, svg)!" });

			// Kiểm tra kích thước (max 5MB)
			if (file.Length > 5 * 1024 * 1024)
				return Json(new { success = false, message = "File không được vượt quá 5MB!" });

			try
			{
				// Tạo thư mục nếu chưa tồn tại
				var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "logos");
				if (!Directory.Exists(uploadsFolder))
					Directory.CreateDirectory(uploadsFolder);

				// Tạo tên file unique
				var fileName = $"logo_{DateTime.Now:yyyyMMddHHmmss}{extension}";
				var filePath = Path.Combine(uploadsFolder, fileName);

				// Lưu file
				using (var stream = new FileStream(filePath, FileMode.Create))
				{
					await file.CopyToAsync(stream);
				}

				var logoUrl = $"/uploads/logos/{fileName}";

				// Cập nhật setting
				var logoSetting = await _context.SystemSettings
					.FirstOrDefaultAsync(s => s.SettingKey == "LOGO_URL");

				if (logoSetting != null)
				{
					logoSetting.SettingValue = logoUrl;
					logoSetting.UpdatedAt = DateTime.Now;
					logoSetting.UpdatedBy = HttpContext.Session.GetInt32("UserId");
				}
				else
				{
					_context.SystemSettings.Add(new SystemSetting
					{
						SettingKey = "LOGO_URL",
						SettingValue = logoUrl,
						Description = "URL Logo hệ thống",
						DataType = "String",
						Category = "Branding",
						IsActive = true,
						CreatedAt = DateTime.Now,
						UpdatedBy = HttpContext.Session.GetInt32("UserId")
					});
				}

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPLOAD",
					"SystemSettings",
					null,
					null,
					null,
					$"Upload logo mới: {fileName}"
				);

				return Json(new
				{
					success = true,
					message = "Upload logo thành công!",
					logoUrl = logoUrl
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi upload: {ex.Message}" });
			}
		}

		// ============================================
		// INITIALIZE DEFAULT SETTINGS
		// ============================================
		private async System.Threading.Tasks.Task InitializeDefaultSettings()
		{
			var defaultSettings = new List<SystemSetting>
			{
				// ========== SALARY ==========
				new SystemSetting
				{
					SettingKey = "BASE_SALARY",
					SettingValue = "5000000",
					Description = "Lương cơ bản mặc định (VNĐ)",
					DataType = "Decimal",
					Category = "Salary",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "OVERTIME_RATE",
					SettingValue = "1.5",
					Description = "Hệ số lương tăng ca (x1.5)",
					DataType = "Decimal",
					Category = "Salary",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "LATE_DEDUCTION",
					SettingValue = "50000",
					Description = "Khấu trừ mỗi lần đi muộn (VNĐ)",
					DataType = "Decimal",
					Category = "Salary",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "STANDARD_HOURS_PER_DAY",
					SettingValue = "8",
					Description = "Số giờ làm chuẩn/ngày",
					DataType = "Decimal",
					Category = "Salary",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "WORK_DAYS_PER_MONTH",
					SettingValue = "26",
					Description = "Số ngày làm việc/tháng",
					DataType = "Number",
					Category = "Salary",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== ATTENDANCE ==========
				new SystemSetting
				{
					SettingKey = "CHECK_IN_START_TIME",
					SettingValue = "07:00",
					Description = "Giờ bắt đầu cho phép check-in",
					DataType = "String",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "CHECK_IN_STANDARD_TIME",
					SettingValue = "08:00",
					Description = "Giờ chuẩn check-in (muộn hơn = đi muộn)",
					DataType = "String",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "CHECK_OUT_MIN_TIME",
					SettingValue = "17:00",
					Description = "Giờ tối thiểu check-out",
					DataType = "String",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "GEOFENCE_ENABLED",
					SettingValue = "true",
					Description = "Bật kiểm tra vị trí địa lý",
					DataType = "Boolean",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "GEOFENCE_RADIUS",
					SettingValue = "100",
					Description = "Bán kính cho phép (mét)",
					DataType = "Number",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "OFFICE_LATITUDE",
					SettingValue = "10.7769",
					Description = "Vĩ độ văn phòng",
					DataType = "Decimal",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "OFFICE_LONGITUDE",
					SettingValue = "106.7009",
					Description = "Kinh độ văn phòng",
					DataType = "Decimal",
					Category = "Attendance",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== GENERAL ==========
				new SystemSetting
				{
					SettingKey = "SYSTEM_NAME",
					SettingValue = "TMD System",
					Description = "Tên hệ thống",
					DataType = "String",
					Category = "General",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "COMPANY_NAME",
					SettingValue = "Công ty TMD",
					Description = "Tên công ty",
					DataType = "String",
					Category = "General",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "COMPANY_ADDRESS",
					SettingValue = "TP. Hồ Chí Minh",
					Description = "Địa chỉ công ty",
					DataType = "String",
					Category = "General",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "COMPANY_PHONE",
					SettingValue = "0123456789",
					Description = "Số điện thoại công ty",
					DataType = "String",
					Category = "General",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "ADMIN_EMAIL",
					SettingValue = "admin@tmd.com",
					Description = "Email admin hệ thống",
					DataType = "String",
					Category = "General",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== NOTIFICATION ==========
				new SystemSetting
				{
					SettingKey = "ENABLE_EMAIL_NOTIFICATION",
					SettingValue = "false",
					Description = "Bật gửi email thông báo",
					DataType = "Boolean",
					Category = "Notification",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "ENABLE_LATE_WARNING",
					SettingValue = "true",
					Description = "Bật cảnh báo đi muộn",
					DataType = "Boolean",
					Category = "Notification",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "MAX_LATE_DAYS_PER_MONTH",
					SettingValue = "5",
					Description = "Số lần đi muộn tối đa/tháng",
					DataType = "Number",
					Category = "Notification",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== CUSTOM CODE ==========
				new SystemSetting
				{
					SettingKey = "CUSTOM_CSS",
					SettingValue = "/* Custom CSS - Áp dụng cho toàn hệ thống */\n\n.custom-highlight {\n    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);\n    color: white;\n    padding: 10px 20px;\n    border-radius: 8px;\n}\n\n/* Thêm CSS tùy chỉnh của bạn ở đây */",
					Description = "Custom CSS cho toàn hệ thống",
					DataType = "Code",
					Category = "CustomCode",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "CUSTOM_JS",
					SettingValue = "// Custom JavaScript - Áp dụng cho toàn hệ thống\n\nconsole.log('✅ Custom JS loaded successfully!');\n\n// Thêm JavaScript tùy chỉnh của bạn ở đây\n// Ví dụ: Tự động focus vào input đầu tiên\n/*\ndocument.addEventListener('DOMContentLoaded', function() {\n    const firstInput = document.querySelector('input[type=\"text\"]');\n    if (firstInput) firstInput.focus();\n});\n*/",
					Description = "Custom JavaScript cho toàn hệ thống",
					DataType = "Code",
					Category = "CustomCode",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "ADMIN_CUSTOM_CSS",
					SettingValue = "/* Admin Custom CSS - Chỉ áp dụng cho trang Admin */\n\n.admin-special {\n    border-left: 4px solid #E74C3C;\n    padding-left: 15px;\n}\n\n/* Thêm CSS riêng cho Admin */",
					Description = "Custom CSS cho khu vực Admin",
					DataType = "Code",
					Category = "CustomCode",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "ADMIN_CUSTOM_JS",
					SettingValue = "// Admin Custom JavaScript - Chỉ áp dụng cho trang Admin\n\nconsole.log('✅ Admin Custom JS loaded!');\n\n// Thêm JavaScript riêng cho Admin\n// Ví dụ: Track admin actions\n/*\nfunction trackAdminAction(action) {\n    console.log('Admin action:', action);\n    // Send to analytics...\n}\n*/",
					Description = "Custom JavaScript cho khu vực Admin",
					DataType = "Code",
					Category = "CustomCode",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== LAYOUT & DESIGN (NEW) ==========
				new SystemSetting
				{
					SettingKey = "HEADER_HTML",
					SettingValue = "<div class=\"custom-header\" style=\"background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 20px; text-align: center; color: white; border-radius: 12px; margin-bottom: 20px;\">\n    <h1 style=\"margin: 0; font-size: 2rem;\">🏢 TMD System</h1>\n    <p style=\"margin: 5px 0 0 0; opacity: 0.9;\">Hệ thống quản lý nhân sự thông minh</p>\n</div>",
					Description = "HTML cho Header tùy chỉnh",
					DataType = "Code",
					Category = "Layout",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "FOOTER_HTML",
					SettingValue = "<footer class=\"custom-footer\" style=\"background: #2C3E50; color: white; padding: 30px 20px; text-align: center; border-radius: 12px; margin-top: 40px;\">\n    <div style=\"margin-bottom: 15px;\">\n        <a href=\"#\" style=\"color: white; text-decoration: none; margin: 0 15px;\">Về chúng tôi</a>\n        <a href=\"#\" style=\"color: white; text-decoration: none; margin: 0 15px;\">Liên hệ</a>\n        <a href=\"#\" style=\"color: white; text-decoration: none; margin: 0 15px;\">Hỗ trợ</a>\n    </div>\n    <p style=\"margin: 0; opacity: 0.8;\">&copy; 2025 TMD System. All rights reserved.</p>\n</footer>",
					Description = "HTML cho Footer tùy chỉnh",
					DataType = "Code",
					Category = "Layout",
					IsActive = true,
					CreatedAt = DateTime.Now
				},

				// ========== BRANDING (NEW) ==========
				new SystemSetting
				{
					SettingKey = "LOGO_URL",
					SettingValue = "/images/logo.png",
					Description = "URL Logo hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "PRIMARY_COLOR",
					SettingValue = "#E74C3C",
					Description = "Màu chủ đạo của hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "SECONDARY_COLOR",
					SettingValue = "#F39C12",
					Description = "Màu phụ của hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "FONT_FAMILY",
					SettingValue = "Segoe UI",
					Description = "Font chữ hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "FONT_SIZE_BASE",
					SettingValue = "16",
					Description = "Kích thước font cơ bản (px)",
					DataType = "Number",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "SYSTEM_DISPLAY_NAME",
					SettingValue = "TMD System",
					Description = "Tên hiển thị hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				},
				new SystemSetting
				{
					SettingKey = "SYSTEM_TAGLINE",
					SettingValue = "Quản lý nhân sự thông minh",
					Description = "Slogan hệ thống",
					DataType = "String",
					Category = "Branding",
					IsActive = true,
					CreatedAt = DateTime.Now
				}
			};

			_context.SystemSettings.AddRange(defaultSettings);
			await _context.SaveChangesAsync();

			await _auditHelper.LogAsync(
				HttpContext.Session.GetInt32("UserId"),
				"CREATE",
				"SystemSettings",
				null,
				null,
				null,
				$"Khởi tạo {defaultSettings.Count} cấu hình mặc định"
			);
		}

		// ============================================
		// HELPER METHODS
		// ============================================
		private string GetDescriptionForKey(string key)
		{
			var descriptions = new Dictionary<string, string>
			{
				{ "HEADER_HTML", "HTML cho Header tùy chỉnh" },
				{ "FOOTER_HTML", "HTML cho Footer tùy chỉnh" },
				{ "LOGO_URL", "URL Logo hệ thống" },
				{ "PRIMARY_COLOR", "Màu chủ đạo của hệ thống" },
				{ "SECONDARY_COLOR", "Màu phụ của hệ thống" },
				{ "FONT_FAMILY", "Font chữ hệ thống" },
				{ "FONT_SIZE_BASE", "Kích thước font cơ bản (px)" },
				{ "SYSTEM_DISPLAY_NAME", "Tên hiển thị hệ thống" },
				{ "SYSTEM_TAGLINE", "Slogan hệ thống" }
			};

			return descriptions.ContainsKey(key) ? descriptions[key] : "Cấu hình tự động tạo";
		}

		private string GetDataTypeForKey(string key)
		{
			if (key.Contains("HTML") || key.Contains("CSS") || key.Contains("JS"))
				return "Code";
			if (key.Contains("COLOR"))
				return "String";
			if (key.Contains("SIZE"))
				return "Number";
			return "String";
		}

		private string GetCategoryForKey(string key)
		{
			if (key.Contains("HTML"))
				return "Layout";
			if (key.Contains("CSS") || key.Contains("JS"))
				return "CustomCode";
			if (key.Contains("COLOR") || key.Contains("LOGO") || key.Contains("FONT") || key.Contains("DISPLAY") || key.Contains("TAGLINE"))
				return "Branding";
			return "General";
		}

		// ============================================
		// REQUEST MODELS
		// ============================================
		public class UpdateSettingRequest
		{
			public string SettingKey { get; set; } = string.Empty;
			public string? SettingValue { get; set; }
		}

		// ============================================
		// HELPER METHOD: Get Setting Value by Key
		// ============================================
		public static string GetSettingValue(TmdContext context, string key)
		{
			var setting = context.SystemSettings
				.FirstOrDefault(s => s.SettingKey == key && s.IsActive == true);
			return setting?.SettingValue ?? string.Empty;
		}

		// ============================================
		// HELPER METHOD: Get Custom Styles (NEW)
		// ============================================
		public static string GetCustomStyles(TmdContext context)
		{
			var customCss = GetSettingValue(context, "CUSTOM_CSS");
			var adminCustomCss = GetSettingValue(context, "ADMIN_CUSTOM_CSS");

			var styles = "";

			if (!string.IsNullOrWhiteSpace(customCss))
			{
				styles += $@"
<style id=""system-custom-css"">
/* === CUSTOM CSS - Áp dụng toàn hệ thống === */
{customCss}
</style>";
			}

			if (!string.IsNullOrWhiteSpace(adminCustomCss))
			{
				styles += $@"
<style id=""admin-custom-css"">
/* === ADMIN CUSTOM CSS === */
{adminCustomCss}
</style>";
			}

			return styles;
		}

		// ============================================
		// HELPER METHOD: Get Custom Scripts (NEW)
		// ============================================
		public static string GetCustomScripts(TmdContext context)
		{
			var customJs = GetSettingValue(context, "CUSTOM_JS");
			var adminCustomJs = GetSettingValue(context, "ADMIN_CUSTOM_JS");

			var scripts = "";

			if (!string.IsNullOrWhiteSpace(customJs))
			{
				scripts += $@"
<script id=""system-custom-js"">
// === CUSTOM JS - Áp dụng toàn hệ thống ===
(function() {{
{customJs}
}})();
</script>";
			}

			if (!string.IsNullOrWhiteSpace(adminCustomJs))
			{
				scripts += $@"
<script id=""admin-custom-js"">
// === ADMIN CUSTOM JS ===
(function() {{
{adminCustomJs}
}})();
</script>";
			}

			return scripts;
		}

		// ============================================
		// GET ALL SETTINGS AS JSON (API)
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> GetAllSettings()
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			var settings = await _context.SystemSettings
				.Where(s => s.IsActive == true)
				.Select(s => new
				{
					s.SettingKey,
					s.SettingValue,
					s.Category,
					s.Description
				})
				.ToListAsync();

			return Json(new { success = true, settings });
		}

		// ============================================
		// DELETE SETTING
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> DeleteSetting(string key)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền xóa!" });

			var setting = await _context.SystemSettings
				.FirstOrDefaultAsync(s => s.SettingKey == key);

			if (setting == null)
				return Json(new { success = false, message = "Không tìm thấy cấu hình!" });

			try
			{
				// Soft delete
				setting.IsActive = false;
				setting.UpdatedAt = DateTime.Now;
				setting.UpdatedBy = HttpContext.Session.GetInt32("UserId");

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"SystemSettings",
					setting.SettingId,
					new { SettingKey = key },
					null,
					$"Xóa cấu hình: {key}"
				);

				return Json(new { success = true, message = "Đã xóa cấu hình!" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// RESET TO DEFAULT
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> ResetToDefault()
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền reset!" });

			try
			{
				// Xóa tất cả settings hiện tại
				var currentSettings = await _context.SystemSettings.ToListAsync();
				_context.SystemSettings.RemoveRange(currentSettings);
				await _context.SaveChangesAsync();

				// Khởi tạo lại default
				await InitializeDefaultSettings();

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"RESET",
					"SystemSettings",
					null,
					null,
					null,
					"Reset tất cả cấu hình về mặc định"
				);

				return Json(new
				{
					success = true,
					message = "Đã reset tất cả cấu hình về mặc định!"
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// EXPORT SETTINGS (JSON)
		// ============================================
		[HttpGet]
		public async System.Threading.Tasks.Task<IActionResult> ExportSettings()
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền export!" });

			var settings = await _context.SystemSettings
				.Where(s => s.IsActive == true)
				.Select(s => new
				{
					s.SettingKey,
					s.SettingValue,
					s.Description,
					s.DataType,
					s.Category
				})
				.ToListAsync();

			var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
			{
				WriteIndented = true
			});

			var bytes = System.Text.Encoding.UTF8.GetBytes(json);
			var fileName = $"settings_backup_{DateTime.Now:yyyyMMddHHmmss}.json";

			await _auditHelper.LogAsync(
				HttpContext.Session.GetInt32("UserId"),
				"EXPORT",
				"SystemSettings",
				null,
				null,
				null,
				$"Export {settings.Count} cấu hình"
			);

			return File(bytes, "application/json", fileName);
		}

		// ============================================
		// IMPORT SETTINGS (JSON)
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> ImportSettings(IFormFile file)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền import!" });

			if (file == null || file.Length == 0)
				return Json(new { success = false, message = "Không có file được chọn!" });

			if (!file.FileName.EndsWith(".json"))
				return Json(new { success = false, message = "Chỉ chấp nhận file JSON!" });

			try
			{
				using var reader = new StreamReader(file.OpenReadStream());
				var jsonContent = await reader.ReadToEndAsync();

				var importedSettings = System.Text.Json.JsonSerializer.Deserialize<List<ImportSettingModel>>(jsonContent);

				if (importedSettings == null || !importedSettings.Any())
					return Json(new { success = false, message = "File JSON không hợp lệ!" });

				var adminId = HttpContext.Session.GetInt32("UserId");
				var updatedCount = 0;
				var createdCount = 0;

				foreach (var imported in importedSettings)
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingKey == imported.SettingKey);

					if (setting != null)
					{
						setting.SettingValue = imported.SettingValue;
						setting.UpdatedAt = DateTime.Now;
						setting.UpdatedBy = adminId;
						updatedCount++;
					}
					else
					{
						_context.SystemSettings.Add(new SystemSetting
						{
							SettingKey = imported.SettingKey,
							SettingValue = imported.SettingValue,
							Description = imported.Description ?? "",
							DataType = imported.DataType ?? "String",
							Category = imported.Category ?? "General",
							IsActive = true,
							CreatedAt = DateTime.Now,
							UpdatedBy = adminId
						});
						createdCount++;
					}
				}

				await _context.SaveChangesAsync();

				await _auditHelper.LogAsync(
					adminId,
					"IMPORT",
					"SystemSettings",
					null,
					null,
					null,
					$"Import thành công: {updatedCount} updated, {createdCount} created"
				);

				return Json(new
				{
					success = true,
					message = $"Import thành công! Updated: {updatedCount}, Created: {createdCount}",
					updatedCount,
					createdCount
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi import: {ex.Message}" });
			}
		}
		// ============================================
		// THÊM VÀO CUỐI SettingsController.cs (SAU ImportSettingModel)
		// ============================================

		// ============================================
		// GET LAYOUT FILE CONTENT
		// ============================================
		[HttpGet]
		public IActionResult GetLayoutContent(string layoutType)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			try
			{
				var fileName = layoutType == "admin" ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
				var filePath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

				if (!System.IO.File.Exists(filePath))
					return Json(new { success = false, message = $"File {fileName} không tồn tại!" });

				var content = System.IO.File.ReadAllText(filePath);

				return Json(new
				{
					success = true,
					content = content,
					fileName = fileName,
					filePath = filePath,
					lastModified = System.IO.File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss")
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi đọc file: {ex.Message}" });
			}
		}

		// ============================================
		// SAVE LAYOUT FILE
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> SaveLayoutFile([FromBody] SaveLayoutRequest request)
		{
			if (!IsAdmin())
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"LayoutFiles",
					"Không có quyền cập nhật layout",
					new { LayoutType = request.LayoutType }
				);

				return Json(new { success = false, message = "Không có quyền thực hiện!" });
			}

			try
			{
				var fileName = request.LayoutType == "admin" ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
				var filePath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

				// Backup file cũ trước khi ghi đè
				await BackupLayoutFile(filePath, fileName);

				// Ghi file mới
				await System.IO.File.WriteAllTextAsync(filePath, request.Content);

				await _auditHelper.LogDetailedAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"LayoutFiles",
					null,
					null,
					new { FileName = fileName },
					$"Cập nhật file layout: {fileName}",
					new Dictionary<string, object>
					{
				{ "FileName", fileName },
				{ "FileSize", request.Content.Length },
				{ "UpdatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
					}
				);

				return Json(new
				{
					success = true,
					message = $"Đã lưu {fileName} thành công! Vui lòng refresh trang để thấy thay đổi.",
					fileName = fileName
				});
			}
			catch (Exception ex)
			{
				await _auditHelper.LogFailedAttemptAsync(
					HttpContext.Session.GetInt32("UserId"),
					"UPDATE",
					"LayoutFiles",
					$"Exception: {ex.Message}",
					new { LayoutType = request.LayoutType, Error = ex.ToString() }
				);

				return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
			}
		}

		// ============================================
		// BACKUP LAYOUT FILE
		// ============================================
		private async System.Threading.Tasks.Task BackupLayoutFile(string filePath, string fileName)
		{
			if (System.IO.File.Exists(filePath))
			{
				var backupFolder = Path.Combine(_env.ContentRootPath, "Backups", "Layouts");
				if (!Directory.Exists(backupFolder))
					Directory.CreateDirectory(backupFolder);

				var backupFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}.cshtml";
				var backupPath = Path.Combine(backupFolder, backupFileName);

				var content = await System.IO.File.ReadAllTextAsync(filePath);
				await System.IO.File.WriteAllTextAsync(backupPath, content);
			}
		}

		// ============================================
		// LIST BACKUP FILES
		// ============================================
		[HttpGet]
		public IActionResult GetBackupFiles()
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền truy cập!" });

			try
			{
				var backupFolder = Path.Combine(_env.ContentRootPath, "Backups", "Layouts");

				if (!Directory.Exists(backupFolder))
					return Json(new { success = true, backups = new List<object>() });

				var files = Directory.GetFiles(backupFolder, "*.cshtml")
					.Select(f => new
					{
						fileName = Path.GetFileName(f),
						fullPath = f,
						size = new FileInfo(f).Length,
						created = System.IO.File.GetCreationTime(f).ToString("yyyy-MM-dd HH:mm:ss"),
						layoutType = Path.GetFileName(f).StartsWith("_Layout_") ? "admin" : "staff"
					})
					.OrderByDescending(f => f.created)
					.Take(20) // Chỉ lấy 20 backup gần nhất
					.ToList();

				return Json(new { success = true, backups = files });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
			}
		}

		// ============================================
		// RESTORE FROM BACKUP
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> RestoreFromBackup([FromBody] RestoreBackupRequest request)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền thực hiện!" });

			try
			{
				var backupPath = Path.Combine(_env.ContentRootPath, "Backups", "Layouts", request.BackupFileName);

				if (!System.IO.File.Exists(backupPath))
					return Json(new { success = false, message = "File backup không tồn tại!" });

				var fileName = request.BackupFileName.StartsWith("_Layout_") ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
				var targetPath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

				// Backup file hiện tại trước khi restore
				await BackupLayoutFile(targetPath, fileName);

				// Copy backup file về
				var content = await System.IO.File.ReadAllTextAsync(backupPath);
				await System.IO.File.WriteAllTextAsync(targetPath, content);

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"RESTORE",
					"LayoutFiles",
					null,
					null,
					null,
					$"Restore layout từ backup: {request.BackupFileName}"
				);

				return Json(new
				{
					success = true,
					message = $"Đã restore {fileName} từ backup thành công!"
				});
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi restore: {ex.Message}" });
			}
		}

		// ============================================
		// DELETE BACKUP FILE
		// ============================================
		[HttpPost]
		public async System.Threading.Tasks.Task<IActionResult> DeleteBackup([FromBody] DeleteBackupRequest request)
		{
			if (!IsAdmin())
				return Json(new { success = false, message = "Không có quyền xóa!" });

			try
			{
				var backupPath = Path.Combine(_env.ContentRootPath, "Backups", "Layouts", request.BackupFileName);

				if (!System.IO.File.Exists(backupPath))
					return Json(new { success = false, message = "File backup không tồn tại!" });

				System.IO.File.Delete(backupPath);

				await _auditHelper.LogAsync(
					HttpContext.Session.GetInt32("UserId"),
					"DELETE",
					"LayoutBackups",
					null,
					null,
					null,
					$"Xóa backup: {request.BackupFileName}"
				);

				return Json(new { success = true, message = "Đã xóa backup!" });
			}
			catch (Exception ex)
			{
				return Json(new { success = false, message = $"Lỗi xóa: {ex.Message}" });
			}
		}

		// ============================================
		// REQUEST MODELS FOR LAYOUT OPERATIONS
		// ============================================
		public class SaveLayoutRequest
		{
			public string LayoutType { get; set; } = string.Empty; // "admin" or "staff"
			public string Content { get; set; } = string.Empty;
		}

		public class RestoreBackupRequest
		{
			public string BackupFileName { get; set; } = string.Empty;
		}

		public class DeleteBackupRequest
		{
			public string BackupFileName { get; set; } = string.Empty;
		}
		public class ImportSettingModel
		{
			public string SettingKey { get; set; } = string.Empty;
			public string? SettingValue { get; set; }
			public string? Description { get; set; }
			public string? DataType { get; set; }
			public string? Category { get; set; }
		}
	}
}