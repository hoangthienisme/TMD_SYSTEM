using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SystemSettingsBackup20251123
{
    public int SettingId { get; set; }

    public string SettingKey { get; set; } = null!;

    public string? SettingValue { get; set; }

    public string? Description { get; set; }

    public string? DataType { get; set; }

    public string? Category { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    public bool IsEnabled { get; set; }

    public string? DisplayName { get; set; }

    public bool IsSystemDefault { get; set; }

    public string? ApplyMethod { get; set; }
}
