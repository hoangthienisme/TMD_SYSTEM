using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SalaryConfigHistory
{
    public int HistoryId { get; set; }

    public int ConfigId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public int ChangedBy { get; set; }

    public DateTime? ChangedAt { get; set; }

    public string? Reason { get; set; }

    public virtual User ChangedByNavigation { get; set; } = null!;

    public virtual SalaryConfiguration Config { get; set; } = null!;
}
