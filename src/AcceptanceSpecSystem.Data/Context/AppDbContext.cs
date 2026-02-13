using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Context;

/// <summary>
/// 应用程序数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
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
    /// 操作历史表
    /// </summary>
    public DbSet<OperationHistory> OperationHistories => Set<OperationHistory>();

    /// <summary>
    /// Word文件表
    /// </summary>
    public DbSet<WordFile> WordFiles => Set<WordFile>();

    /// <summary>
    /// AI服务配置表
    /// </summary>
    public DbSet<AiServiceConfig> AiServiceConfigs => Set<AiServiceConfig>();

    /// <summary>
    /// 同义词组表
    /// </summary>
    public DbSet<SynonymGroup> SynonymGroups => Set<SynonymGroup>();

    /// <summary>
    /// 同义词表
    /// </summary>
    public DbSet<SynonymWord> SynonymWords => Set<SynonymWord>();

    /// <summary>
    /// 关键字表
    /// </summary>
    public DbSet<Keyword> Keywords => Set<Keyword>();

    /// <summary>
    /// 文本处理配置表
    /// </summary>
    public DbSet<TextProcessingConfig> TextProcessingConfigs => Set<TextProcessingConfig>();

    /// <summary>
    /// Prompt模板表
    /// </summary>
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    /// <summary>
    /// 导入列映射规则表（全局）
    /// </summary>
    public DbSet<ColumnMappingRule> ColumnMappingRules => Set<ColumnMappingRule>();

    /// <summary>
    /// 默认MySQL连接字符串
    /// </summary>
    public const string DefaultConnectionString = "Server=localhost;Database=acceptance_spec_db;User=root;Password=abc+123;CharSet=utf8mb4;";

    /// <summary>
    /// 数据库连接字符串
    /// </summary>
    private readonly string _connectionString;

    /// <summary>
    /// 创建DbContext实例（使用默认连接字符串）
    /// </summary>
    public AppDbContext()
    {
        _connectionString = DefaultConnectionString;
    }

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
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        _connectionString = string.Empty;
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
        });

        // EmbeddingCache配置
        modelBuilder.Entity<EmbeddingCache>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.SpecId, e.ModelName }).IsUnique();
            entity.HasOne(e => e.Spec)
                  .WithMany(s => s.EmbeddingCaches)
                  .HasForeignKey(e => e.SpecId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // OperationHistory配置
        modelBuilder.Entity<OperationHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OperationType).IsRequired();
        });

        // WordFile配置
        modelBuilder.Entity<WordFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(260);
            entity.Property(e => e.FileType).IsRequired();
            entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.HasIndex(e => e.FileHash).IsUnique();
        });

        // AiServiceConfig配置
        modelBuilder.Entity<AiServiceConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // SynonymGroup配置
        modelBuilder.Entity<SynonymGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // SynonymWord配置
        modelBuilder.Entity<SynonymWord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Word).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Word);
            entity.HasOne(e => e.Group)
                  .WithMany(g => g.Words)
                  .HasForeignKey(e => e.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Keyword配置
        modelBuilder.Entity<Keyword>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Word).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Word).IsUnique();
        });

        // TextProcessingConfig配置
        modelBuilder.Entity<TextProcessingConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OkStandardFormat).HasMaxLength(50);
            entity.Property(e => e.NgStandardFormat).HasMaxLength(50);
            entity.Property(e => e.HighlightColorHex).HasMaxLength(10);
        });

        // PromptTemplate配置
        modelBuilder.Entity<PromptTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // ColumnMappingRule配置
        modelBuilder.Entity<ColumnMappingRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => new { e.TargetField, e.Pattern });
            entity.HasIndex(e => new { e.TargetField, e.Priority });
        });
    }
}
