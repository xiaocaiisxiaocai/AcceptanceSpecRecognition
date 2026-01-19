# 技术设计文档：验收规格管理系统

## Context

### 背景
企业验收流程中存在大量重复性工作：每次新产品验收都需要手动填写验收规格表，而这些数据往往可以从历史验收记录中找到参考。本系统旨在通过智能匹配技术，自动化这一流程。

### 约束条件
- 桌面应用，需离线可用（相似度匹配）
- 支持在线AI服务（OpenAI、Azure OpenAI）
- 支持本地私有化AI部署（Ollama、LM Studio）
- 数据本地存储（SQLite + EF Core）
- 单机部署，无需服务端

### 利益相关者
- 验收工程师：主要用户，执行日常验收工作
- 质量管理人员：配置匹配规则和阈值
- IT管理员：配置AI服务（在线密钥或本地私有化部署）

## Goals / Non-Goals

### Goals
- 提供直观的WinForms界面，降低学习成本
- 支持多种匹配算法，适应不同场景需求
- 配置可复用，减少重复设置
- 匹配结果透明可解释，显示得分计算过程
- 支持批量处理，提高效率
- **支持在线和本地私有化AI部署，满足不同安全需求**

### Non-Goals
- 不支持多用户协作（单机应用）
- 不提供Web版本
- 不支持.doc旧格式（仅.docx）
- 不集成ERP/MES等外部系统
- **暂不实现Excel格式支持（架构预留，后续版本添加）**

## 系统架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        Presentation Layer                        │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                    WinForms UI                            │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐│  │
│  │  │ 数据导入 │ │ 智能填充 │ │ 配置管理 │ │ 历史记录     ││  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────────┘│  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Business Layer                            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────┐│
│  │ DocumentSvc  │ │ MatchService │ │ ConfigurationService     ││
│  │ - IDocParser │ │ - 相似度计算 │ │ - 配置加载/保存          ││
│  │ - WordParser │ │ - 向量匹配   │ │ - 导入/导出              ││
│  │ - (Excel预留)│ │ - LLM匹配    │ │                          ││
│  └──────────────┘ └──────────────┘ └──────────────────────────┘│
│                              │                                   │
│                              ▼                                   │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                    AI Integration Layer                   │  │
│  │  ┌─────────────────────────────────────────────────────┐ │  │
│  │  │              Semantic Kernel                         │ │  │
│  │  │  ┌─────────────────────┐ ┌─────────────────────────┐│ │  │
│  │  │  │    在线服务         │ │    本地私有化服务        ││ │  │
│  │  │  │  ┌───────────────┐  │ │  ┌───────────────────┐  ││ │  │
│  │  │  │  │ OpenAI API    │  │ │  │ Ollama            │  ││ │  │
│  │  │  │  ├───────────────┤  │ │  ├───────────────────┤  ││ │  │
│  │  │  │  │ Azure OpenAI  │  │ │  │ LM Studio         │  ││ │  │
│  │  │  │  └───────────────┘  │ │  ├───────────────────┤  ││ │  │
│  │  │  │                     │ │  │ 自定义OpenAI兼容  │  ││ │  │
│  │  │  │                     │ │  │ API端点           │  ││ │  │
│  │  │  │                     │ │  └───────────────────┘  ││ │  │
│  │  │  └─────────────────────┘ └─────────────────────────┘│ │  │
│  │  └─────────────────────────────────────────────────────┘ │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Data Layer (EF Core)                      │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                    AppDbContext                           │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐│  │
│  │  │ Customers│ │ Processes│ │ Specs    │ │ Embeddings   ││  │
│  │  ├──────────┤ ├──────────┤ ├──────────┤ ├──────────────┤│  │
│  │  │ WordFiles│ │ History  │ │ AIConfig │ │              ││  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────────┘│  │
│  └──────────────────────────────────────────────────────────┘  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────┐│
│  │ SQLite DB    │ │ Word Files   │ │ Config Files (.json)     ││
│  │ (Code First) │ │ - 原始文档   │ │ - 匹配配置               ││
│  └──────────────┘ └──────────────┘ └──────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

## Decisions

### D1: Word处理库选择
**决策**: 使用 `DocumentFormat.OpenXml`
**理由**:
- 微软官方库，兼容性最佳
- 支持复杂表格操作（合并单元格、嵌套表格）
- 无需安装Office

**备选方案**:
- NPOI：功能强大但API复杂
- Aspose.Words：商业授权费用高

### D1.1: 文档处理抽象设计（预留Excel支持）
**决策**: 使用接口抽象文档解析和写入逻辑，预留Excel格式扩展

