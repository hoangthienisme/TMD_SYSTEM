using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class LoginHistory
{
    public int LoginHistoryId { get; set; }

    public int? UserId { get; set; }

    public string? Username { get; set; }

    public DateTime? LoginTime { get; set; }

    public DateTime? LogoutTime { get; set; }

    public string? Ipaddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Browser { get; set; }

    public string? Device { get; set; }

    public string? Location { get; set; }

    public bool? IsSuccess { get; set; }

    public string? FailReason { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? User { get; set; }
}
