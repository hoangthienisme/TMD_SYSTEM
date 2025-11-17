using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class PasswordResetHistory
{
    public int ResetId { get; set; }

    public int UserId { get; set; }

    public int? ResetByUserId { get; set; }

    public string? OldPasswordHash { get; set; }

    public DateTime? ResetTime { get; set; }

    public string? ResetReason { get; set; }

    public string? Ipaddress { get; set; }

    public virtual User? ResetByUser { get; set; }

    public virtual User User { get; set; } = null!;
}
