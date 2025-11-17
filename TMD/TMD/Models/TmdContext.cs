using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace TMD.Models;

public partial class TmdContext : DbContext
{
    public TmdContext()
    {
    }

    public TmdContext(DbContextOptions<TmdContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Attendance> Attendances { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<LoginHistory> LoginHistories { get; set; }

    public virtual DbSet<PasswordResetHistory> PasswordResetHistories { get; set; }

    public virtual DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserTask> UserTasks { get; set; }

    public virtual DbSet<VwTaskPerformance> VwTaskPerformances { get; set; }

    public virtual DbSet<VwTasksWithDeadline> VwTasksWithDeadlines { get; set; }

    public virtual DbSet<VwTodayAttendance> VwTodayAttendances { get; set; }

    public virtual DbSet<VwUserDetail> VwUserDetails { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=.\\MSSQLSERVER02;Database=TMD;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Attendance>(entity =>
        {
            entity.HasKey(e => e.AttendanceId).HasName("PK__Attendan__8B69261CEBF9F49E");

            entity.HasIndex(e => new { e.UserId, e.WorkDate }, "IX_Attendances_UserId_WorkDate");

            entity.Property(e => e.CheckInAddress).HasMaxLength(500);
            entity.Property(e => e.CheckInIpaddress)
                .HasMaxLength(50)
                .HasColumnName("CheckInIPAddress");
            entity.Property(e => e.CheckInLatitude).HasColumnType("decimal(10, 8)");
            entity.Property(e => e.CheckInLongitude).HasColumnType("decimal(11, 8)");
            entity.Property(e => e.CheckInNotes).HasMaxLength(1000);
            entity.Property(e => e.CheckOutAddress).HasMaxLength(500);
            entity.Property(e => e.CheckOutIpaddress)
                .HasMaxLength(50)
                .HasColumnName("CheckOutIPAddress");
            entity.Property(e => e.CheckOutLatitude).HasColumnType("decimal(10, 8)");
            entity.Property(e => e.CheckOutLongitude).HasColumnType("decimal(11, 8)");
            entity.Property(e => e.CheckOutNotes).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.IsLate).HasDefaultValue(false);
            entity.Property(e => e.IsWithinGeofence).HasDefaultValue(true);
            entity.Property(e => e.TotalHours).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.User).WithMany(p => p.Attendances)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__Attendanc__UserI__534D60F1");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditLogId).HasName("PK__AuditLog__EB5F6CBD62F10178");

            entity.HasIndex(e => new { e.UserId, e.Timestamp }, "IX_AuditLogs_UserId_Timestamp");

            entity.Property(e => e.Action).HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.EntityName).HasMaxLength(100);
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(50)
                .HasColumnName("IPAddress");
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.Timestamp).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__AuditLogs__UserI__571DF1D5");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.DepartmentId).HasName("PK__Departme__B2079BED40AFE942");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.HasKey(e => e.LoginHistoryId).HasName("PK__LoginHis__2773EA9F24699591");

            entity.ToTable("LoginHistory");

            entity.HasIndex(e => e.UserId, "IX_LoginHistory_UserId");

            entity.Property(e => e.Browser).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Device).HasMaxLength(100);
            entity.Property(e => e.FailReason).HasMaxLength(200);
            entity.Property(e => e.Ipaddress)
                .HasMaxLength(50)
                .HasColumnName("IPAddress");
            entity.Property(e => e.IsSuccess).HasDefaultValue(true);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.Username).HasMaxLength(50);

            entity.HasOne(d => d.User).WithMany(p => p.LoginHistories)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__LoginHist__UserI__5BE2A6F2");
        });

        modelBuilder.Entity<PasswordResetHistory>(entity =>
        {
            entity.HasKey(e => e.ResetId).HasName("PK__Password__783CF04DF61E0EB7");

            entity.ToTable("PasswordResetHistory");

            entity.Property(e => e.Ipaddress)
                .HasMaxLength(50)
                .HasColumnName("IPAddress");
            entity.Property(e => e.OldPasswordHash).HasMaxLength(255);
            entity.Property(e => e.ResetReason).HasMaxLength(500);
            entity.Property(e => e.ResetTime).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.ResetByUser).WithMany(p => p.PasswordResetHistoryResetByUsers)
                .HasForeignKey(d => d.ResetByUserId)
                .HasConstraintName("FK__PasswordR__Reset__60A75C0F");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetHistoryUsers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PasswordR__UserI__5FB337D6");
        });

        modelBuilder.Entity<PasswordResetOtp>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Password__3214EC07F3B72DCD");

            entity.ToTable("PasswordResetOTPs");

            entity.HasIndex(e => e.Email, "IX_PasswordResetOTPs_Email");

            entity.HasIndex(e => e.OtpCode, "IX_PasswordResetOTPs_OtpCode");

            entity.HasIndex(e => e.UserId, "IX_PasswordResetOTPs_UserId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.ExpiryTime).HasColumnType("datetime");
            entity.Property(e => e.OtpCode).HasMaxLength(6);
            entity.Property(e => e.UsedAt).HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetOtps)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_PasswordResetOTPs_Users");
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Password__3214EC071EAAF641");

            entity.HasIndex(e => e.Token, "IX_PasswordResetTokens_Token");

            entity.HasIndex(e => e.UserId, "IX_PasswordResetTokens_UserId");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ExpiryTime).HasColumnType("datetime");
            entity.Property(e => e.Token).HasMaxLength(500);
            entity.Property(e => e.UsedAt).HasColumnType("datetime");

            entity.HasOne(d => d.User).WithMany(p => p.PasswordResetTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PasswordResetTokens_Users");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PK__Roles__8AFACE1A31ADBDE1");

            entity.HasIndex(e => e.RoleName, "UQ__Roles__8A2B6160E7BCAB19").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.RoleName).HasMaxLength(50);
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.HasKey(e => e.TaskId).HasName("PK__Tasks__7C6949B188536639");

            entity.HasIndex(e => e.Deadline, "IX_Tasks_Deadline").HasFilter("([Deadline] IS NOT NULL)");

            entity.HasIndex(e => new { e.IsActive, e.Deadline }, "IX_Tasks_IsActive_Deadline").HasFilter("([Deadline] IS NOT NULL)");

            entity.HasIndex(e => e.Priority, "IX_Tasks_Priority");

            entity.HasIndex(e => e.UpdatedAt, "IX_Tasks_UpdatedAt");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Platform).HasMaxLength(200);
            entity.Property(e => e.Priority)
                .HasMaxLength(20)
                .HasDefaultValue("Medium");
            entity.Property(e => e.TargetPerWeek).HasDefaultValue(0);
            entity.Property(e => e.TaskName).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4CB231ECDE");

            entity.HasIndex(e => e.DepartmentId, "IX_Users_DepartmentId");

            entity.HasIndex(e => e.RoleId, "IX_Users_RoleId");

            entity.HasIndex(e => e.Username, "IX_Users_Username");

            entity.HasIndex(e => e.Username, "UQ__Users__536C85E4FED6441B").IsUnique();

            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.Username).HasMaxLength(50);

            entity.HasOne(d => d.Department).WithMany(p => p.Users)
                .HasForeignKey(d => d.DepartmentId)
                .HasConstraintName("FK__Users__Departmen__4222D4EF");

            entity.HasOne(d => d.Role).WithMany(p => p.Users)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Users__RoleId__4316F928");
        });

        modelBuilder.Entity<UserTask>(entity =>
        {
            entity.HasKey(e => e.UserTaskId).HasName("PK__UserTask__4EF5961FD9ECDA05");

            entity.Property(e => e.CompletedThisWeek).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ReportLink).HasMaxLength(500);

            entity.HasOne(d => d.Task).WithMany(p => p.UserTasks)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("FK__UserTasks__TaskI__4D94879B");

            entity.HasOne(d => d.User).WithMany(p => p.UserTasks)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__UserTasks__UserI__4CA06362");
        });

        modelBuilder.Entity<VwTaskPerformance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TaskPerformance");

            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Platform).HasMaxLength(200);
            entity.Property(e => e.ReportLink).HasMaxLength(500);
            entity.Property(e => e.TaskName).HasMaxLength(200);
        });

        modelBuilder.Entity<VwTasksWithDeadline>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TasksWithDeadline");

            entity.Property(e => e.DeadlineStatus)
                .HasMaxLength(13)
                .IsUnicode(false);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Platform).HasMaxLength(200);
            entity.Property(e => e.Priority).HasMaxLength(20);
            entity.Property(e => e.TaskId).ValueGeneratedOnAdd();
            entity.Property(e => e.TaskName).HasMaxLength(200);
        });

        modelBuilder.Entity<VwTodayAttendance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TodayAttendance");

            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.CheckInAddress).HasMaxLength(500);
            entity.Property(e => e.CheckOutAddress).HasMaxLength(500);
            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.TotalHours).HasColumnType("decimal(5, 2)");
        });

        modelBuilder.Entity<VwUserDetail>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_UserDetails");

            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.DepartmentName).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
            entity.Property(e => e.RoleName).HasMaxLength(50);
            entity.Property(e => e.Username).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
