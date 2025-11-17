using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class PasswordResetOtp
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Email { get; set; } = null!;

    public string OtpCode { get; set; } = null!;

    public DateTime ExpiryTime { get; set; }

    public bool IsUsed { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public int FailedAttempts { get; set; }

    public virtual User User { get; set; } = null!;
}
