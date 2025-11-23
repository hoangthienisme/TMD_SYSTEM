using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMD.Models;

namespace TMDSystem.Services
{
	public class AttendancePolicyService
	{
		private readonly TmdContext _context;

		public AttendancePolicyService(TmdContext context)
		{
			_context = context;
		}

		// Evaluate late: returns tuple (level, lateMinutes, deductionMinutes, deductionAmount)
		public (string level, int lateMinutes, decimal deductionMinutes, decimal deductionAmount) EvaluateLate(
			int userId,
			DateTime workDate,
			DateTime? actualCheckIn,
			TimeSpan standardCheckIn)
		{
			if (actualCheckIn == null) return ("None", 0, 0m, 0m);

			var expected = workDate.Date + standardCheckIn;
			var lateMinutes = (int)Math.Max(0, (actualCheckIn.Value - expected).TotalMinutes);
			if (lateMinutes == 0) return ("OnTime", 0, 0m, 0m);

			int t1 = int.TryParse(SettingsGetter("LATE_THRESHOLD_MINUTES_SHORT"), out var v1) ? v1 : 15;
			int t2 = int.TryParse(SettingsGetter("LATE_THRESHOLD_MINUTES_MEDIUM"), out var v2) ? v2 : 60;
			int t3 = int.TryParse(SettingsGetter("LATE_THRESHOLD_MINUTES_LONG"), out var v3) ? v3 : 120;

			string level = lateMinutes <= t1 ? "SHORT" : lateMinutes <= t2 ? "MEDIUM" : lateMinutes <= t3 ? "LONG" : "EXTREME";

			// permit check
			bool allowWithPermit = bool.TryParse(SettingsGetter("LATE_ALLOW_WITH_PERMIT"), out var awp) && awp;
			if (allowWithPermit)
			{
				int window = int.TryParse(SettingsGetter("LATE_PERMIT_VALID_WINDOW_DAYS"), out var w) ? w : 7;
				var permit = _context.LateRequests
					.Where(l => l.UserId == userId && l.Status == "Approved"
						&& l.RequestDate.ToDateTime(TimeOnly.MinValue) >= workDate.AddDays(-window)
						&& l.RequestDate.ToDateTime(TimeOnly.MinValue) <= workDate)
					.OrderByDescending(l => l.RequestDate)
					.FirstOrDefault();
				if (permit != null) return (level, lateMinutes, 0m, 0m);
			}

			var penaltyType = (SettingsGetter("LATE_PENALTY_TYPE") ?? "deduct").ToLower();
			var penaltyValueString = SettingsGetter("LATE_PENALTY_VALUE");
			decimal deductionMinutes = 0m;
			decimal deductionAmount = 0m;

			if (penaltyType == "none")
			{
				deductionMinutes = 0m;
			}
			else if (penaltyType == "deduct")
			{
				if (decimal.TryParse(penaltyValueString, out var pv)) deductionMinutes = Math.Min(pv, lateMinutes);
				else deductionMinutes = lateMinutes;
			}
			else if (penaltyType == "percentage")
			{
				if (decimal.TryParse(penaltyValueString, out var pct)) deductionMinutes = Math.Round(lateMinutes * pct / 100m, 2);
			}
			else if (penaltyType == "fine")
			{
				if (decimal.TryParse(penaltyValueString, out var fine)) deductionAmount = fine;
			}

			if (deductionMinutes > 0)
			{
				var salary = _context.UserSalarySettings.FirstOrDefault(u => u.UserId == userId && u.IsActive == true);
				var baseSalary = salary?.BaseSalary ?? 5000000m;
				var hourly = (baseSalary / 26m) / 8m;
				deductionAmount = Math.Round(deductionMinutes / 60m * hourly, 0);
			}

			return (level, lateMinutes, deductionMinutes, deductionAmount);
		}

