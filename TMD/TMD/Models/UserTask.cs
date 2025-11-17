using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class UserTask
{
    public int UserTaskId { get; set; }

    public int UserId { get; set; }

    public int TaskId { get; set; }

    public int? CompletedThisWeek { get; set; }

    public string? ReportLink { get; set; }

    public DateOnly? WeekStartDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Task Task { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
