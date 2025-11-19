using System;
using System.Collections.Generic;

namespace TMD.Models;

public partial class VwSalaryCalculation
{
    public int UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Email { get; set; }

    public string? DepartmentName { get; set; }

    public int? Year { get; set; }

    public int? Month { get; set; }

    public decimal? BaseSalary { get; set; }

    public string? SalaryType { get; set; }

    public int? WorkedDays { get; set; }

    public decimal? TotalWorkHours { get; set; }

    public int? LateDays { get; set; }

    public decimal? DailySalaryTotal { get; set; }

    public decimal? TotalOvertimeHours { get; set; }

    public decimal? OvertimeSalary { get; set; }

    public decimal? AutoDeduction { get; set; }

    public decimal? MonthlyAllowance { get; set; }
}
