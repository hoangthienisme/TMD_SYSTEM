using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class VwTodayAttendance
{
    public int AttendanceId { get; set; }

    public int UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Avatar { get; set; }

    public string? DepartmentName { get; set; }

    public DateTime? CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    public bool? IsLate { get; set; }

    public decimal? TotalHours { get; set; }

    public string? CheckInAddress { get; set; }

    public string? CheckOutAddress { get; set; }
}
