using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class AuditLog
{
    public int AuditLogId { get; set; }

    public int? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string? EntityName { get; set; }

    public int? EntityId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Description { get; set; }

    public string? Ipaddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Location { get; set; }

    public DateTime? Timestamp { get; set; }

    public virtual User? User { get; set; }
}
