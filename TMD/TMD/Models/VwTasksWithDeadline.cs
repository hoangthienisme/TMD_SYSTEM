using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class VwTasksWithDeadline
{
    public int TaskId { get; set; }

    public string TaskName { get; set; } = null!;

    public string? Description { get; set; }

    public string? Platform { get; set; }

    public int? TargetPerWeek { get; set; }

    public DateTime? Deadline { get; set; }

    public string? Priority { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? DaysUntilDeadline { get; set; }

    public string DeadlineStatus { get; set; } = null!;

    public int? AssignedUserCount { get; set; }
}
