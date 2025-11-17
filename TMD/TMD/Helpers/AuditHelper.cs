using TMD.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace TMDSystem.Helpers
{
	public class AuditHelper
	{
		private readonly TmdContext _context;
		private readonly IHttpContextAccessor _httpContextAccessor;

		public AuditHelper(TmdContext context, IHttpContextAccessor httpContextAccessor)
		{
			_context = context;
			_httpContextAccessor = httpContextAccessor;
		}

		/// <summary>
		/// Ghi log hành động vào database
		/// </summary>
		public async System.Threading.Tasks.Task LogAsync(
			int? userId,
			string action,
			string entityName,
			int? entityId,
			object? oldValue,
			object? newValue,
			string description)
		{
			try
			{
				var jsonOptions = new JsonSerializerOptions
				{
					WriteIndented = true,
					Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				};

				var log = new AuditLog
				{
					UserId = userId,
					Action = action,
					EntityName = entityName,
					EntityId = entityId,
					OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue, jsonOptions) : null,
					NewValue = newValue != null ? JsonSerializer.Serialize(newValue, jsonOptions) : null,
					Description = description,
					Timestamp = DateTime.Now,
					Ipaddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
					UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString()
				};

				_context.AuditLogs.Add(log);
				await _context.SaveChangesAsync();
			}
			catch (Exception ex)
			{
				// Log error nhưng không throw để không ảnh hưởng đến business logic
				Console.WriteLine($"[AuditHelper Error]: {ex.Message}");
				Console.WriteLine($"[AuditHelper Stack]: {ex.StackTrace}");
			}
		}

		/// <summary>
		/// Log hành động với thông tin chi tiết hơn (bao gồm cả metadata bổ sung)
		/// </summary>
		public async System.Threading.Tasks.Task LogDetailedAsync(
			int? userId,
			string action,
			string entityName,
			int? entityId,
			object? oldValue,
			object? newValue,
			string description,
			Dictionary<string, object>? additionalData = null)
		{
			try
			{
				var context = _httpContextAccessor.HttpContext;
				var userAgent = context?.Request.Headers["User-Agent"].ToString() ?? "Unknown";
				var ip = context?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

				// Tạo mô tả chi tiết hơn
				var detailedDescription = description;
				if (additionalData != null && additionalData.Count > 0)
				{
					detailedDescription += "\n--- Chi tiết bổ sung ---\n";
					foreach (var item in additionalData)
					{
						detailedDescription += $"{item.Key}: {item.Value}\n";
					}
				}

				var jsonOptions = new JsonSerializerOptions
				{
					WriteIndented = true,
					Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				};

				var log = new AuditLog
				{
					UserId = userId,
					Action = action,
					EntityName = entityName,
					EntityId = entityId,
					OldValue = oldValue != null ? JsonSerializer.Serialize(oldValue, jsonOptions) : null,
					NewValue = newValue != null ? JsonSerializer.Serialize(newValue, jsonOptions) : null,
					Description = detailedDescription,
					Timestamp = DateTime.Now,
					Ipaddress = ip,
					UserAgent = userAgent
				};

				_context.AuditLogs.Add(log);
				await _context.SaveChangesAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[AuditHelper Error]: {ex.Message}");
				Console.WriteLine($"[AuditHelper Stack]: {ex.StackTrace}");
			}
		}

		/// <summary>
		/// Log các hành động không thành công (failed attempts)
		/// </summary>
		public async System.Threading.Tasks.Task LogFailedAttemptAsync(
			int? userId,
			string action,
			string entityName,
			string reason,
			object? attemptData = null)
		{
			await LogAsync(
				userId,
				$"{action}_FAILED",
				entityName,
				null,
				attemptData,
				null,
				$"Thất bại: {reason}"
			);
		}

		/// <summary>
		/// Log view/access actions (xem dữ liệu nhạy cảm)
		/// </summary>
		public async System.Threading.Tasks.Task LogViewAsync(
			int userId,
			string entityName,
			int entityId,
			string description)
		{
			await LogAsync(
				userId,
				"VIEW",
				entityName,
				entityId,
				null,
				null,
				$"Xem thông tin: {description}"
			);
		}
	}
}