```csharp
// 文档解析器接口（预留Excel扩展）
public interface IDocumentParser
{
    DocumentType DocumentType { get; }
    bool CanParse(string filePath);
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(Stream stream);
    Task<TableData> ExtractTableDataAsync(Stream stream, int tableIndex, ColumnMapping mapping);
}

// 文档写入器接口（预留Excel扩展）
public interface IDocumentWriter
{
    DocumentType DocumentType { get; }
    bool CanWrite(string filePath);
    Task WriteTableDataAsync(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations);
}

public enum DocumentType
{
    Word,       // .docx - 当前实现
    Excel       // .xlsx - 预留，暂不实现
}

// Word实现
public class WordDocumentParser : IDocumentParser { /* 实现 */ }
public class WordDocumentWriter : IDocumentWriter { /* 实现 */ }

// Excel实现（预留）
// public class ExcelDocumentParser : IDocumentParser { /* 后续实现 */ }
// public class ExcelDocumentWriter : IDocumentWriter { /* 后续实现 */ }
```

**理由**:
- 架构预留，后续添加Excel支持无需大规模重构
- 统一的接口使UI层和业务层与具体文档格式解耦
- 便于单元测试和模拟

### D2: 相似度算法
**决策**: 提供多种算法供用户选择
- Levenshtein距离：适合短文本、拼写差异
- Jaccard系数：适合词集合比较
- 余弦相似度：适合向量化后的文本

**理由**: 不同场景适合不同算法，给用户灵活性

### D3: AI框架选择
**决策**: 使用 Microsoft Semantic Kernel
**理由**:
- 微软官方AI编排框架
- 原生支持.NET 8
- 统一的Connector抽象，易于切换提供商
- 内置Memory功能，支持向量检索
- **同时支持在线API和本地私有化部署**

### D4: ORM框架选择
**决策**: 使用 Entity Framework Core + Code First
**理由**:
- 微软官方ORM，与.NET 8深度集成
- Code First便于版本控制和团队协作
- 自动迁移管理数据库Schema变更
- 强类型查询，编译时检查
- 支持SQLite Provider

**备选方案**:
- Dapper：轻量但需要手写SQL
- 直接ADO.NET：灵活但开发效率低

### D5: 数据库Schema设计（EF Core Code First）

#### 实体类设计

```csharp
// 客户实体
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航属性
    public ICollection<Process> Processes { get; set; } = new List<Process>();
}

// 制程实体
public class Process
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航属性
    public Customer Customer { get; set; } = null!;
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}

// 验收规格实体
public class AcceptanceSpec
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public string Project { get; set; } = string.Empty;        // 项目
    public string Specification { get; set; } = string.Empty;  // 规格
    public string? Acceptance { get; set; }                    // 验收
    public string? Remark { get; set; }                        // 备注
    public int WordFileId { get; set; }                        // 来源文件ID
    public DateTime ImportedAt { get; set; } = DateTime.Now;

    // 导航属性
    public Process Process { get; set; } = null!;
    public WordFile WordFile { get; set; } = null!;
    public ICollection<EmbeddingCache> EmbeddingCaches { get; set; } = new List<EmbeddingCache>();
}

// 向量缓存实体
public class EmbeddingCache
{
    public int Id { get; set; }
    public int SpecId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public byte[] Vector { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航属性
    public AcceptanceSpec Spec { get; set; } = null!;
}

// 操作历史实体
public class OperationHistory
{
    public int Id { get; set; }
    public OperationType OperationType { get; set; }
    public string? TargetFile { get; set; }
    public string? Details { get; set; }           // JSON格式
    public bool CanUndo { get; set; } = true;
    public string? UndoData { get; set; }          // JSON格式
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public enum OperationType
{
    Import,
    Fill,
    Delete
}

// Word文件存储实体
public class WordFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public byte[] FileContent { get; set; } = Array.Empty<byte>();
    public string FileHash { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;

    // 导航属性
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}

// AI服务配置实体（存储到数据库便于管理）
public class AiServiceConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;           // 配置名称
    public AiServiceType ServiceType { get; set; }             // 服务类型
    public string? ApiKey { get; set; }                        // API密钥（加密存储）
    public string? Endpoint { get; set; }                      // 服务端点
    public string? EmbeddingModel { get; set; }                // Embedding模型名
    public string? LlmModel { get; set; }                      // LLM模型名
    public bool IsDefault { get; set; }                        // 是否默认配置
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

public enum AiServiceType
{
    // 在线服务
    OpenAI,
    AzureOpenAI,

    // 本地私有化服务
    Ollama,
    LMStudio,
    CustomOpenAICompatible   // 自定义OpenAI兼容API
}

// 同义词组实体
public class SynonymGroup
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    // 导航属性
    public ICollection<SynonymWord> Words { get; set; } = new List<SynonymWord>();
}

// 同义词实体
public class SynonymWord
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Word { get; set; } = string.Empty;
    public bool IsStandard { get; set; }                       // 是否为标准词（组内第一个词）

    // 导航属性
    public SynonymGroup Group { get; set; } = null!;
}

// 关键字实体
public class Keyword
{
    public int Id { get; set; }
    public string Word { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// 文本处理配置实体
public class TextProcessingConfig
{
    public int Id { get; set; }

    // 简繁转换配置
    public bool EnableChineseConversion { get; set; }
    public ChineseConversionMode ConversionMode { get; set; }

    // 同义词配置
    public bool EnableSynonym { get; set; }

    // OK/NG格式转换配置
    public bool EnableOkNgConversion { get; set; }
    public string OkStandardFormat { get; set; } = "OK";
    public string NgStandardFormat { get; set; } = "NG";

    // 关键字高亮配置
    public bool EnableKeywordHighlight { get; set; }
    public string HighlightColorHex { get; set; } = "#FFFF00";  // 默认黄色

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

// Prompt模板实体
public class PromptTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "default";              // 模板名称
    public string Content { get; set; } = string.Empty;        // Prompt内容
    public bool IsDefault { get; set; }                        // 是否为默认模板
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

public enum ChineseConversionMode
{
    None,
    HansToTW,      // 简体→台湾繁体
    TWToHans       // 台湾繁体→简体
}
```

