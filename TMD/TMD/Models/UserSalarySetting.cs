using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class UserSalarySetting
{
    public int UserSalaryId { get; set; }

    public int UserId { get; set; }

    public string SalaryType { get; set; } = null!;

    public decimal BaseSalary { get; set; }

    public decimal? HourlyRate { get; set; }

    public decimal? DefaultOvertimeRate { get; set; }

    public decimal? AllowanceAmount { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual User? UpdatedByNavigation { get; set; }

    public virtual User User { get; set; } = null!;
}
