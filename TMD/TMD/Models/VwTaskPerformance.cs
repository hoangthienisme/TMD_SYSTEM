using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class VwTaskPerformance
{
    public int UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Avatar { get; set; }

    public string? DepartmentName { get; set; }

    public string TaskName { get; set; } = null!;

    public string? Platform { get; set; }

    public int? TargetPerWeek { get; set; }

    public int? CompletedThisWeek { get; set; }

    public string? ReportLink { get; set; }

    public double? CompletionPercentage { get; set; }
}