#### DbContext配置

```csharp
public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<AcceptanceSpec> AcceptanceSpecs => Set<AcceptanceSpec>();
    public DbSet<EmbeddingCache> EmbeddingCaches => Set<EmbeddingCache>();
    public DbSet<OperationHistory> OperationHistories => Set<OperationHistory>();
    public DbSet<WordFile> WordFiles => Set<WordFile>();
    public DbSet<AiServiceConfig> AiServiceConfigs => Set<AiServiceConfig>();
    public DbSet<SynonymGroup> SynonymGroups => Set<SynonymGroup>();
    public DbSet<SynonymWord> SynonymWords => Set<SynonymWord>();
    public DbSet<Keyword> Keywords => Set<Keyword>();
    public DbSet<TextProcessingConfig> TextProcessingConfigs => Set<TextProcessingConfig>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=acceptance_spec.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Customer配置
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Process配置
        modelBuilder.Entity<Process>(entity =>
        {
            entity.HasIndex(e => new { e.CustomerId, e.Name }).IsUnique();
            entity.HasOne(e => e.Customer)
                  .WithMany(c => c.Processes)
                  .HasForeignKey(e => e.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // AcceptanceSpec配置
        modelBuilder.Entity<AcceptanceSpec>(entity =>
        {
            entity.HasOne(e => e.Process)
                  .WithMany(p => p.AcceptanceSpecs)
                  .HasForeignKey(e => e.ProcessId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.WordFile)
                  .WithMany(w => w.AcceptanceSpecs)
                  .HasForeignKey(e => e.WordFileId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // EmbeddingCache配置
        modelBuilder.Entity<EmbeddingCache>(entity =>
        {
            entity.HasIndex(e => new { e.SpecId, e.ModelName }).IsUnique();
            entity.HasOne(e => e.Spec)
                  .WithMany(s => s.EmbeddingCaches)
                  .HasForeignKey(e => e.SpecId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // WordFile配置
        modelBuilder.Entity<WordFile>(entity =>
        {
            entity.HasIndex(e => e.FileHash).IsUnique();
        });

        // AiServiceConfig配置
        modelBuilder.Entity<AiServiceConfig>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // SynonymGroup配置
        modelBuilder.Entity<SynonymGroup>(entity =>
        {
            // 无特殊索引
        });

        // SynonymWord配置
        modelBuilder.Entity<SynonymWord>(entity =>
        {
            entity.HasIndex(e => e.Word);
            entity.HasOne(e => e.Group)
                  .WithMany(g => g.Words)
                  .HasForeignKey(e => e.GroupId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Keyword配置
        modelBuilder.Entity<Keyword>(entity =>
        {
            entity.HasIndex(e => e.Word).IsUnique();
        });

        // TextProcessingConfig配置（单例模式，仅一条记录）
        modelBuilder.Entity<TextProcessingConfig>(entity =>
        {
            // 无特殊约束
        });

        // PromptTemplate配置
        modelBuilder.Entity<PromptTemplate>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
}
```

### D6: AI服务架构设计

#### 在线服务 vs 本地私有化

| 类型 | 服务 | 适用场景 | 优势 | 劣势 |
|------|------|----------|------|------|
| **在线** | OpenAI API | 快速集成、最新模型 | 模型强大、无需部署 | 需要网络、数据出境 |
| **在线** | Azure OpenAI | 企业合规需求 | 数据在Azure、SLA保障 | 需要Azure订阅 |
| **本地** | Ollama | 数据敏感、离线需求 | 完全私有、无网络依赖 | 需要本地算力 |
| **本地** | LM Studio | 桌面用户友好 | GUI管理、易于使用 | 资源占用较高 |
| **本地** | 自定义端点 | 企业私有部署 | 完全可控 | 需要自行部署 |

