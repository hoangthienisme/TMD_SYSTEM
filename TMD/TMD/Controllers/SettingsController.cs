	using Microsoft.AspNetCore.Mvc;
	using Microsoft.EntityFrameworkCore;
	using TMDSystem.Helpers;
	using TMD.Models;
	using Task = System.Threading.Tasks.Task;
	using System.Text.RegularExpressions;

	namespace TMD.Controllers
	{
		public class SettingsController : Controller
		{
			private readonly TmdContext _context;
			private readonly AuditHelper _auditHelper;
			private readonly IWebHostEnvironment _env;
			private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
			public async Task<IActionResult> Index()
			{
				if (!IsAdmin())
					return RedirectToAction("Login", "Account");

				var settings = await _context.SystemSettings
					.Where(s => s.IsActive == true)
					.OrderBy(s => s.Category)
					.ThenBy(s => s.SettingKey)
					.ToListAsync();

				if (settings.Count == 0)
				{
					await InitializeDefaultSettings();
					settings = await _context.SystemSettings.ToListAsync();
				}

				await _auditHelper.LogViewAsync(
					HttpContext.Session.GetInt32("UserId")!.Value,
					"SystemSettings",
					0,
					"Xem trang cấu hình hệ thống"
				);

				return View(settings);
			}


			// ============================================
			// 🆕 GENERATE KEY WITH PREFIX BASED ON APPLY METHOD
			// ============================================
			[HttpPost]
			public IActionResult GenerateKey([FromBody] GenerateKeyRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				if (string.IsNullOrWhiteSpace(request.Description))
					return Json(new { success = false, message = "Mô tả không được trống!" });

				try
				{
					var baseKey = GenerateSettingKeyFromDescription(request.Description);
					var prefix = GetPrefixForApplyMethod(request.ApplyMethod);
					var generatedKey = string.IsNullOrEmpty(prefix) ? baseKey : $"{prefix}_{baseKey}";

					var exists = _context.SystemSettings.Any(s => s.SettingKey == generatedKey);

					if (exists)
					{
						int counter = 2;
						string uniqueKey;
						do
						{
							uniqueKey = $"{generatedKey}_{counter}";
							counter++;
						} while (_context.SystemSettings.Any(s => s.SettingKey == uniqueKey));

						generatedKey = uniqueKey;
					}

					return Json(new
					{
						success = true,
						key = generatedKey,
						exists = false,
						message = "Key được tạo thành công"
					});
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = ex.Message });
				}
			}

			private string GetPrefixForApplyMethod(string? applyMethod)
			{
				return applyMethod switch
				{
					"Add" => "ALLOWANCE",
					"Percentage" => "BONUS_PERCENT",
					"Multiply" => "BONUS_MULTIPLIER",
					"Deduct" => "DEDUCTION",
					_ => ""
				};
			}

			// ============================================
			// TOGGLE ENABLE/DISABLE SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> ToggleSettingStatus([FromBody] ToggleSettingStatusRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				var setting = await _context.SystemSettings
					.FirstOrDefaultAsync(s => s.SettingId == request.SettingId);

				if (setting == null)
					return Json(new { success = false, message = "Không tìm thấy setting!" });

				try
				{
					var adminId = HttpContext.Session.GetInt32("UserId");
					var oldStatus = setting.IsEnabled;

					setting.IsEnabled = !setting.IsEnabled;
					setting.UpdatedAt = DateTime.Now;
					setting.UpdatedBy = adminId;

					await _context.SaveChangesAsync();

					await _auditHelper.LogAsync(
						adminId,
						"UPDATE",
						"SystemSettings",
						setting.SettingId,
						new { IsEnabled = oldStatus },
						new { IsEnabled = setting.IsEnabled },
						$"Thay đổi trạng thái setting: {setting.SettingKey}"
					);

					return Json(new
					{
						success = true,
						message = $"Đã {(setting.IsEnabled ? "bật" : "tắt")} setting '{setting.SettingKey}'",
						isEnabled = setting.IsEnabled
					});
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// GET SETTING BY KEY
			// ============================================
			[HttpGet]
			public async Task<IActionResult> GetSetting(string key)
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
						setting.IsEnabled,
						setting.ApplyMethod,
						setting.UpdatedAt
					}
				});
			}

			// ============================================
			// UPDATE SINGLE SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> UpdateSetting([FromBody] UpdateSettingRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền thực hiện!" });

				var setting = await _context.SystemSettings
					.FirstOrDefaultAsync(s => s.SettingKey == request.SettingKey);

				if (setting == null)
					return Json(new { success = false, message = "Không tìm thấy cấu hình!" });

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
							{ "NewValue", request.SettingValue ?? "null" }
						}
					);

					return Json(new { success = true, message = "Cập nhật cấu hình thành công!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
				}
			}

			// ============================================
			// BATCH UPDATE SETTINGS
			// ============================================
			[HttpPost]
			public async Task<IActionResult> BatchUpdateSettings([FromBody] List<UpdateSettingRequest> requests)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền thực hiện!" });

				if (requests == null || requests.Count == 0)
					return Json(new { success = false, message = "Không có dữ liệu để cập nhật!" });

				try
				{
					var adminId = HttpContext.Session.GetInt32("UserId");
					var updatedCount = 0;
					var createdCount = 0;

					foreach (var request in requests)
					{
						var setting = await _context.SystemSettings
							.FirstOrDefaultAsync(s => s.SettingKey == request.SettingKey);

						if (setting == null)
						{
							setting = new SystemSetting
							{
								SettingKey = request.SettingKey,
								SettingValue = request.SettingValue,
								Description = request.Description ?? GetDescriptionForKey(request.SettingKey),
								DataType = request.DataType ?? GetDataTypeForKey(request.SettingKey),
								Category = request.Category ?? GetCategoryForKey(request.SettingKey),
								ApplyMethod = request.ApplyMethod ?? "Add",
								IsActive = true,
								IsEnabled = true,
								CreatedAt = DateTime.Now,
								UpdatedBy = adminId
							};

							_context.SystemSettings.Add(setting);
							createdCount++;
						}
						else
						{
							setting.SettingValue = request.SettingValue;
							setting.UpdatedAt = DateTime.Now;
							setting.UpdatedBy = adminId;
							updatedCount++;
						}
					}

					await _context.SaveChangesAsync();

					return Json(new
					{
						success = true,
						message = $"Đã cập nhật {updatedCount}/{requests.Count} cấu hình!",
						updatedCount,
						createdCount
					});
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
				}
			}

			// ============================================
			// INITIALIZE DEFAULT SETTINGS
			// ============================================
			private async Task InitializeDefaultSettings()
			{
				var defaultSettings = new List<SystemSetting>
				{
					new() {
						SettingKey = "BASE_SALARY",
						SettingValue = "5000000",
						Description = "Lương cơ bản mặc định (VNĐ)",
						DataType = "Decimal",
						Category = "Salary",
						ApplyMethod = "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					},
					new() {
						SettingKey = "OVERTIME_RATE",
						SettingValue = "1.5",
						Description = "Hệ số lương tăng ca",
						DataType = "Decimal",
						Category = "Salary",
						ApplyMethod = "Multiply",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					},
					new() {
						SettingKey = "WORK_DAYS_PER_MONTH",
						SettingValue = "26",
						Description = "Số ngày làm việc/tháng",
						DataType = "Number",
						Category = "Salary",
						ApplyMethod = "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					},
					new() {
						SettingKey = "STANDARD_HOURS_PER_DAY",
						SettingValue = "8",
						Description = "Số giờ làm chuẩn/ngày",
						DataType = "Decimal",
						Category = "Salary",
						ApplyMethod = "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					},
					new() {
						SettingKey = "CHECK_IN_STANDARD_TIME",
						SettingValue = "08:00",
						Description = "Giờ chuẩn check-in",
						DataType = "String",
						Category = "Attendance",
						ApplyMethod = "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					},
					new() {
						SettingKey = "LATE_THRESHOLD_MINUTES",
						SettingValue = "15",
						Description = "Ngưỡng phút đi trễ",
						DataType = "Number",
						Category = "Attendance",
						ApplyMethod = "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now
					}
				};

				_context.SystemSettings.AddRange(defaultSettings);
				await _context.SaveChangesAsync();
			}

			private string GenerateSettingKeyFromDescription(string description)
			{
				var key = Regex.Replace(description, @"[^a-zA-Z0-9\s]", " ");
				key = Regex.Replace(key, @"\s+", "_");
				key = key.ToUpper();
				return key[..Math.Min(50, key.Length)];
			}

			private static string GetDescriptionForKey(string key)
			{
				return "Cấu hình tự động";
			}

			private static string GetDataTypeForKey(string key)
			{
				if (key.Contains("ALLOWANCE") || key.Contains("SALARY") || key.Contains("RATE"))
					return "Decimal";
				if (key.Contains("DAYS") || key.Contains("MINUTES"))
					return "Number";
				return "String";
			}

			private static string GetCategoryForKey(string key)
			{
				if (key.Contains("ALLOWANCE") || key.Contains("BONUS") || key.Contains("SALARY"))
					return "Salary";
				if (key.Contains("LATE") || key.Contains("CHECK"))
					return "Attendance";
				return "General";
			}

			// ============================================
			// CREATE SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> CreateSetting([FromBody] CreateSettingRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền tạo mới!" });

				try
				{
					var settingKey = request.SettingKey;
					if (string.IsNullOrEmpty(settingKey) && !string.IsNullOrEmpty(request.Description))
					{
						settingKey = GenerateSettingKeyFromDescription(request.Description);
						var prefix = GetPrefixForApplyMethod(request.ApplyMethod);
						if (!string.IsNullOrEmpty(prefix))
							settingKey = $"{prefix}_{settingKey}";
					}

					var existingSetting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingKey == settingKey);

					if (existingSetting != null)
						return Json(new { success = false, message = "SettingKey đã tồn tại!" });

					var newSetting = new SystemSetting
					{
						SettingKey = settingKey ?? "",
						SettingValue = request.SettingValue,
						Description = request.Description,
						DataType = request.DataType ?? "String",
						Category = request.Category ?? "General",
						ApplyMethod = request.ApplyMethod ?? "Add",
						IsActive = true,
						IsEnabled = true,
						CreatedAt = DateTime.Now,
						UpdatedBy = HttpContext.Session.GetInt32("UserId")
					};

					_context.SystemSettings.Add(newSetting);
					await _context.SaveChangesAsync();

					await _auditHelper.LogAsync(
						HttpContext.Session.GetInt32("UserId"),
						"CREATE",
						"SystemSettings",
						newSetting.SettingId,
						null,
						new { newSetting.SettingKey, newSetting.ApplyMethod },
						$"Tạo mới setting: {newSetting.SettingKey}"
					);

					return Json(new
					{
						success = true,
						message = "Tạo mới setting thành công!",
						setting = new
						{
							newSetting.SettingId,
							newSetting.SettingKey,
							newSetting.SettingValue,
							newSetting.ApplyMethod
						}
					});
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// EDIT SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> EditSetting([FromBody] EditSettingRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền chỉnh sửa!" });

				try
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingId == request.SettingId);

					if (setting == null)
						return Json(new { success = false, message = "Không tìm thấy setting!" });

					if (setting.SettingKey != request.SettingKey)
					{
						var existingKey = await _context.SystemSettings
							.AnyAsync(s => s.SettingKey == request.SettingKey && s.SettingId != request.SettingId);

						if (existingKey)
							return Json(new { success = false, message = "SettingKey đã tồn tại!" });
					}

					setting.SettingKey = request.SettingKey;
					setting.SettingValue = request.SettingValue;
					setting.Description = request.Description;
					setting.DataType = request.DataType;
					setting.Category = request.Category;
					setting.ApplyMethod = request.ApplyMethod;
					setting.UpdatedAt = DateTime.Now;
					setting.UpdatedBy = HttpContext.Session.GetInt32("UserId");

					await _context.SaveChangesAsync();

					return Json(new { success = true, message = "Cập nhật thành công!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// MOVE TO TRASH
			// ============================================
			[HttpPost]
			public async Task<IActionResult> MoveToTrash(int id)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền xóa!" });

				try
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingId == id);

					if (setting == null)
						return Json(new { success = false, message = "Không tìm thấy setting!" });

					setting.IsActive = false;
					setting.UpdatedAt = DateTime.Now;
					setting.UpdatedBy = HttpContext.Session.GetInt32("UserId");

					await _context.SaveChangesAsync();

					return Json(new { success = true, message = "Đã chuyển vào thùng rác!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// RESTORE SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> RestoreSetting(int id)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền khôi phục!" });

				try
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingId == id);

					if (setting == null)
						return Json(new { success = false, message = "Không tìm thấy setting!" });

					setting.IsActive = true;
					setting.UpdatedAt = DateTime.Now;
					setting.UpdatedBy = HttpContext.Session.GetInt32("UserId");

					await _context.SaveChangesAsync();

					return Json(new { success = true, message = "Đã khôi phục!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// PERMANENT DELETE
			// ============================================
			[HttpPost]
			public async Task<IActionResult> PermanentDelete(int id)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền xóa vĩnh viễn!" });

				try
				{
					var setting = await _context.SystemSettings
						.FirstOrDefaultAsync(s => s.SettingId == id);

					if (setting == null)
						return Json(new { success = false, message = "Không tìm thấy setting!" });

					_context.SystemSettings.Remove(setting);
					await _context.SaveChangesAsync();

					return Json(new { success = true, message = "Đã xóa vĩnh viễn!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// GET TRASH LIST
			// ============================================
			[HttpGet]
			public async Task<IActionResult> GetTrashList()
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền truy cập!" });

				try
				{
					var trashedSettings = await _context.SystemSettings
						.Where(s => s.IsActive == false)
						.OrderByDescending(s => s.UpdatedAt)
						.Select(s => new
						{
							s.SettingId,
							s.SettingKey,
							s.SettingValue,
							s.Description,
							s.Category,
							s.ApplyMethod
						})
						.ToListAsync();

					return Json(new { success = true, data = trashedSettings });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// STATIC HELPERS
			// ============================================
			public static string GetSettingValue(TmdContext context, string key)
			{
				var setting = context.SystemSettings
					.FirstOrDefault(s => s.SettingKey == key && s.IsActive == true && s.IsEnabled);
				return setting?.SettingValue ?? string.Empty;
			}


			public static string GetCustomStyles(TmdContext context) => "";
			public static string GetCustomScripts(TmdContext context) => "";

			// ============================================
			// GET ALL SETTINGS
			// ============================================
			[HttpGet]
			public async Task<IActionResult> GetAllSettings()
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
						s.ApplyMethod,
						s.IsEnabled
					})
					.ToListAsync();

				return Json(new { success = true, settings });
			}



			// ============================================
			// DELETE SETTING
			// ============================================
			[HttpPost]
			public async Task<IActionResult> DeleteSetting(string key)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền xóa!" });

				var setting = await _context.SystemSettings
					.FirstOrDefaultAsync(s => s.SettingKey == key);

				if (setting == null)
					return Json(new { success = false, message = "Không tìm thấy!" });

				try
				{
					setting.IsActive = false;
					setting.UpdatedAt = DateTime.Now;
					setting.UpdatedBy = HttpContext.Session.GetInt32("UserId");

					await _context.SaveChangesAsync();

					return Json(new { success = true, message = "Đã xóa!" });
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
			public async Task<IActionResult> ResetToDefault()
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền reset!" });

				try
				{
					var currentSettings = await _context.SystemSettings.ToListAsync();
					_context.SystemSettings.RemoveRange(currentSettings);
					await _context.SaveChangesAsync();

					await InitializeDefaultSettings();

					return Json(new { success = true, message = "Đã reset về mặc định!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// EXPORT SETTINGS
			// ============================================
			[HttpGet]
			public async Task<IActionResult> ExportSettings()
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
						s.Category,
						s.ApplyMethod,
						s.IsEnabled
					})
					.ToListAsync();

				var json = System.Text.Json.JsonSerializer.Serialize(settings, JsonOptions);
				var bytes = System.Text.Encoding.UTF8.GetBytes(json);
				var fileName = $"settings_{DateTime.Now:yyyyMMddHHmmss}.json";

				return File(bytes, "application/json", fileName);
			}
			// ============================================
			// IMPORT SETTINGS
			// ============================================
			[HttpPost]
			public async Task<IActionResult> ImportSettings(IFormFile file)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền import!" });

				if (file == null || !file.FileName.EndsWith(".json"))
					return Json(new { success = false, message = "File không hợp lệ!" });

				try
				{
					using var reader = new StreamReader(file.OpenReadStream());
					var jsonContent = await reader.ReadToEndAsync();

					var importedSettings = System.Text.Json.JsonSerializer.Deserialize<List<ImportSettingModel>>(jsonContent);

					if (importedSettings == null)
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
								ApplyMethod = imported.ApplyMethod ?? "Add",
								IsActive = true,
								IsEnabled = true,
								CreatedAt = DateTime.Now,
								UpdatedBy = adminId
							});
							createdCount++;
						}
					}

					await _context.SaveChangesAsync();

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
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// LAYOUT EDITOR
			// ============================================
			[HttpGet]
			public IActionResult GetLayoutContent(string layoutType)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				try
				{
					var fileName = layoutType == "admin" ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
					var filePath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

					if (!System.IO.File.Exists(filePath))
						return Json(new { success = false, message = "File không tồn tại!" });

					var content = System.IO.File.ReadAllText(filePath);

					return Json(new
					{
						success = true,
						content,
						fileName,
						lastModified = System.IO.File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss")
					});
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			[HttpPost]
			public async Task<IActionResult> SaveLayoutFile([FromBody] SaveLayoutRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				try
				{
					var fileName = request.LayoutType == "admin" ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
					var filePath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

					await BackupLayoutFile(filePath, fileName);
					await System.IO.File.WriteAllTextAsync(filePath, request.Content);

					await _auditHelper.LogDetailedAsync(
						HttpContext.Session.GetInt32("UserId"),
						"UPDATE",
						"LayoutFiles",
						null,
						null,
						new { FileName = fileName },
						$"Cập nhật layout: {fileName}",
						new Dictionary<string, object> { { "FileName", fileName } }
					);

					return Json(new { success = true, message = $"Đã lưu {fileName}!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			private async Task BackupLayoutFile(string filePath, string fileName)
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

			[HttpGet]
			public IActionResult GetBackupFiles()
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				try
				{
					var backupFolder = Path.Combine(_env.ContentRootPath, "Backups", "Layouts");

					if (!Directory.Exists(backupFolder))
						return Json(new { success = true, backups = new List<object>() });

					var files = Directory.GetFiles(backupFolder, "*.cshtml")
						.Select(f => new
						{
							fileName = Path.GetFileName(f),
							size = new FileInfo(f).Length,
							created = System.IO.File.GetCreationTime(f).ToString("yyyy-MM-dd HH:mm:ss")
						})
						.OrderByDescending(f => f.created)
						.Take(20)
						.ToList();

					return Json(new { success = true, backups = files });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			[HttpPost]
			public async Task<IActionResult> RestoreFromBackup([FromBody] RestoreBackupRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				try
				{
					var backupPath = Path.Combine(_env.ContentRootPath, "Backups", "Layouts", request.BackupFileName);

					if (!System.IO.File.Exists(backupPath))
						return Json(new { success = false, message = "File không tồn tại!" });

					var fileName = request.BackupFileName.StartsWith("_Layout_") ? "_Layout.cshtml" : "_LayoutStaff.cshtml";
					var targetPath = Path.Combine(_env.ContentRootPath, "Views", "Shared", fileName);

					await BackupLayoutFile(targetPath, fileName);

					var content = await System.IO.File.ReadAllTextAsync(backupPath);
					await System.IO.File.WriteAllTextAsync(targetPath, content);

					return Json(new { success = true, message = $"Đã restore {fileName}!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			[HttpPost]
			public async Task<IActionResult> DeleteBackup([FromBody] DeleteBackupRequest request)
			{
				if (!IsAdmin())
					return Json(new { success = false, message = "Không có quyền!" });

				try
				{
					var backupPath = Path.Combine(_env.ContentRootPath, "Backups", "Layouts", request.BackupFileName);

					if (!System.IO.File.Exists(backupPath))
						return Json(new { success = false, message = "File không tồn tại!" });

					System.IO.File.Delete(backupPath);

					return Json(new { success = true, message = "Đã xóa backup!" });
				}
				catch (Exception ex)
				{
					return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
				}
			}

			// ============================================
			// REQUEST MODELS
			// ============================================
			public class GenerateKeyRequest
			{
				public string Description { get; set; }
				public string? ApplyMethod { get; set; }
			}

			public class ToggleSettingStatusRequest
			{
				public int SettingId { get; set; }
			}

			public class UpdateSettingRequest
			{
				public required string SettingKey { get; set; }
				public string? SettingValue { get; set; }
				public string? Description { get; set; }
				public string? DataType { get; set; }
				public string? Category { get; set; }
				public string? ApplyMethod { get; set; }
			}

			public class CreateSettingRequest
			{
				public string? SettingKey { get; set; }
				public string? SettingValue { get; set; }
				public string? Description { get; set; }
				public string? DataType { get; set; }
				public string? Category { get; set; }
				public string? ApplyMethod { get; set; }
			}

			public class EditSettingRequest
			{
				public int SettingId { get; set; }
				public required string SettingKey { get; set; }
				public string? SettingValue { get; set; }
				public string? Description { get; set; }
				public string? DataType { get; set; }
				public string? Category { get; set; }
				public string? ApplyMethod { get; set; }
			}
			public class ReviewRequestViewModel
			{
				public string RequestType { get; set; } = string.Empty;
				public int RequestId { get; set; }
				public string Action { get; set; } = string.Empty;
				public string? Note { get; set; }
			}
			public class SaveLayoutRequest
			{
				public required string LayoutType { get; set; }
				public required string Content { get; set; }
			}

			public class RestoreBackupRequest
			{
				public required string BackupFileName { get; set; }
			}

			public class DeleteBackupRequest
			{
				public required string BackupFileName { get; set; }
			}

			public class ImportSettingModel
			{
				public required string SettingKey { get; set; }
				public string? SettingValue { get; set; }
				public string? Description { get; set; }
				public string? DataType { get; set; }
				public string? Category { get; set; }
				public string? ApplyMethod { get; set; }
			}
		}
	}