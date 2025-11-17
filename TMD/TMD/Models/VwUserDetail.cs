using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class VwUserDetail
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Avatar { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? DepartmentName { get; set; }

    public string? RoleName { get; set; }
}
