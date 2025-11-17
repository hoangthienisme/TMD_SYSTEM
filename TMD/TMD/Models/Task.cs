using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class Task
{
    public int TaskId { get; set; }

    public string TaskName { get; set; } = null!;

    public string? Description { get; set; }

    public string? Platform { get; set; }

    public int? TargetPerWeek { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? Deadline { get; set; }

    public string? Priority { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<UserTask> UserTasks { get; set; } = new List<UserTask>();
}