		// Is leave paid according to settings and override rules
		public bool IsLeavePaid(string leaveType, int daysRequested)
		{
			var paidJson = SettingsGetter("LEAVE_ALLOW_PAID_TYPES");
			var allowed = ParseJson<List<string>>(paidJson) ?? new List<string>();
			if (allowed.Any(x => string.Equals(x, leaveType, StringComparison.OrdinalIgnoreCase))) return true;

			var overridesJson = SettingsGetter("LEAVE_OVERRIDDEN_PAY_RULES");
			var overrides = ParseJson<List<LeaveOverride>>(overridesJson) ?? new List<LeaveOverride>();
			var ov = overrides.FirstOrDefault(o => string.Equals(o.type, leaveType, StringComparison.OrdinalIgnoreCase));
			if (ov != null)
			{
				if (ov.pay == "full") return true;
				if (ov.pay == "partial" && ov.days >= daysRequested) return true;
			}

			return false;
		}

		// Calculate overtime pay using Attendances/Settings
		public decimal CalculateOvertimePay(AttendanceForPolicy a)
		{
			if (a == null) return 0m;

			bool requireApproval = bool.TryParse(SettingsGetter("OVERTIME_REQUIRE_APPROVAL"), out var ra) ? ra : true;
			if (requireApproval && a.IsOvertimeApproved != true) return 0m;

			var minMinutes = int.TryParse(SettingsGetter("OVERTIME_MINUTES_THRESHOLD"), out var mm) ? mm : 30;
			if (a.ApprovedOvertimeHours * 60 < minMinutes) return 0m;

			var baseRate = decimal.TryParse(SettingsGetter("OVERTIME_BASE_RATE"), out var br) ? br : 1.5m;
			var nightRate = decimal.TryParse(SettingsGetter("OVERTIME_NIGHT_RATE"), out var nr) ? nr : 2.0m;
			var holidayRate = decimal.TryParse(SettingsGetter("OVERTIME_HOLIDAY_RATE"), out var hr) ? hr : 3.0m;

			decimal holidayMultiplier = GetHolidayMultiplier(a.WorkDate);
			decimal rate = baseRate;

			var checkout = a.CheckOutTime ?? DateTime.MinValue;
			bool isNight = checkout != DateTime.MinValue && (checkout.TimeOfDay.Hours >= 22 || checkout.TimeOfDay.Hours < 6);
			if (isNight) rate = Math.Max(rate, nightRate);
			if (holidayMultiplier > 1m) rate = Math.Max(rate, holidayRate);

			var userSalary = _context.UserSalarySettings.FirstOrDefault(u => u.UserId == a.UserId && u.IsActive == true);
			var baseSalary = userSalary?.BaseSalary ?? 5000000m;
			var hourly = (baseSalary / 26m) / 8m;

			decimal pay = a.ApprovedOvertimeHours * hourly * rate * holidayMultiplier;
			return Math.Round(pay, 0);
		}

		private decimal GetHolidayMultiplier(DateTime date)
		{
			var listJson = SettingsGetter("HOLIDAY_LIST");
			if (!string.IsNullOrWhiteSpace(listJson))
			{
				try
				{
					var list = JsonSerializer.Deserialize<List<HolidayItem>>(listJson) ?? new List<HolidayItem>();
					var found = list.FirstOrDefault(h => DateTime.TryParse(h.date, out var d) && d.Date == date.Date);
					if (found != null) return found.multiplier <= 0 ? (decimal.TryParse(SettingsGetter("HOLIDAY_DEFAULT_MULTIPLIER"), out var dm) ? dm : 2m) : found.multiplier;
				}
				catch { }
			}
			return 1.0m;
		}

		private static T? ParseJson<T>(string? json)
		{
			if (string.IsNullOrWhiteSpace(json)) return default;
			try { return JsonSerializer.Deserialize<T>(json); } catch { return default; }
		}

		private string SettingsGetter(string key)
		{
			var s = _context.SystemSettings.FirstOrDefault(x => x.SettingKey == key && x.IsActive == true);
			return s?.SettingValue ?? string.Empty;
		}

		private class LeaveOverride { public string type { get; set; } = ""; public int days { get; set; } public string pay { get; set; } = "partial"; }
		private class HolidayItem { public string date { get; set; } = ""; public string name { get; set; } = ""; public decimal multiplier { get; set; } = 1m; }
	}

	// DTO to pass minimal attendance info into policy calculation
	public class AttendanceForPolicy
	{
		public int UserId { get; set; }
		public DateTime WorkDate { get; set; }
		public DateTime? CheckOutTime { get; set; }
		public decimal ApprovedOvertimeHours { get; set; }
		public bool? IsOvertimeApproved { get; set; } = false;
	}
}
