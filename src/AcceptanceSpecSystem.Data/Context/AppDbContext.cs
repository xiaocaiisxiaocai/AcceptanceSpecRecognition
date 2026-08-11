using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    /// 验收规格逐次引用记录表
    /// </summary>
    public DbSet<AcceptanceSpecReferenceEvent> AcceptanceSpecReferenceEvents =>
        Set<AcceptanceSpecReferenceEvent>();

    /// <summary>
    /// 验收规格完整内容版本快照表
    /// </summary>
    public DbSet<AcceptanceSpecContentVersion> AcceptanceSpecContentVersions =>
        Set<AcceptanceSpecContentVersion>();

    public DbSet<AcceptanceSpecCleanupScan> AcceptanceSpecCleanupScans =>
        Set<AcceptanceSpecCleanupScan>();

    public DbSet<AcceptanceSpecCleanupScanItem> AcceptanceSpecCleanupScanItems =>
        Set<AcceptanceSpecCleanupScanItem>();

    public DbSet<AcceptanceSpecCleanupDeletionRecord> AcceptanceSpecCleanupDeletionRecords =>
        Set<AcceptanceSpecCleanupDeletionRecord>();

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

    public DbSet<SmartStructureRoutingRule> SmartStructureRoutingRules => Set<SmartStructureRoutingRule>();

    /// <summary>
    /// 文档结构模板表
    /// </summary>
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    public DbSet<DocumentTemplateRegion> DocumentTemplateRegions => Set<DocumentTemplateRegion>();

    /// <summary>
    /// 系统用户表
    /// </summary>
    public DbSet<SystemUser> SystemUsers => Set<SystemUser>();

    /// <summary>
    /// 浏览器刷新令牌会话
    /// </summary>
    public DbSet<AuthRefreshSession> AuthRefreshSessions => Set<AuthRefreshSession>();

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

    public DbSet<DocumentImportExecution> DocumentImportExecutions => Set<DocumentImportExecution>();

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
            entity.HasQueryFilter(e => e.CleanupStatus == AcceptanceSpecCleanupStatus.Active);
            entity.Property(e => e.Project).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Specification).IsRequired();
            entity.Property(e => e.ReferenceCount).HasDefaultValue(0L).IsConcurrencyToken();
            entity.Property(e => e.ReferenceVersion).HasDefaultValue(1L).IsConcurrencyToken();
            entity.Property(e => e.CleanupStatus).HasDefaultValue(AcceptanceSpecCleanupStatus.Active);
            entity.Property(e => e.CleanupScanIgnored).HasDefaultValue(false);
            entity.Property(e => e.CleanupScanIgnoreReason).HasMaxLength(500);
            entity.Property(e => e.QuarantineReason).HasMaxLength(500);
            entity.Property(e => e.QuarantineSourceScanId).HasMaxLength(32);
            entity.HasIndex(e => new { e.CleanupStatus, e.QuarantineExpiresAtUtc, e.Id });
            entity.HasIndex(e => new { e.CleanupStatus, e.CleanupScanIgnored, e.Id });
            entity.HasIndex(e => new
            {
                e.CustomerId,
                e.ProcessId,
                e.MachineModelId,
                e.ImportedAt,
                e.Id
            });
            entity.HasIndex(e => e.ImportedAt);
            entity.HasIndex(e => e.WordFileId);
            entity.HasIndex(e => e.OwnerOrgUnitId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasOne(e => e.Customer)
                  .WithMany(c => c.AcceptanceSpecs)
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Process)
                  .WithMany(p => p.AcceptanceSpecs)
                  .HasForeignKey(e => e.ProcessId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.MachineModel)
                  .WithMany(m => m.AcceptanceSpecs)
                  .HasForeignKey(e => e.MachineModelId)
                  .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<AcceptanceSpecReferenceEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e =>
                e.AcceptanceSpec.CleanupStatus == AcceptanceSpecCleanupStatus.Active);
            entity.Property(e => e.TaskId).HasMaxLength(64);
            entity.Property(e => e.OccurrenceCount).HasDefaultValue(1L);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AcceptanceSpecReferenceEvents_OccurrenceCount",
                "`OccurrenceCount` > 0"));
            entity.HasIndex(e => new
            {
                e.TaskId,
                e.AcceptanceSpecId,
                e.ReferenceVersion,
                e.TaskOccurrenceIndex
            }).IsUnique();
            entity.HasIndex(e => new
            {
                e.AcceptanceSpecId,
                e.ReferenceVersion,
                e.ReferencedAtUtc,
                e.Id
            });
            entity.HasOne(e => e.AcceptanceSpec)
                .WithMany(spec => spec.ReferenceEvents)
                .HasForeignKey(e => e.AcceptanceSpecId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AcceptanceSpecContentVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e =>
                e.AcceptanceSpec.CleanupStatus == AcceptanceSpecCleanupStatus.Active);
            entity.Property(e => e.Project).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Specification).IsRequired().HasMaxLength(4000);
            entity.Property(e => e.Acceptance).HasMaxLength(4000);
            entity.Property(e => e.Remark).HasMaxLength(2000);
            entity.Property(e => e.ChangedByNameSnapshot).HasMaxLength(100);
            entity.Property(e => e.ChangeSource).IsRequired().HasMaxLength(40);
            entity.Property(e => e.ChangeReason).HasMaxLength(500);
            entity.HasIndex(e => new { e.AcceptanceSpecId, e.Version }).IsUnique();
            entity.HasIndex(e => new { e.AcceptanceSpecId, e.Version, e.Id });
            entity.HasOne(e => e.AcceptanceSpec)
                .WithMany(spec => spec.ContentVersions)
                .HasForeignKey(e => e.AcceptanceSpecId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<SystemUser>()
                .WithMany()
                .HasForeignKey(e => e.ChangedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AcceptanceSpecCleanupScan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.ScopeOrgUnitIds).HasMaxLength(4000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            entity.HasIndex(e => new { e.Status, e.CreatedAtUtc, e.Id });
            entity.HasIndex(e => new { e.CompanyId, e.RequestedByUserId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<AcceptanceSpecCleanupScanItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e =>
                e.AcceptanceSpec.CleanupStatus == AcceptanceSpecCleanupStatus.Active);
            entity.Property(e => e.ScanId).HasMaxLength(32);
            entity.HasIndex(e => new { e.ScanId, e.AcceptanceSpecId }).IsUnique();
            entity.HasIndex(e => new { e.ScanId, e.Category, e.Id });
            entity.HasOne(e => e.Scan)
                .WithMany(scan => scan.Items)
                .HasForeignKey(e => e.ScanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AcceptanceSpec)
                .WithMany(spec => spec.CleanupScanItems)
                .HasForeignKey(e => e.AcceptanceSpecId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AcceptanceSpecCleanupDeletionRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceScanId).HasMaxLength(32);
            entity.HasIndex(e => new { e.CompanyId, e.DeletedAtUtc, e.Id });
            entity.HasIndex(e => e.OriginalAcceptanceSpecId);
        });

        // EmbeddingCache配置
        modelBuilder.Entity<EmbeddingCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(e =>
                e.Spec.CleanupStatus == AcceptanceSpecCleanupStatus.Active);
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
            entity.HasQueryFilter(e => e.DeletionStatus == WordFileDeletionStatus.Active);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(260);
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.DeletionStatus)
                .HasDefaultValue(WordFileDeletionStatus.Active);
            entity.Property(e => e.DeletionRetryCount).HasDefaultValue(0);
            entity.Property(e => e.LastDeletionError).HasMaxLength(64);
            entity.Property(e => e.DeletionLeaseToken).HasMaxLength(64);
            entity.HasIndex(e => new { e.DeletionStatus, e.NextDeletionAttemptAt, e.Id })
                .HasDatabaseName("IX_WordFiles_DeletionStatus_NextDeletionAttemptAt_Id");
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

            // 乐观并发控制：MySQL 无原生 rowversion 类型，采用应用侧维护的数值版本号，
            // 由 AiServiceConfigurationAppService 在每次更新前自增；EF Core 将其作为并发令牌
            // 附加到 UPDATE 语句的 WHERE 子句，冲突时抛出 DbUpdateConcurrencyException。
            entity.Property(e => e.RowVersion).IsConcurrencyToken().HasDefaultValue(0u);

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
            entity.Property(e => e.ScopeKey).IsRequired().HasMaxLength(32);
            entity.Property(e => e.NormalizedPattern).IsRequired().HasMaxLength(200);
            entity.Property(e => e.GlobalNormalizedPatternKey).HasMaxLength(200);
            entity.Property(e => e.Source)
                .HasDefaultValue(ColumnMappingRuleSource.Manual)
                .HasSentinel((ColumnMappingRuleSource)0);
            entity.HasIndex(e => new { e.TargetField, e.Pattern });
            entity.HasIndex(e => new { e.CustomerId, e.TargetField, e.Pattern });
            entity.HasIndex(e => new { e.TargetField, e.Priority });
            entity.HasIndex(e => new { e.ScopeKey, e.TargetField, e.NormalizedPattern }).IsUnique();
            entity.HasIndex(e => e.GlobalNormalizedPatternKey).IsUnique();
        });

        // SmartStructureRoutingRule 配置
        modelBuilder.Entity<SmartStructureRoutingRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TableKind).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Recommendation).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Source)
                .HasDefaultValue(SmartStructureRoutingRuleSource.Manual)
                .HasSentinel((SmartStructureRoutingRuleSource)0);
            entity.Property(e => e.Weight).HasDefaultValue(1.0);
            entity.HasIndex(e => new { e.CustomerId, e.MatchScope, e.Pattern });
            entity.HasIndex(e => new { e.TableKind, e.Priority });
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

        modelBuilder.Entity<DocumentTemplateRegion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HeadersJson).IsRequired();
            entity.HasIndex(e => new { e.DocumentTemplateId, e.RegionIndex }).IsUnique();
            entity.HasOne(e => e.DocumentTemplate)
                .WithMany(e => e.Regions)
                .HasForeignKey(e => e.DocumentTemplateId)
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

        modelBuilder.Entity<AuthRefreshSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FamilyId).IsRequired().HasMaxLength(32);
            entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RevocationReason).HasMaxLength(128);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.FamilyId, e.Status });
            entity.HasIndex(e => new { e.UserId, e.Status });
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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
                  .OnDelete(DeleteBehavior.Restrict);
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
            entity.HasIndex(e => new { e.CompanyId, e.OwnerOrgUnitId, e.CreatedAt });
        });

        modelBuilder.Entity<DocumentImportExecution>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestKey).IsRequired().HasMaxLength(64);
            entity.Property(e => e.RequestFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ResultJson).IsRequired();
            entity.Property(e => e.Message).IsRequired().HasMaxLength(512);
            entity.HasIndex(e => e.RequestKey).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.SourceFileId);
            entity.HasIndex(e => new { e.CompanyId, e.CreatedByUserId, e.CreatedAt });
        });
    }

    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefreshColumnMappingRuleUniqueIdentities();
        BumpAiServiceConfigRowVersions();
        var referencedWordFileIds = GetChangedWordFileReferenceIds();
        IDbContextTransaction? ownedTransaction = null;
        try
        {
            if (referencedWordFileIds.Length > 0 &&
                Database.IsRelational() &&
                Database.CurrentTransaction == null)
                ownedTransaction = Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
            EnsureActiveWordFileReferences(referencedWordFileIds);
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            ownedTransaction?.Commit();
            return result;
        }
        catch
        {
            ownedTransaction?.Rollback();
            throw;
        }
        finally
        {
            ownedTransaction?.Dispose();
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RefreshColumnMappingRuleUniqueIdentities();
        BumpAiServiceConfigRowVersions();
        var referencedWordFileIds = GetChangedWordFileReferenceIds();
        IDbContextTransaction? ownedTransaction = null;
        try
        {
            if (referencedWordFileIds.Length > 0 &&
                Database.IsRelational() &&
                Database.CurrentTransaction == null)
            {
                ownedTransaction = await Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    cancellationToken);
            }
            await EnsureActiveWordFileReferencesAsync(referencedWordFileIds, cancellationToken);
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownedTransaction != null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (ownedTransaction != null)
                await ownedTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownedTransaction != null)
                await ownedTransaction.DisposeAsync();
        }
    }

    internal int[] GetChangedWordFileReferenceIds()
    {
        ChangeTracker.DetectChanges();
        return ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity switch
            {
                AcceptanceSpec spec when entry.State == EntityState.Added ||
                                         entry.Property(nameof(AcceptanceSpec.WordFileId)).IsModified
                    => spec.WordFileId,
                MatchingFillTask task when entry.State == EntityState.Added ||
                                             entry.Property(nameof(MatchingFillTask.SourceFileId)).IsModified
                    => task.SourceFileId,
                DocumentImportExecution execution when entry.State == EntityState.Added ||
                                                       entry.Property(nameof(DocumentImportExecution.SourceFileId)).IsModified
                    => execution.SourceFileId,
                _ => int.MinValue
            })
            .Where(id => id != int.MinValue)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    private void EnsureActiveWordFileReferences(IEnumerable<int> referenceIds)
    {
        foreach (var id in referenceIds)
        {
            var trackedUnavailable = ChangeTracker.Entries<WordFile>()
                .Any(entry => entry.Entity.Id == id &&
                              (entry.State == EntityState.Deleted ||
                               entry.Entity.DeletionStatus != WordFileDeletionStatus.Active));
            if (trackedUnavailable)
                throw new WordFileReferenceUnavailableException(id);

            if (id <= 0)
            {
                var tracked = ChangeTracker.Entries<WordFile>()
                    .Any(entry => entry.Entity.Id == id &&
                                  entry.State != EntityState.Deleted &&
                                  entry.Entity.DeletionStatus == WordFileDeletionStatus.Active);
                if (!tracked)
                    throw new WordFileReferenceUnavailableException(id);
                continue;
            }

            var active = IsMySqlProvider()
                ? LockActiveWordFileMySql(id)
                : WordFiles.IgnoreQueryFilters().AsNoTracking()
                    .Any(file => file.Id == id && file.DeletionStatus == WordFileDeletionStatus.Active);
            if (!active)
                throw new WordFileReferenceUnavailableException(id);
        }
    }

    private async Task EnsureActiveWordFileReferencesAsync(
        IEnumerable<int> referenceIds,
        CancellationToken cancellationToken)
    {
        foreach (var id in referenceIds)
        {
            var trackedUnavailable = ChangeTracker.Entries<WordFile>()
                .Any(entry => entry.Entity.Id == id &&
                              (entry.State == EntityState.Deleted ||
                               entry.Entity.DeletionStatus != WordFileDeletionStatus.Active));
            if (trackedUnavailable)
                throw new WordFileReferenceUnavailableException(id);

            if (id <= 0)
            {
                var tracked = ChangeTracker.Entries<WordFile>()
                    .Any(entry => entry.Entity.Id == id &&
                                  entry.State != EntityState.Deleted &&
                                  entry.Entity.DeletionStatus == WordFileDeletionStatus.Active);
                if (!tracked)
                    throw new WordFileReferenceUnavailableException(id);
                continue;
            }

            var active = IsMySqlProvider()
                ? await LockActiveWordFileMySqlAsync(id, cancellationToken)
                : await WordFiles.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(
                        file => file.Id == id && file.DeletionStatus == WordFileDeletionStatus.Active,
                        cancellationToken);
            if (!active)
                throw new WordFileReferenceUnavailableException(id);
        }
    }

    private bool IsMySqlProvider() =>
        Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    private bool LockActiveWordFileMySql(int id)
    {
        using var command = CreateWordFileReferenceLockCommand(id);
        return command.ExecuteScalar() != null;
    }

    private async Task<bool> LockActiveWordFileMySqlAsync(
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateWordFileReferenceLockCommand(id);
        return await command.ExecuteScalarAsync(cancellationToken) != null;
    }

    private System.Data.Common.DbCommand CreateWordFileReferenceLockCommand(int id)
    {
        var transaction = Database.CurrentTransaction
            ?? throw new InvalidOperationException("WordFile 引用锁必须在数据库事务内获取");
        var command = Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "SELECT `Id` FROM `WordFiles` " +
            "WHERE `Id` = @wordFileId AND `DeletionStatus` = @activeStatus FOR UPDATE;";
        var idParameter = command.CreateParameter();
        idParameter.ParameterName = "@wordFileId";
        idParameter.Value = id;
        command.Parameters.Add(idParameter);
        var statusParameter = command.CreateParameter();
        statusParameter.ParameterName = "@activeStatus";
        statusParameter.Value = (int)WordFileDeletionStatus.Active;
        command.Parameters.Add(statusParameter);
        return command;
    }

    private void RefreshColumnMappingRuleUniqueIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<ColumnMappingRule>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RefreshUniqueIdentity();
            }
        }
    }

    /// <summary>
    /// 每次保存前将修改中的 AiServiceConfig 行版本号自增一，
    /// 使 EF Core 生成的 UPDATE 语句携带新旧版本号，达成乐观并发校验效果。
    /// </summary>
    private void BumpAiServiceConfigRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries<AiServiceConfig>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.RowVersion = unchecked(entry.Entity.RowVersion + 1);
            }
        }
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
