using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TMD.Models;
using TMDSystem.Helpers;

namespace TMDSystem.Services
{
	public class AutoRejectRequestsService : BackgroundService
	{
		private readonly IServiceProvider _serviceProvider;
		private readonly ILogger<AutoRejectRequestsService> _logger;
		private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6); // Kiểm tra mỗi 6 giờ
		private const int MAX_PENDING_DAYS = 3;

		public AutoRejectRequestsService(
			IServiceProvider serviceProvider,
			ILogger<AutoRejectRequestsService> logger)
		{
			_serviceProvider = serviceProvider;
			_logger = logger;
		}

		// ✅ FIX: Sử dụng fully qualified name để tránh xung đột với TMD.Models.Task
		protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("Auto Reject Requests Service started");

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await ProcessAutoRejectRequests();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error occurred while auto-rejecting requests");
				}

				// Chờ đến lần kiểm tra tiếp theo
				await System.Threading.Tasks.Task.Delay(_checkInterval, stoppingToken);
			}

			_logger.LogInformation("Auto Reject Requests Service stopped");
		}

		// ✅ FIX: Sử dụng fully qualified name
		private async System.Threading.Tasks.Task ProcessAutoRejectRequests()
		{
			using var scope = _serviceProvider.CreateScope();
			var context = scope.ServiceProvider.GetRequiredService<TmdContext>();
			var auditHelper = scope.ServiceProvider.GetRequiredService<AuditHelper>();

			var cutoffDate = DateTime.Now.AddDays(-MAX_PENDING_DAYS);
			int totalRejected = 0;

			// ============================================
			// AUTO-REJECT OVERTIME REQUESTS
			// ============================================
			var expiredOvertimeRequests = await context.OvertimeRequests
				.Where(r => r.Status == "Pending" &&
						   r.CreatedAt.HasValue &&
						   r.CreatedAt.Value <= cutoffDate)
				.ToListAsync();

			foreach (var request in expiredOvertimeRequests)
			{
				var oldStatus = request.Status;
				request.Status = "Rejected";
				request.ReviewedBy = null; // System auto-reject
				request.ReviewedAt = DateTime.Now;
				request.ReviewNote = $"Tự động từ chối do quá {MAX_PENDING_DAYS} ngày không được xử lý";
				request.UpdatedAt = DateTime.Now;

				// Cập nhật Attendance nếu có
				var attendance = await context.Attendances
					.FirstOrDefaultAsync(a => a.UserId == request.UserId &&
											a.WorkDate == request.WorkDate);

				if (attendance != null)
				{
					attendance.IsOvertimeApproved = false;
					attendance.ApprovedOvertimeHours = 0;
					attendance.HasOvertimeRequest = true;
					attendance.OvertimeRequestId = request.OvertimeRequestId;
					attendance.UpdatedAt = DateTime.Now;
				}

				await auditHelper.LogDetailedAsync(
					null, // System action
					"AUTO_REJECT",
					"OvertimeRequest",
					request.OvertimeRequestId,
					new { Status = oldStatus },
					new { Status = "Rejected" },
					$"Tự động từ chối overtime request #{request.OvertimeRequestId} - Quá {MAX_PENDING_DAYS} ngày",
					new Dictionary<string, object>
					{
						{ "UserId", request.UserId },
						{ "WorkDate", request.WorkDate.ToString("dd/MM/yyyy") },
						{ "OvertimeHours", request.OvertimeHours },
						{ "DaysExpired", (DateTime.Now - request.CreatedAt.Value).Days }
					}
				);

				totalRejected++;
			}

			// ============================================
			// AUTO-REJECT LEAVE REQUESTS
			// ============================================
			var expiredLeaveRequests = await context.LeaveRequests
				.Where(r => r.Status == "Pending" &&
						   r.CreatedAt.HasValue &&
						   r.CreatedAt.Value <= cutoffDate)
				.ToListAsync();

			foreach (var request in expiredLeaveRequests)
			{
				var oldStatus = request.Status;
				request.Status = "Rejected";
				request.ReviewedBy = null;
				request.ReviewedAt = DateTime.Now;
				request.ReviewNote = $"Tự động từ chối do quá {MAX_PENDING_DAYS} ngày không được xử lý";
				request.UpdatedAt = DateTime.Now;

				// Lấy config nghỉ không lương
				var unpaidMultiplierConfig = await context.SystemSettings
					.FirstOrDefaultAsync(c => c.SettingKey == "LEAVE_UNPAID_MULTIPLIER" && c.IsActive == true);

				var unpaidMultiplier = unpaidMultiplierConfig != null
					? decimal.Parse(unpaidMultiplierConfig.SettingValue) / 100m
					: 0m;

				// Áp dụng multiplier cho các ngày nghỉ
				for (var date = request.StartDate; date <= request.EndDate; date = date.AddDays(1))
				{
					var attendance = await context.Attendances
						.FirstOrDefaultAsync(a => a.UserId == request.UserId && a.WorkDate == date);

					if (attendance != null)
					{
						attendance.SalaryMultiplier = unpaidMultiplier;
						attendance.UpdatedAt = DateTime.Now;
					}
				}

				await auditHelper.LogDetailedAsync(
					null,
					"AUTO_REJECT",
					"LeaveRequest",
					request.LeaveRequestId,
					new { Status = oldStatus },
					new { Status = "Rejected" },
					$"Tự động từ chối leave request #{request.LeaveRequestId} - Quá {MAX_PENDING_DAYS} ngày",
					new Dictionary<string, object>
					{
						{ "UserId", request.UserId },
						{ "LeaveType", request.LeaveType ?? "N/A" },
						{ "TotalDays", request.TotalDays },
						{ "DaysExpired", (DateTime.Now - request.CreatedAt.Value).Days }
					}
				);

				totalRejected++;
			}

			// ============================================
			// AUTO-REJECT LATE REQUESTS
			// ============================================
			var expiredLateRequests = await context.LateRequests
				.Where(r => r.Status == "Pending" &&
						   r.CreatedAt.HasValue &&
						   r.CreatedAt.Value <= cutoffDate)
				.ToListAsync();

			foreach (var request in expiredLateRequests)
			{
				var oldStatus = request.Status;
				request.Status = "Rejected";
				request.ReviewedBy = null;
				request.ReviewedAt = DateTime.Now;
				request.ReviewNote = $"Tự động từ chối do quá {MAX_PENDING_DAYS} ngày không được xử lý";
				request.UpdatedAt = DateTime.Now;

				// Cập nhật Attendance
				var attendance = await context.Attendances
					.FirstOrDefaultAsync(a => a.UserId == request.UserId &&
											a.WorkDate == request.RequestDate);

				if (attendance != null)
				{
					attendance.HasLateRequest = true;
					attendance.LateRequestId = request.LateRequestId;
					attendance.UpdatedAt = DateTime.Now;
					// Giữ nguyên DeductionHours khi từ chối
				}

				await auditHelper.LogDetailedAsync(
					null,
					"AUTO_REJECT",
					"LateRequest",
					request.LateRequestId,
					new { Status = oldStatus },
					new { Status = "Rejected" },
					$"Tự động từ chối late request #{request.LateRequestId} - Quá {MAX_PENDING_DAYS} ngày",
					new Dictionary<string, object>
					{
						{ "UserId", request.UserId },
						{ "RequestDate", request.RequestDate.ToString("dd/MM/yyyy") },
						{ "ExpectedArrivalTime", request.ExpectedArrivalTime.ToString("HH:mm") },
						{ "DaysExpired", (DateTime.Now - request.CreatedAt.Value).Days }
					}
				);

				totalRejected++;
			}

			// Save all changes
			if (totalRejected > 0)
			{
				await context.SaveChangesAsync();
				_logger.LogInformation($"Auto-rejected {totalRejected} expired requests (older than {MAX_PENDING_DAYS} days)");
			}
			else
			{
				_logger.LogInformation("No expired requests found for auto-rejection");
			}
		}
	}
}