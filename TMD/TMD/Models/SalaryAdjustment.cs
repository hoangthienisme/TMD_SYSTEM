using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SalaryAdjustment
{
    public int AdjustmentId { get; set; }

    public int UserId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int? AttendanceId { get; set; }

    public string AdjustmentType { get; set; } = null!;

    public string? Category { get; set; }

    public decimal? Amount { get; set; }

    public decimal? Hours { get; set; }

    public string Reason { get; set; } = null!;

    public string? Notes { get; set; }

    public int AdjustedBy { get; set; }

    public DateTime? AdjustedAt { get; set; }

    public bool? IsApproved { get; set; }

    public int? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public virtual User AdjustedByNavigation { get; set; } = null!;

    public virtual User? ApprovedByNavigation { get; set; }

    public virtual Attendance? Attendance { get; set; }

    public virtual User User { get; set; } = null!;
}
