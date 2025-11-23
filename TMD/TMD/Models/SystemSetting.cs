using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SystemSetting
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

    public int? Priority { get; set; }

    public string? Unit { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }
}
