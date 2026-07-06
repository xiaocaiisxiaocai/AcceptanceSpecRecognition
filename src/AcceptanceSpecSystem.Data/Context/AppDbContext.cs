using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AcceptanceSpecSystem.Data.Context;

/// <summary>
/// 应用程序数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// 数据保护提供者（用于 ApiKey 加密）
    /// </summary>
    private readonly IDataProtectionProvider? _dataProtectionProvider;

    /// <summary>
    /// 客户表
    /// </summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>
    /// 制程表
    /// </summary>
    public DbSet<Process> Processes => Set<Process>();

    /// <summary>
    /// 机型表
    /// </summary>
    public DbSet<MachineModel> MachineModels => Set<MachineModel>();

    /// <summary>
    /// 验收规格表
    /// </summary>
    public DbSet<AcceptanceSpec> AcceptanceSpecs => Set<AcceptanceSpec>();

    /// <summary>
    /// 向量缓存表
    /// </summary>
    public DbSet<EmbeddingCache> EmbeddingCaches => Set<EmbeddingCache>();

    /// <summary>
    /// 向量缓存预热覆盖配置表
    /// </summary>
    public DbSet<EmbeddingCacheWarmupSetting> EmbeddingCacheWarmupSettings => Set<EmbeddingCacheWarmupSetting>();

    /// <summary>
    /// 数据库备份配置表
    /// </summary>
    public DbSet<DatabaseBackupSetting> DatabaseBackupSettings => Set<DatabaseBackupSetting>();

    /// <summary>
    /// Word文件表
    /// </summary>
    public DbSet<WordFile> WordFiles => Set<WordFile>();

    /// <summary>
    /// AI服务配置表
    /// </summary>
    public DbSet<AiServiceConfig> AiServiceConfigs => Set<AiServiceConfig>();

    /// <summary>
    /// Prompt模板表
    /// </summary>
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    /// <summary>
    /// Word 列映射规则表
    /// </summary>
    public DbSet<ColumnMappingRule> ColumnMappingRules => Set<ColumnMappingRule>();

    /// <summary>
    /// 文档结构模板表
    /// </summary>
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    /// <summary>
    /// 系统用户表
    /// </summary>
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();

    /// <summary>
    /// 公司表
    /// </summary>
    public DbSet<OrgCompany> OrgCompanies => Set<OrgCompany>();

    /// <summary>
    /// 组织节点表
    /// </summary>
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    /// <summary>
    /// 角色表
    /// </summary>
    public DbSet<AuthRole> AuthRoles => Set<AuthRole>();

    /// <summary>
    /// 权限表
    /// </summary>
    public DbSet<AuthPermission> AuthPermissions => Set<AuthPermission>();

    /// <summary>
    /// 角色权限关联表
    /// </summary>
    public DbSet<AuthRolePermission> AuthRolePermissions => Set<AuthRolePermission>();

    /// <summary>
    /// 用户角色关联表
    /// </summary>
    public DbSet<AuthUserRole> AuthUserRoles => Set<AuthUserRole>();

    /// <summary>
    /// 用户组织关联表
    /// </summary>
    public DbSet<AuthUserOrgUnit> AuthUserOrgUnits => Set<AuthUserOrgUnit>();

    /// <summary>
    /// 角色数据范围表
    /// </summary>
    public DbSet<AuthRoleDataScope> AuthRoleDataScopes => Set<AuthRoleDataScope>();

    /// <summary>
    /// 角色数据范围节点表
    /// </summary>
    public DbSet<AuthRoleDataScopeNode> AuthRoleDataScopeNodes => Set<AuthRoleDataScopeNode>();

    /// <summary>
    /// 审计日志表
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// 智能填充任务表
    /// </summary>
    public DbSet<MatchingFillTask> MatchingFillTasks => Set<MatchingFillTask>();

    /// <summary>
    /// 执行记录表
    /// </summary>
    public DbSet<ExecutionHistoryRecord> ExecutionHistoryRecords => Set<ExecutionHistoryRecord>();

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// 创建DbContext实例（使用指定连接字符串）
    /// </summary>
    /// <param name="connectionString">数据库连接字符串</param>
    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 创建DbContext实例（使用DbContextOptions）
    /// </summary>
    /// <param name="options">DbContext选项</param>
    /// <param name="dataProtectionProvider">数据保护提供者（可选，用于加密 ApiKey）</param>
    public AppDbContext(DbContextOptions<AppDbContext> options, IDataProtectionProvider? dataProtectionProvider = null) : base(options)
    {
        _connectionString = string.Empty;
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <summary>
    /// 配置数据库连接
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrEmpty(_connectionString))
        {
            optionsBuilder.UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString));
        }
    }

    /// <summary>
    /// 配置实体模型
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Customer配置
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Process配置
        modelBuilder.Entity<Process>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name);
        });

        // MachineModel配置
        modelBuilder.Entity<MachineModel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name);
        });

        // AcceptanceSpec配置
        modelBuilder.Entity<AcceptanceSpec>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Project).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Specification).IsRequired();
            entity.HasIndex(e => new { e.CustomerId, e.ProcessId, e.MachineModelId });
            entity.HasIndex(e => e.ImportedAt);
            entity.HasIndex(e => e.WordFileId);
            entity.HasIndex(e => e.OwnerOrgUnitId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasOne(e => e.Customer)
                  .WithMany(c => c.AcceptanceSpecs)
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Process)
                  .WithMany(p => p.AcceptanceSpecs)
                  .HasForeignKey(e => e.ProcessId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.MachineModel)
                  .WithMany(m => m.AcceptanceSpecs)
                  .HasForeignKey(e => e.MachineModelId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.WordFile)
                  .WithMany(w => w.AcceptanceSpecs)
                  .HasForeignKey(e => e.WordFileId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SystemUser>()
                  .WithMany()
                  .HasForeignKey(e => e.CreatedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<OrgUnit>()
                  .WithMany()
                  .HasForeignKey(e => e.OwnerOrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // EmbeddingCache配置
        modelBuilder.Entity<EmbeddingCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Usage).IsRequired().HasMaxLength(64).HasDefaultValue(EmbeddingCache.DefaultUsage);
            entity.Property(e => e.TextHash).IsRequired().HasMaxLength(128).HasDefaultValue(string.Empty);
            entity.Property(e => e.ModelVersion).HasMaxLength(50);
            entity.HasIndex(e => new { e.SpecId, e.ModelName, e.Usage }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.Spec)
                  .WithMany(s => s.EmbeddingCaches)
                  .HasForeignKey(e => e.SpecId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // EmbeddingCacheWarmupSetting 配置
        modelBuilder.Entity<EmbeddingCacheWarmupSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunAtLocalTime).HasMaxLength(16);
        });

        // DatabaseBackupSetting 配置
        modelBuilder.Entity<DatabaseBackupSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunAtLocalTime).HasMaxLength(16);
            entity.Property(e => e.BackupDirectory).IsRequired().HasMaxLength(500);
            entity.Property(e => e.LastError).HasMaxLength(2000);
            entity.Property(e => e.LastFileName).HasMaxLength(255);
        });

        // WordFile配置
        modelBuilder.Entity<WordFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(260);
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.HasIndex(e => e.FileHash);
            entity.HasIndex(e => e.CompanyId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.OwnerOrgUnitId);
        });

        // AiServiceConfig配置
        modelBuilder.Entity<AiServiceConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.IsDisabled).HasDefaultValue(false);
            entity.Property(e => e.DefaultRecallTopK).HasDefaultValue(2);

            // ApiKey 加密存储（DataProtection ValueConverter）
            if (_dataProtectionProvider != null)
            {
                var protector = _dataProtectionProvider.CreateProtector("AiServiceConfig.ApiKey");
                entity.Property(e => e.ApiKey).HasConversion(
                    new ValueConverter<string?, string?>(
                        v => EncryptApiKey(v, protector),
                        v => DecryptApiKey(v, protector)));
            }
        });

        // PromptTemplate配置
        modelBuilder.Entity<PromptTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DisplayName).HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // ColumnMappingRule 配置
        modelBuilder.Entity<ColumnMappingRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Source).HasDefaultValue(ColumnMappingRuleSource.Manual);
            entity.HasIndex(e => new { e.TargetField, e.Pattern });
            entity.HasIndex(e => new { e.CustomerId, e.TargetField, e.Pattern });
            entity.HasIndex(e => new { e.TargetField, e.Priority });
        });

        // DocumentTemplate 配置
        modelBuilder.Entity<DocumentTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TemplateName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.HeadersFingerprint).IsRequired().HasMaxLength(128);
            entity.Property(e => e.HeadersJson).IsRequired();
            entity.Property(e => e.TableKind).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Recommendation).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => new { e.CustomerId, e.HeadersFingerprint }).IsUnique();
            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // OrgCompany配置
        modelBuilder.Entity<OrgCompany>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => e.Code).IsUnique();
        });

        // OrgUnit配置
        modelBuilder.Entity<OrgUnit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Path).IsRequired().HasMaxLength(512);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique();
            entity.HasIndex(e => new { e.CompanyId, e.Path });
            entity.HasIndex(e => new { e.CompanyId, e.ParentId });
            entity.HasOne(e => e.Company)
                .WithMany(c => c.OrgUnits)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // AuthPermission配置
        modelBuilder.Entity<AuthPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Resource).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RoutePath).HasMaxLength(256);
            entity.Property(e => e.HttpMethod).HasMaxLength(16);
            entity.Property(e => e.ApiPath).HasMaxLength(256);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => new { e.PermissionType, e.Resource, e.Action });
        });

        // AuthRole配置
        modelBuilder.Entity<AuthRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique();
            entity.HasOne(e => e.Company)
                .WithMany(c => c.Roles)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuthRolePermission配置
        modelBuilder.Entity<AuthRolePermission>(entity =>
        {
            entity.HasKey(e => new { e.RoleId, e.PermissionId });
            entity.HasOne(e => e.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SystemUser配置
        modelBuilder.Entity<SystemUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(64);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Nickname).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Avatar).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PermissionVersion).HasDefaultValue(1);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => new { e.CompanyId, e.IsActive });
            entity.HasOne(e => e.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // AuthUserRole配置
        modelBuilder.Entity<AuthUserRole>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.RoleId, e.StartAt, e.EndAt });
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuthUserOrgUnit配置
        modelBuilder.Entity<AuthUserOrgUnit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.OrgUnitId, e.StartAt, e.EndAt });
            entity.HasOne(e => e.User)
                .WithMany(u => u.UserOrgUnits)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.OrgUnit)
                .WithMany(o => o.UserOrgUnits)
                .HasForeignKey(e => e.OrgUnitId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuthRoleDataScope配置
        modelBuilder.Entity<AuthRoleDataScope>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Resource).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => new { e.RoleId, e.Resource, e.ScopeType });
            entity.HasOne(e => e.Role)
                .WithMany(r => r.DataScopes)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuthRoleDataScopeNode配置
        modelBuilder.Entity<AuthRoleDataScopeNode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.RoleDataScopeId, e.OrgUnitId }).IsUnique();
            entity.HasOne(e => e.RoleDataScope)
                .WithMany(s => s.Nodes)
                .HasForeignKey(e => e.RoleDataScopeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.OrgUnit)
                .WithMany(o => o.DataScopeNodes)
                .HasForeignKey(e => e.OrgUnitId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog配置
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).IsRequired();
            entity.Property(e => e.Level).IsRequired();
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Username).HasMaxLength(64);
            entity.Property(e => e.RequestMethod).HasMaxLength(16);
            entity.Property(e => e.RequestPath).HasMaxLength(512);
            entity.Property(e => e.QueryString).HasMaxLength(1024);
            entity.Property(e => e.ClientIp).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(512);
            entity.Property(e => e.ClientTraceId).HasMaxLength(64);
            entity.Property(e => e.ClientId).HasMaxLength(64);
            entity.Property(e => e.FrontendRoute).HasMaxLength(512);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Source, e.CreatedAt });
            entity.HasIndex(e => e.Username);
            entity.HasIndex(e => e.StatusCode);
        });

        // MatchingFillTask 配置
        modelBuilder.Entity<MatchingFillTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.HasIndex(e => e.TaskId).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.CompanyId, e.CreatedByUserId, e.CreatedAt });
            entity.HasOne(e => e.SourceFile)
                  .WithMany()
                  .HasForeignKey(e => e.SourceFileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ExecutionHistoryRecord 配置
        modelBuilder.Entity<ExecutionHistoryRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TaskId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.TaskType).IsRequired().HasMaxLength(32);
            entity.Property(e => e.SourceFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DetailJson).IsRequired();
            entity.HasIndex(e => e.TaskId).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.CompanyId, e.CreatedByUserId, e.CreatedAt });
        });
    }

    /// <summary>
    /// 加密 ApiKey（空值透传）
    /// </summary>
    private static string? EncryptApiKey(string? value, IDataProtector protector)
    {
        return string.IsNullOrEmpty(value) ? value : protector.Protect(value);
    }

    /// <summary>
    /// 解密 ApiKey（向后兼容：旧明文数据解密失败时原样返回，下次保存时自动加密）
    /// </summary>
    private static string? DecryptApiKey(string? value, IDataProtector protector)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return protector.Unprotect(value);
        }
        catch
        {
            // 旧明文数据兼容：解密失败时原样返回，下次保存时自动加密
            System.Diagnostics.Trace.TraceWarning("AiServiceConfig.ApiKey 解密失败，按历史明文兼容逻辑回退。");
            return value;
        }
    }
}