#### AI服务抽象接口

```csharp
public interface IAiService
{
    // 服务信息
    AiServiceType ServiceType { get; }
    bool IsOnline { get; }

    // 连接测试
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    // Embedding功能
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    // LLM功能
    Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default);
    Task<MatchAnalysisResult> AnalyzeMatchAsync(string query, string candidate, CancellationToken cancellationToken = default);
}

public interface IAiServiceFactory
{
    IAiService Create(AiServiceConfig config);
    IReadOnlyList<AiServiceType> GetAvailableServiceTypes();
}
```

### D7: 配置文件格式
**决策**: 使用JSON格式

```json
{
  "name": "客户A-制程X配置",
  "version": "1.0",
  "createdAt": "2024-01-01T00:00:00Z",
  "columnMapping": {
    "sourceColumns": {
      "project": 0,
      "specification": 1,
      "acceptance": 2,
      "remark": 3
    },
    "targetColumns": {
      "project": 0,
      "specification": 1,
      "acceptance": 2,
      "remark": 3
    }
  },
  "matchingConfig": {
    "method": "embedding",
    "threshold": 0.8,
    "similarityAlgorithm": "cosine",
    "searchColumns": ["project", "specification"],
    "fillColumns": ["acceptance", "remark"]
  },
  "aiServiceConfigId": 1
}
```

### D8: 得分计算展示
**决策**: 预览界面显示详细得分明细

| 展示项 | 说明 |
|--------|------|
| 匹配方式 | 相似度/Embedding/LLM+Embedding |
| AI服务类型 | 在线(OpenAI/Azure) / 本地(Ollama/LMStudio) |
| 原始文本 | 查询的【项目+规格】 |
| 匹配文本 | 数据库中的【项目+规格】 |
| 分项得分 | 项目相似度、规格相似度 |
| 综合得分 | 加权平均后的最终得分 |
| 阈值状态 | 是否达到阈值，达到显示绿色，否则红色 |
| 算法说明 | 使用的具体算法和参数 |

## Risks / Trade-offs

### R1: 本地Embedding性能
**风险**: 本地Embedding模型可能较慢
**缓解**:
- 预计算并缓存向量
- 支持批量处理时的进度显示和取消
- 推荐用户使用GPU加速
- 提供模型选择建议（如小模型用于快速匹配）

### R2: 合并单元格处理复杂性
**风险**: 嵌套表格+合并单元格的组合场景可能出现边界情况
**缓解**:
- 导入时将合并单元格拆分为多行，每行填充相同内容
- 填充时检测原表格结构，恢复合并
- 提供预览功能，用户可手动调整

### R3: LLM调用成本（在线服务）
**风险**: 大批量处理时在线LLM调用成本高
**缓解**:
- 优先使用Embedding匹配
- LLM仅在Embedding得分不确定时介入
- 用户可选择纯相似度匹配（免费）
- **推荐使用本地私有化部署降低成本**

### R4: 数据一致性
**风险**: 重复导入可能导致数据混乱
**缓解**:
- 导入前检测文件hash，提示重复
- 提供覆盖/追加选项
- 记录操作历史，支持撤销
- **EF Core事务保证数据一致性**

### R5: EF Core迁移管理
**风险**: Schema变更可能导致数据丢失
**缓解**:
- 使用EF Core Migrations管理变更
- 发布前备份数据库
- 提供数据导出/导入功能

### R6: 本地AI服务可用性
**风险**: 用户未正确配置本地AI服务
**缓解**:
- 提供连接测试功能
- 显示详细错误信息和解决建议
- 本地服务不可用时自动降级到相似度匹配

## Migration Plan

### 数据库迁移策略

```bash
# 初始迁移
dotnet ef migrations add InitialCreate

# 应用迁移
dotnet ef database update

# 生成SQL脚本（用于生产环境）
dotnet ef migrations script
```

### 版本升级流程
1. 备份现有数据库
2. 运行新版本迁移
3. 验证数据完整性
4. 回滚机制：保留备份，支持降级

## Open Questions

1. **Q**: 是否需要支持批量导出历史数据？
   **A**: 待定，可作为后续功能

2. **Q**: 向量缓存是否需要定期清理？
   **A**: 暂不需要，SQLite文件大小可控；可在设置中提供清理功能

3. **Q**: 是否需要支持多语言（英文）？
   **A**: 否，仅中文界面

4. **Q**: 本地私有化部署是否需要提供安装向导？
   **A**: 提供文档说明，Ollama/LM Studio安装由用户自行完成
