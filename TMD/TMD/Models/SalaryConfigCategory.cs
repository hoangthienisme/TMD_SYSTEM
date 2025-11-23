using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class SalaryConfigCategory
{
    public int CategoryId { get; set; }

    public string CategoryCode { get; set; } = null!;

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public int? DisplayOrder { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<SalaryConfiguration> SalaryConfigurations { get; set; } = new List<SalaryConfiguration>();
}
