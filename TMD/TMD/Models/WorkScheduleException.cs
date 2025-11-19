using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class WorkScheduleException
{
    public int ExceptionId { get; set; }

    public DateOnly WorkDate { get; set; }

    public int? UserId { get; set; }

    public int? DepartmentId { get; set; }

    public TimeOnly? CheckInStartTime { get; set; }

    public TimeOnly? CheckInStandardTime { get; set; }

    public TimeOnly? CheckOutMinTime { get; set; }

    public decimal? StandardHours { get; set; }

    public decimal? SalaryMultiplier { get; set; }

    public decimal? OvertimeMultiplier { get; set; }

    public string? Description { get; set; }

    public string? ExceptionType { get; set; }

    public bool? IsActive { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Attendance> Attendances { get; set; } = new List<Attendance>();

    public virtual User? CreatedByNavigation { get; set; }

    public virtual Department? Department { get; set; }

    public virtual User? User { get; set; }
}
