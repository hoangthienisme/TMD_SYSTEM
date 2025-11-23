using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SalaryConfiguration
{
    public int ConfigId { get; set; }

    public int CategoryId { get; set; }

    public string ConfigCode { get; set; } = null!;

    public string ConfigName { get; set; } = null!;

    public string? Description { get; set; }

    public string ValueType { get; set; } = null!;

    public string Value { get; set; } = null!;

    public string? DisplayLabel { get; set; }

    public string? Unit { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public string? DefaultValue { get; set; }

    public bool? IsEditable { get; set; }

    public bool? IsActive { get; set; }

    public int? DisplayOrder { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedBy { get; set; }

    public virtual SalaryConfigCategory Category { get; set; } = null!;

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<SalaryConfigHistory> SalaryConfigHistories { get; set; } = new List<SalaryConfigHistory>();

    public virtual User? UpdatedByNavigation { get; set; }
}
