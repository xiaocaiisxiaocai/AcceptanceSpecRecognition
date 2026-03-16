# 验收规格管理系统 — 设计概要

> **版本**: v1.0 | **日期**: 2026-03-02 | **状态**: 当前实现

---

## 目录

1. [系统定位与业务目标](#1-系统定位与业务目标)
2. [整体架构](#2-整体架构)
3. [后端分层架构](#3-后端分层架构)
4. [核心模块设计](#4-核心模块设计)
5. [数据模型设计](#5-数据模型设计)
6. [API 设计](#6-api-设计)
7. [前端架构](#7-前端架构)
8. [设计模式总览](#8-设计模式总览)
9. [测试策略](#9-测试策略)
10. [部署方案](#10-部署方案)
11. [变更管理](#11-变更管理-openspec)
12. [关键技术决策](#12-关键技术决策)

---

## 1. 系统定位与业务目标

### 1.1 核心使命

帮助企业验收工程师从历史 Word/Excel 文档中提取验收规格数据，并通过 **AI 智能匹配**自动填充到新文档中，大幅降低人工查找和手动填写的成本。

### 1.2 核心业务流程

```mermaid
graph LR
    A[上传历史文档<br/>Word / Excel] --> B[解析表格<br/>提取规格数据]
    B --> C[入库存储<br/>客户→制程→规格]
    C --> D[上传新文档<br/>待填充]
    D --> E[AI 智能匹配<br/>Embedding + LLM]
    E --> F[预览确认<br/>人工审核]
    F --> G[执行填充<br/>生成结果文档]
    G --> H[下载<br/>填充后文档]
```

### 1.3 核心数据模型

按 **客户 (Customer) → 机型 (MachineModel) → 制程 (Process)** 层级组织验收规格：

- **查找键**：`项目 (Project) + 规格 (Specification)` — 用于匹配源文本
- **填充目标**：`验收标准 (Acceptance) + 备注 (Remark)` — 匹配后写入新文档

---

## 2. 整体架构

### 2.1 系统部署拓扑

```mermaid
graph TB
    subgraph 前端
        FE[Vue 3 SPA<br/>Nginx :8080]
    end

    subgraph 后端
        API[ASP.NET Core 8<br/>Web API :5014]
    end

    subgraph 数据层
        DB[(MySQL 8.0<br/>:3306)]
        FS[文件系统<br/>uploads/]
    end

    subgraph AI 服务
        OAI[OpenAI]
        AZURE[Azure OpenAI]
        OLLAMA[Ollama<br/>本地]
        LMS[LM Studio<br/>本地]
    end

    FE -- "/api/*" --> API
    API -- "EF Core" --> DB
    API -- "读写文件" --> FS
    API -- "Semantic Kernel" --> OAI
    API -- "Semantic Kernel" --> AZURE
    API -- "Semantic Kernel" --> OLLAMA
    API -- "Semantic Kernel" --> LMS
```

### 2.2 开发 vs 生产环境

```
开发环境:
  Vite (:8848) ──proxy /api──▶ ASP.NET Core (:5014) ──▶ MySQL (:3306)

生产环境 (Docker Compose):
  Nginx (:8080) ──proxy /api──▶ ASP.NET Core (:5014) ──▶ MySQL (:3306)
  │                              │
  └── 静态资源 (Vue dist)         └── uploads/ (Docker Volume)
```

---

## 3. 后端分层架构

### 3.1 项目依赖关系

```mermaid
graph TD
    API["AcceptanceSpecSystem.Api<br/>HTTP 入口层<br/>控制器 / DTO / 中间件 / DI"]
    CORE["AcceptanceSpecSystem.Core<br/>核心业务层<br/>AI / Matching / TextProcessing / Documents"]
    DATA["AcceptanceSpecSystem.Data<br/>数据访问层<br/>EF Core / Entities / Migrations / Repository"]

    API --> CORE
    API --> DATA
    CORE --> DATA

    style API fill:#4CAF50,color:#fff
    style CORE fill:#2196F3,color:#fff
    style DATA fill:#FF9800,color:#fff
```

### 3.2 各层职责

| 层次 | 项目 | 职责 | 关键技术 |
|------|------|------|---------|
| **API 层** | `AcceptanceSpecSystem.Api` | HTTP 路由、请求校验、DTO 转换、DI 注册、中间件 | ASP.NET Core 8, Swagger |
| **Core 层** | `AcceptanceSpecSystem.Core` | AI 编排、匹配算法、文本预处理、文档解析与写入 | Semantic Kernel 1.68, OpenXml, ClosedXML |
| **Data 层** | `AcceptanceSpecSystem.Data` | 实体定义、DbContext、Repository、迁移 | EF Core 8, Pomelo MySQL |

**依赖规则**：
- Core 层**不依赖** API 层 → 可独立单元测试
- 控制器**不直接操作** DbContext → 通过 `IUnitOfWork` 抽象
- 启动时 `DatabaseInitializer` 自动应用待执行迁移（Testing 环境跳过）

### 3.3 DI 注册全景

```mermaid
graph LR
    subgraph 单例 Singleton
        FSS[IFileStorageService]
        DSF[DocumentServiceFactory]
        SKF[ISemanticKernelServiceFactory]
        TSS[ITextSimilarityService]
    end

    subgraph 作用域 Scoped
        UOW[IUnitOfWork]
        AIS[AiServiceSelector]
        EMB[IEmbeddingService]
        MAT[IMatchingService]
        LLM_R[ILlmReviewService]
        LLM_S[ILlmSuggestionService]
        TPP[ITextPreprocessingPipeline]
    end

    subgraph 基础设施
        DBC[AppDbContext<br/>MySQL]
        HC[HttpClient]
        DP[DataProtection]
        CORS[CORS Policy]
    end
```

---

## 4. 核心模块设计

### 4.1 文档处理模块 (Core/Documents)

**设计模式**：工厂模式 + 策略模式

```mermaid
classDiagram
    class DocumentServiceFactory {
        +GetParser(filePath) IDocumentParser
        +GetParser(type) IDocumentParser
        +GetWriter(filePath) IDocumentWriter
        +GetWriter(type) IDocumentWriter
        +GetSupportedTypes() IReadOnlyList
        +IsSupported(filePath) bool
    }

    class IDocumentParser {
        <<interface>>
        +DocumentType DocumentType
        +CanParse(filePath) bool
        +GetTablesAsync(stream) Task~TableInfo[]~
        +ExtractTableDataAsync(stream, tableIndex) Task~TableData~
    }

    class IDocumentWriter {
        <<interface>>
        +DocumentType DocumentType
        +CanWrite(filePath) bool
        +FillAndGetBytesAsync(stream, mapping, fillOps) Task~byte[]~
        +FillAndSaveAsync(templatePath, outputPath, mapping, fillOps) Task~string~
    }

    class WordDocumentParser {
        OpenXml SDK
    }
    class ExcelDocumentParser {
        ClosedXML
    }
    class WordDocumentWriter {
        OpenXml SDK
    }

    DocumentServiceFactory --> IDocumentParser
    DocumentServiceFactory --> IDocumentWriter
    IDocumentParser <|.. WordDocumentParser
    IDocumentParser <|.. ExcelDocumentParser
    IDocumentWriter <|.. WordDocumentWriter
```

**核心数据模型**：

```mermaid
classDiagram
    class TableInfo {
        +int Index
        +string Name
        +int RowCount
        +int ColumnCount
        +List~string~ Headers
        +bool HasMergedCells
    }

    class TableData {
        +int TableIndex
        +List~string~ Headers
        +List~RowData~ Rows
        +int ColumnCount
    }

    class ColumnMapping {
        +int ProjectColumn
        +int SpecificationColumn
        +int AcceptanceColumn
        +int RemarkColumn
        +int HeaderRowIndex
        +int DataStartRowIndex
    }

    class FillOperations {
        +List~CellWriteOperation~ Operations
    }

    class CellWriteOperation {
        +int RowIndex
        +int ColumnIndex
        +string Value
    }

    TableData --> RowData
    FillOperations --> CellWriteOperation
```

### 4.2 匹配引擎模块 (Core/Matching)

**Embedding 匹配 + LLM 辅助策略**：

```mermaid
flowchart TD
    START[源文本: 项目 + 规格] --> EMB{Embedding<br/>服务可用?}

    EMB -- 是 --> VEC[1. Embedding 向量匹配<br/>余弦相似度]
    EMB -- 否 --> FAIL[返回错误<br/>Embedding 服务不可用]

    VEC --> SCORE[计算综合得分]

    SCORE --> THR{得分 ≥ 阈值?}
    THR -- 否 --> FILTER[过滤掉]

    THR -- 是 --> LLM_EN{启用 LLM<br/>辅助?}
    LLM_EN -- 否 --> RESULT[返回匹配结果]

    LLM_EN -- 是 --> LLM_R[3a. LLM 复核<br/>验证匹配正确性<br/>返回 0-100 分]
    LLM_EN -- 是 --> LLM_S[3b. LLM 建议<br/>低分时生成填充建议<br/>返回验收+备注+理由]

    LLM_R --> RESULT
    LLM_S --> RESULT

    style VEC fill:#4CAF50,color:#fff
    style FAIL fill:#F44336,color:#fff
    style LLM_R fill:#9C27B0,color:#fff
    style LLM_S fill:#9C27B0,color:#fff
```

**置信度分级**：

| 等级 | 分数范围 | 颜色 | 处理策略 |
|------|---------|------|---------|
| 高置信 | ≥ 0.8 | 🟢 | 推荐自动填充 |
| 中置信 | 0.6 ~ 0.8 | 🟡 | 建议填充，用户确认 |
| 低置信 | < 0.6 | 🔴 | 触发 LLM 建议（可选） |

**匹配结果模型**：

```mermaid
classDiagram
    class MatchResult {
        +string SourceText
        +string MatchedText
        +int? MatchedSpecId
        +string? MatchedAcceptance
        +string? MatchedRemark
        +double Score
        +Dictionary~string,double~ ScoreDetails
        +double? LlmScore
        +string? LlmReason
        +bool IsHighConfidence
        +bool IsMediumConfidence
        +bool IsLowConfidence
    }

    class MatchCandidate {
        +int SpecId
        +string Project
        +string Specification
        +string? Acceptance
        +string? Remark
        +string CombinedText
        +float[]? Embedding
    }

    class MatchingConfig {
        +int? EmbeddingServiceId
        +int? LlmServiceId
        +double MinScoreThreshold = 0.3
        +bool UseLlmReview = false
        +bool UseLlmSuggestion = false
        +double LlmSuggestionScoreThreshold = 0.6
        +int LlmParallelism = 3
    }
```

**Embedding 缓存机制**：

```mermaid
sequenceDiagram
    participant M as MatchingService
    participant E as EmbeddingService
    participant DB as EmbeddingCache 表
    participant AI as AI 服务

    M->>E: GenerateEmbeddingAsync(text, serviceId)
    E->>DB: 查询缓存 (SpecId + ModelName)
    alt 缓存命中
        DB-->>E: 返回 Vector (byte[])
        E-->>M: float[] 向量
    else 缓存未命中
        E->>AI: 调用 Embedding API
        AI-->>E: float[] 向量
        E->>DB: 写入缓存
        E-->>M: float[] 向量
    end
```

### 4.3 AI 服务管理 (Core/AI/SemanticKernel)

**设计模式**：工厂模式 + 服务选择器

```mermaid
classDiagram
    class ISemanticKernelServiceFactory {
        <<interface>>
        +CreateChatCompletionService(config) IChatCompletionService
        +CreateEmbeddingGenerator(config) IEmbeddingGenerator
    }

    class SemanticKernelServiceFactory {
        -ConcurrentDictionary cache
        -BuildOpenAIClient(config) OpenAIClient
        -BuildCacheKey(config) string
    }

    class AiServiceSelector {
        +GetCandidatesAsync(purpose, preferredId?) Task~List~
        -离线优先排序
        -Priority 字段排序
    }

    class AiServiceConfig {
        +string Name
        +AiServiceType ServiceType
        +AiServicePurpose Purpose
        +int Priority
        +string? ApiKey [加密]
        +string? Endpoint
        +string? EmbeddingModel
        +string? LlmModel
    }

    ISemanticKernelServiceFactory <|.. SemanticKernelServiceFactory
    AiServiceSelector --> AiServiceConfig
    SemanticKernelServiceFactory --> AiServiceConfig
```

**多提供商支持矩阵**：

| 提供商 | 类型 | LLM | Embedding | 协议 | 优先级 |
|--------|------|-----|-----------|------|--------|
| OpenAI | 云端 | ✅ | ✅ | OpenAI API | 默认 |
| Azure OpenAI | 云端 | ✅ | ✅ | Azure API | 默认 |
| Ollama | 本地 | ✅ | ✅ | OpenAI 兼容 | 离线优先 |
| LM Studio | 本地 | ✅ | ✅ | OpenAI 兼容 | 离线优先 |
| 自定义兼容 | 任意 | ✅ | ✅ | OpenAI 兼容 | 自定义 |

**服务选择流程**：

```mermaid
flowchart TD
    A[请求 AI 服务<br/>purpose = LLM / Embedding] --> B[按用途过滤]
    B --> C[离线服务优先<br/>Ollama > LM Studio]
    C --> D[Priority 排序<br/>越小越优先]
    D --> E[时间排序<br/>UpdatedAt / CreatedAt]
    E --> F{指定优选 ID?}
    F -- 是 --> G[优选 ID 移到首位]
    F -- 否 --> H[返回候选列表]
    G --> H
```

### 4.4 文本预处理管道 (Core/TextProcessing)

**设计模式**：管道模式 (Pipeline)

```mermaid
flowchart LR
    INPUT[原始文本] --> S1[Step 1<br/>空白符规范化<br/>\\r\\n → 空格<br/>连续空白合并]
    S1 --> S2[Step 2<br/>简繁转换<br/>OpenCCNET<br/>简体 ↔ 台湾繁体]
    S2 --> S3[Step 3<br/>同义词替换<br/>分词逐词匹配<br/>避免子串问题]
    S3 --> S4[Step 4<br/>OK/NG 标准化<br/>OK → 良好<br/>NG → 不良]
    S4 --> OUTPUT[标准化文本]

    style S1 fill:#E3F2FD
    style S2 fill:#F3E5F5
    style S3 fill:#E8F5E9
    style S4 fill:#FFF3E0
```

```mermaid
classDiagram
    class ITextPreprocessingPipeline {
        <<interface>>
        +CreateSessionAsync() Task~TextProcessingSession~
    }

    class TextProcessingSession {
        +TextProcessingConfig Config
        +Process(text?) string
    }

    class DefaultTextPreprocessingPipeline {
        -IUnitOfWork unitOfWork
        -IChineseConversionService chineseConversion
        -IOkNgConversionService okNgConversion
        -ISynonymService synonymService
    }

    class IChineseConversionService {
        <<interface>>
        +Convert(text, mode) string
    }

    class IOkNgConversionService {
        <<interface>>
        +NormalizeOkNg(text, okFormat, ngFormat) string
    }

    ITextPreprocessingPipeline <|.. DefaultTextPreprocessingPipeline
    DefaultTextPreprocessingPipeline --> TextProcessingSession
    DefaultTextPreprocessingPipeline --> IChineseConversionService
    DefaultTextPreprocessingPipeline --> IOkNgConversionService
```

---

## 5. 数据模型设计

### 5.1 实体关系图 (ER Diagram)

```mermaid
erDiagram
    Customer ||--o{ AcceptanceSpec : "拥有"
    Process ||--o{ AcceptanceSpec : "关联(可选)"
    MachineModel ||--o{ AcceptanceSpec : "关联(可选)"
    WordFile ||--o{ AcceptanceSpec : "来源"
    AcceptanceSpec ||--o{ EmbeddingCache : "缓存向量"

    Customer {
        int Id PK
        string Name UK "客户名称(唯一)"
        datetime CreatedAt
    }

    Process {
        int Id PK
        string Name "制程名称"
        datetime CreatedAt
    }

    MachineModel {
        int Id PK
        string Name "机型名称"
        datetime CreatedAt
    }

    AcceptanceSpec {
        int Id PK
        int CustomerId FK "必填"
        int ProcessId FK "可选"
        int MachineModelId FK "可选"
        string Project "项目(查找键)"
        string Specification "规格(查找键)"
        string Acceptance "验收标准(填充目标)"
        string Remark "备注(填充目标)"
        int WordFileId FK "来源文件"
        datetime ImportedAt
    }

    WordFile {
        int Id PK
        string FileName "原始文件名"
        int FileType "Word/Excel"
        string FilePath "相对路径(新)"
        binary FileContent "二进制(兼容)"
        string FileHash UK "SHA256"
        datetime UploadedAt
    }

    EmbeddingCache {
        int Id PK
        int SpecId FK
        string ModelName "模型名称"
        binary Vector "float[] 序列化"
        datetime CreatedAt
    }
```

### 5.2 配置实体关系

```mermaid
erDiagram
    AiServiceConfig {
        int Id PK
        string Name UK "服务名称"
        int ServiceType "OpenAI/Azure/Ollama/LMStudio/Custom"
        int Purpose "Flags: LLM=1, Embedding=2"
        int Priority "越小越优先"
        string ApiKey "DataProtection 加密"
        string Endpoint "服务端点"
        string EmbeddingModel "Embedding 模型"
        string LlmModel "LLM 模型"
    }

    SynonymGroup ||--o{ SynonymWord : "包含"
    SynonymGroup {
        int Id PK
        string Name "同义词组名"
    }
    SynonymWord {
        int Id PK
        int GroupId FK
        string Word "同义词"
        bool IsStandard "是否标准词"
    }

    TextProcessingConfig {
        int Id PK
        bool EnableChineseConversion
        int ConversionMode
        bool EnableOkNg
        string OkFormat
        string NgFormat
        bool EnableSynonym
    }

    PromptTemplate {
        int Id PK
        string Name UK "模板名称"
        string Template "含 placeholder"
        string Description
    }

    ColumnMappingRule {
        int Id PK
        string HeaderPattern "表头匹配模式"
        int MatchMode "Contains/Equals/Regex"
        int TargetField "Project/Specification/Acceptance/Remark"
        int Priority "越小越优先"
        bool IsEnabled
    }

    OperationHistory {
        int Id PK
        int OperationType "Import/Fill/Delete"
        string TargetFile
        string Details "JSON"
        bool CanUndo
        string UndoData "JSON"
        datetime CreatedAt
    }
```

### 5.3 核心索引策略

| 实体 | 索引 | 类型 |
|------|------|------|
| `AcceptanceSpec` | `(CustomerId, ProcessId, MachineModelId)` | 复合索引 |
| `EmbeddingCache` | `(SpecId, ModelName)` | 复合唯一索引 |
| `WordFile` | `FileHash` | 唯一索引 |
| `AiServiceConfig` | `Name` | 唯一索引 |
| `Customer` | `Name` | 唯一索引 |

---

## 6. API 设计

### 6.1 统一响应格式

```json
{
  "code": 0,
  "message": "操作成功",
  "data": { "..." }
}
```

**错误码映射**：

| 异常类型 | HTTP 状态码 | 说明 |
|---------|------------|------|
| `ArgumentException` | 400 | 参数校验失败 |
| `InvalidOperationException` | 400 | 业务规则违反 |
| `KeyNotFoundException` | 404 | 资源不存在 |
| `UnauthorizedAccessException` | 401 | 未授权 |
| 其他异常 | 500 | 内部服务器错误 |

### 6.2 端点全景

```mermaid
mindmap
  root((API 端点))
    文档管理
      POST /api/documents/upload
      POST /api/documents/import
      POST /api/documents/excel/import
      GET /api/documents/:id/tables
      GET /api/documents/:id/tables/:idx/preview
      DELETE /api/documents/:id
    智能匹配
      POST /api/matching/preview
      POST /api/matching/execute
      GET /api/matching/download/:taskId
      GET /api/matching/status/:taskId
    验收规格
      GET /api/specs/summary
      GET /api/specs（分页与筛选）
      GET /api/specs/:id
      PUT /api/specs/:id
      DELETE /api/specs/:id
    基础数据
      CRUD /api/customers
      CRUD /api/processes
      CRUD /api/machine-models
    配置管理
      CRUD /api/ai-services
      GET, PUT /api/text-processing
      CRUD /api/prompt-templates
      CRUD /api/column-mapping-rules
      CRUD /api/synonyms
      CRUD /api/keywords
    辅助
      GET /api/history
      POST /api/file-compare
      GET /health
      GET /swagger
```

### 6.3 核心端点详解

#### 文档处理流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant FE as 前端
    participant DC as DocumentsController
    participant FS as FileStorageService
    participant DP as DocumentParser
    participant DB as Database

    U->>FE: 选择文件
    FE->>DC: POST /upload (multipart)
    DC->>FS: SaveUploadedWordAsync()
    DC->>DB: 创建 WordFile 记录
    DC-->>FE: { fileId, tableCount }

    FE->>DC: GET /{fileId}/tables
    DC->>DP: GetTablesAsync(stream)
    DC-->>FE: [{ index, rowCount, headers }]

    FE->>DC: GET /{fileId}/tables/0/preview
    DC->>DP: ExtractTableDataAsync(stream, 0)
    DC-->>FE: { headers, rows }

    U->>FE: 确认列映射 + 选客户/制程
    FE->>DC: POST /import { fileId, customerId, mapping }
    DC->>DP: ExtractTableDataAsync(stream, mapping)
    loop 每一行
        DC->>DB: 创建 AcceptanceSpec
    end
    DC->>DB: 记录 OperationHistory
    DC-->>FE: { importedCount, errors }
```

#### 匹配填充流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant FE as 前端
    participant MC as MatchingController
    participant MS as MatchingService
    participant ES as EmbeddingService
    participant LLM as LlmAssistService
    participant DW as DocumentWriter

    U->>FE: 上传待填充文档 + 配置
    FE->>MC: POST /preview { fileId, tableIndex, config }
    MC->>MS: FindMatchesAsync(sourceTexts, candidates, config)

    MS->>ES: GenerateEmbeddingsAsync(texts)
    ES-->>MS: float[][] 向量
    MS->>MS: 余弦相似度计算
    Note over MC,MS: 若 Embedding 服务不可用，直接返回 400 错误

    opt LLM 复核启用
        MS->>LLM: ReviewMatchAsync(result)
        LLM-->>MS: { score: 85, reason: "..." }
    end

    opt LLM 建议启用
        MS->>LLM: GenerateSuggestionAsync(source, candidates)
        LLM-->>MS: { acceptance: "...", remark: "..." }
    end

    MC-->>FE: [{ sourceRow, bestMatch, score, llmSuggestion }]

    U->>FE: 确认填充内容
    FE->>MC: POST /execute { fileId, mappings }
    MC->>DW: FillAndGetBytesAsync(template, mapping, fillOps)
    DW-->>MC: byte[] 填充后文档
    MC-->>FE: { taskId }

    FE->>MC: GET /download/{taskId}
    MC-->>FE: 二进制文件流 (.docx)
```

---

## 7. 前端架构

### 7.1 技术栈

| 层次 | 技术选型 | 版本 |
|------|---------|------|
| 框架 | Vue 3 (Composition API) | 3.5.22 |
| 语言 | TypeScript | 5.9.3 |
| 构建 | Vite + pnpm | 7.1.12 |
| UI 库 | Element Plus | 2.11.5 |
| 样式 | Tailwind CSS | 4.1.16 |
| 状态 | Pinia | 3.0.3 |
| HTTP | Axios（封装 PureHttp） | 1.12.2 |
| 表格 | @pureadmin/table | 3.3.0 |
| 模板 | Pure Admin Thin | — |

### 7.2 前端目录结构

```
web/src/
├── main.ts                         应用入口
├── App.vue                         根组件 (ElConfigProvider 中文化)
├── api/                            API 封装层 (16 个模块)
│   ├── customer.ts                 客户 API
│   ├── process.ts                  制程 API
│   ├── machine-model.ts            机型 API
│   ├── spec.ts                     规格 API
│   ├── document.ts                 文档 API
│   ├── matching.ts                 匹配 API (长超时 300s)
│   ├── ai-service.ts               AI 服务 API
│   ├── text-processing.ts          文本处理 API
│   ├── prompt-template.ts          Prompt 模板 API
│   ├── column-mapping-rules.ts     列映射规则 API
│   ├── synonym.ts                  同义词 API
│   ├── keyword.ts                  关键词 API
│   ├── history.ts                  历史 API
│   ├── file-compare.ts             文件对比 API
│   └── user.ts                     用户认证 API
├── router/modules/                 路由模块
├── store/modules/                  Pinia 状态 (app/user/permission/...)
├── views/                          页面视图
├── components/                     全局可复用组件
├── directives/                     自定义指令 (auth/perms/copy/...)
├── layout/                         全局布局 (侧边栏/导航栏/标签页)
├── utils/                          工具函数 (http/auth/message/...)
└── style/                          全局样式 (theme/dark/element-plus/...)
```

### 7.3 功能模块地图

```mermaid
graph TB
  subgraph DASHBOARD["仪表盘"]
    DASH["/dashboard<br/>系统概览·统计数据"]
  end

  subgraph BASE_DATA["基础数据 /base-data"]
    CUST[客户管理]
    PROC[制程管理]
    MACH[机型管理]
    SPEC["验收规格<br/>分组树 + 数据表"]
  end

  subgraph DATA_IMPORT["数据导入 /data-import"]
    IMP["5 步向导<br/>上传 → 选表格 → 列映射<br/>→ 选目标 → 确认导入"]
  end

  subgraph SMART_FILL["智能填充 /smart-fill"]
    FILL["4 步向导<br/>上传 → 选表格 → 配置匹配<br/>→ 预览确认<br/>支持: 单表/批量/SSE 流式"]
  end

  subgraph CONFIG["配置管理 /config"]
    AI_CFG[AI 服务配置]
    TXT_CFG[文本处理配置]
    PMT_CFG["Prompt 模板"]
    COL_CFG[列映射规则]
  end

  subgraph AUX["辅助功能"]
    FC["文件对比<br/>/file-compare"]
    SYN[同义词管理]
    KW[关键词管理]
    HIS["操作历史<br/>支持撤销"]
  end

  DASH --- CUST
  CUST --- IMP
  IMP --- FILL
  FILL --- AI_CFG
  AI_CFG --- FC
```

### 7.4 智能填充交互流程

```mermaid
stateDiagram-v2
    [*] --> 上传文件: 拖拽或点击上传
    上传文件 --> 选择表格: 获取表格列表
    选择表格 --> 配置匹配: 设置列映射 + AI 参数

    state 配置匹配 {
        [*] --> 选择范围: 客户/制程/机型
        选择范围 --> 设置阈值: 最小匹配分数
        设置阈值 --> 选择AI: Embedding + LLM 选项
        选择AI --> 设置并行度: LLM 并发数
    }

    配置匹配 --> 预览结果: POST /matching/preview

    state 预览结果 {
        [*] --> 查看匹配: 源数据 vs 最佳匹配
        查看匹配 --> 查看得分: 综合分 + Embedding 得分明细
        查看得分 --> 查看LLM: LLM 复核 + 建议
        查看LLM --> 确认修改: 手动调整填充内容
    }

    预览结果 --> 执行填充: POST /matching/execute
    执行填充 --> 下载结果: GET /matching/download
    下载结果 --> [*]
```

### 7.5 前端关键设计

| 特性 | 实现方式 | 说明 |
|------|---------|------|
| **多步向导** | `currentStep` ref + `canGoNext` computed | 降低用户认知负担 |
| **列映射自动识别** | `ColumnMappingRule` 优先级匹配 | Contains / Equals / Regex 三种模式 |
| **SSE 流式** | EventSource + AbortController | LLM 实时进度推送 |
| **长超时** | Axios 300s + Vite proxy timeout=0 | 适配 AI 长耗时请求 |
| **Token 刷新** | PureHttp 拦截器队列 | 无感刷新，请求不丢失 |
| **权限控制** | v-auth / v-perms 指令 + Pinia | 页面级 + 按钮级 |
| **主题切换** | epTheme store + CSS 变量 | 深色/浅色模式 |

---

## 8. 设计模式总览

```mermaid
graph TB
    subgraph 创建型模式
        F1[工厂模式<br/>DocumentServiceFactory<br/>SemanticKernelServiceFactory]
    end

    subgraph 结构型模式
        S1[适配器模式<br/>多格式文档解析]
        S2[缓存模式<br/>EmbeddingCache<br/>ServiceFactory 实例缓存]
    end

    subgraph 行为型模式
        B1[策略模式<br/>IDocumentParser<br/>IMatchingService]
        B2[管道模式<br/>TextPreprocessingPipeline]
        B3[服务选择器<br/>AiServiceSelector]
    end

    subgraph 架构模式
        A1[仓储模式<br/>IRepository + 特化 Repository]
        A2[工作单元<br/>IUnitOfWork]
        A3[显式失败模式<br/>Embedding 不可用即返回错误]
    end
```

| 模式 | 应用位置 | 解决的问题 |
|------|---------|-----------|
| **工厂模式** | `DocumentServiceFactory`, `SemanticKernelServiceFactory` | 解耦创建逻辑，支持多格式/多 AI 提供商 |
| **策略模式** | `IDocumentParser`, `IMatchingService` | 算法可替换（Word/Excel、Embedding/LLM辅助） |
| **仓储模式** | `IRepository<T>` + 12 个特化 Repository | 数据访问抽象，屏蔽 EF Core 细节 |
| **工作单元** | `IUnitOfWork` (13 个 Repository 聚合) | 事务管理、SaveChanges 统一 |
| **管道模式** | `DefaultTextPreprocessingPipeline` | 文本处理步骤解耦、可配置 |
| **显式失败模式** | `SemanticKernelMatchingService` | Embedding 不可用时直接返回明确错误 |
| **缓存模式** | `EmbeddingCache` 表, `ConcurrentDictionary` | 避免重复 AI 调用和连接创建 |
| **服务选择器** | `AiServiceSelector` | 动态选择最优 AI 服务（离线优先 + 优先级排序） |
| **适配器模式** | `DocumentServiceFactory` | 统一不同格式文档的解析/写入接口 |

---

## 9. 测试策略

### 9.1 测试金字塔

```mermaid
graph TB
    subgraph "E2E 测试 (tools/E2ETest)"
        E2E[控制台 CLI<br/>真实 HTTP 流程<br/>上传→导入→预览→填充→下载]
    end

    subgraph "API 集成测试 (Api.Tests)"
        API_T[13 个测试类<br/>WebApplicationFactory + SQLite<br/>覆盖完整业务流程]
    end

    subgraph "Core 单元测试 (Core.Tests)"
        CORE_T[匹配算法测试<br/>文档解析/写入测试<br/>异常路径测试]
    end

    subgraph "Data 仓储测试 (Data.Tests)"
        DATA_T[Repository CRUD<br/>UnitOfWork 事务<br/>EF Core InMemory]
    end

    E2E --> API_T --> CORE_T --> DATA_T

    style E2E fill:#F44336,color:#fff
    style API_T fill:#FF9800,color:#fff
    style CORE_T fill:#4CAF50,color:#fff
    style DATA_T fill:#2196F3,color:#fff
```

### 9.2 测试基础设施

```mermaid
classDiagram
    class ApiWebApplicationFactory {
        -SQLite InMemory 替换 MySQL
        -TestFileStorageService 替换文件存储
        -TestEmbeddingService 替换 Embedding
        -TestLlmReviewService 替换 LLM 复核
        -TestLlmSuggestionService 替换 LLM 建议
        -Ephemeral DataProtection
        +CreateClient() HttpClient
    }

    class TestEmbeddingService {
        +GenerateEmbeddingAsync() float[]
        确定性: 字符桶化生成 16 维向量
    }

    class TestLlmReviewService {
        +ReviewMatchAsync() LlmReviewResult
        固定: score=0.4, reason, commentary
    }

    class TestLlmSuggestionService {
        +GenerateSuggestionAsync() LlmSuggestionResult
        固定: acceptance, remark, reason
    }

    ApiWebApplicationFactory --> TestEmbeddingService
    ApiWebApplicationFactory --> TestLlmReviewService
    ApiWebApplicationFactory --> TestLlmSuggestionService
```

### 9.3 测试覆盖矩阵

| 层次 | 测试项目 | 数据库 | 模式 | 测试文件数 |
|------|---------|--------|------|-----------|
| **API** | `Api.Tests` | SQLite InMemory | `WebApplicationFactory` + `IClassFixture` | 13 |
| **Core** | `Core.Tests` | 无（纯单元） | 依赖注入 + Mock | 5 |
| **Data** | `Data.Tests` | EF InMemory | `TestBase` 抽象基类 | 3 |
| **E2E** | `tools/E2ETest` | 真实服务器 | Console CLI + HTTP | 1 |

---

## 10. 部署方案

### 10.1 Docker Compose 架构

```mermaid
graph LR
    subgraph Docker Network
        NGINX["acceptance-web<br/>Nginx :80<br/>(暴露 :8080)"]
        API_D["acceptance-api<br/>ASP.NET Core :8080<br/>(暴露 :5014)"]
        MYSQL["acceptance-mysql<br/>MySQL 8.0 :3306"]
    end

    subgraph Volumes
        V1[(mysql_data)]
        V2[(api_uploads)]
    end

    NGINX -- "/api/*" --> API_D
    API_D -- "EF Core" --> MYSQL
    MYSQL --- V1
    API_D --- V2

    style NGINX fill:#4CAF50,color:#fff
    style API_D fill:#2196F3,color:#fff
    style MYSQL fill:#FF9800,color:#fff
```

### 10.2 构建流程

```mermaid
graph TD
  subgraph API_BUILD["API 构建 · 多阶段 Dockerfile"]
    A1["SDK 8.0 镜像"] --> A2["dotnet restore"]
    A2 --> A3["dotnet publish -c Release"]
    A3 --> A4["Runtime 8.0 镜像<br/>:8080"]
  end

  subgraph WEB_BUILD["Web 构建 · 多阶段 Dockerfile"]
    W1["Node 20 Alpine"] --> W2["pnpm install --frozen-lockfile"]
    W2 --> W3["pnpm build"]
    W3 --> W4["Nginx Alpine<br/>:80"]
  end

  subgraph COMPOSE["Compose"]
    DC["docker compose up -d --build"]
    DC --> A1
    DC --> W1
    DC --> MYSQL_INIT["MySQL 8.0<br/>健康检查后启动 API"]
  end
```

### 10.3 环境对照

| 配置项 | 开发环境 | 生产环境 (Docker) |
|--------|---------|-----------------|
| 前端端口 | Vite :8848 | Nginx :8080 |
| 后端端口 | :5014 | :5014 (映射自 :8080) |
| 数据库 | localhost:3306 | acceptance-mysql:3306 |
| API 代理 | Vite proxy | Nginx proxy |
| 文件存储 | 本地 uploads/ | Docker Volume api_uploads |
| 迁移 | 自动应用 | 自动应用 |
| Swagger | 启用 | 启用 |

---

## 11. 变更管理 (OpenSpec)

### 11.1 规格体系

```mermaid
graph TB
    subgraph "openspec/specs/ (当前系统真相)"
        S1[api/spec.md<br/>API 接口规格]
        S2[data-storage/spec.md<br/>数据存储规格]
        S3[file-storage/spec.md<br/>文件存储规格]
        S4[matching-engine/spec.md<br/>匹配引擎规格]
        S5[table-preview/spec.md<br/>表格预览规格]
        S6[user-interface/spec.md<br/>用户界面规格]
    end

    subgraph "openspec/changes/archive/ (已完成变更)"
        C1[refactor-specset-model<br/>数据模型重构]
        C2[add-machine-model<br/>机型管理]
        C3[add-file-compare<br/>文件对比]
        C4[add-excel-import<br/>Excel 导入]
        C5[add-column-mapping-rules<br/>列映射规则]
        C6[refactor-ai-stack-to-sk<br/>Semantic Kernel 重构]
        C7[update-to-web-architecture<br/>Web 架构升级]
        C8[add-llm-matching-assist<br/>LLM 辅助匹配]
    end

    C1 -.->|影响| S2
    C6 -.->|影响| S4
    C7 -.->|影响| S6
    C8 -.->|影响| S4
    C8 -.->|影响| S6
```

### 11.2 变更流程

```mermaid
flowchart LR
    P[提案 proposal.md<br/>动机·影响·范围] --> D[设计 design.md<br/>技术决策]
    D --> T[任务 tasks.md<br/>实施清单]
    T --> I[实施<br/>编码·测试]
    I --> V[验证<br/>规格校验]
    V --> A[归档<br/>archive/]
```

---

## 12. 关键技术决策

| # | 决策 | 选择 | 备选方案 | 理由 |
|---|------|------|---------|------|
| 1 | AI 编排框架 | **Semantic Kernel 1.68** | LangChain.NET, 直接调用 | 原生 .NET，统一多提供商接口，微软官方维护 |
| 2 | 匹配策略 | **Embedding 主匹配（失败即报错）** | 多算法混合匹配 | 保持结果一致性，避免不同算法导致的语义偏差 |
| 3 | 文档处理 | **OpenXml + ClosedXML** | NPOI, Aspose | 无需安装 Office，MIT 开源，跨平台 |
| 4 | 简繁转换 | **OpenCCNET** | 简单字符映射 | 支持台湾用语习惯，非简单一对一映射 |
| 5 | 前端框架 | **Vue 3 + Pure Admin Thin** | React, Angular | 成熟企业管理后台方案，中文社区活跃 |
| 6 | 数据库 | **MySQL 8.0 (Pomelo)** | PostgreSQL, SQLite | 生产级 RDBMS，EF Core Migration 管理 Schema |
| 7 | 向量存储 | **数据库表 (EmbeddingCache)** | Milvus, Qdrant, pgvector | 简单直接，规模可控，无需引入额外基础设施 |
| 8 | 文件存储 | **文件系统（相对路径）** | 数据库 BLOB, MinIO | 便于跨环境迁移，Docker Volume 持久化 |
| 9 | AI 服务选择 | **离线优先 + 优先级排序** | 固定服务 | 支持断网环境，灵活切换提供商 |
| 10 | 文本预处理 | **管道模式（可配置步骤）** | 硬编码处理链 | 步骤可独立开关，运行时加载配置 |

---

## 附录 A：技术栈全景

### 后端依赖

| 包 | 版本 | 用途 |
|----|------|------|
| ASP.NET Core | 8.0 | Web API 框架 |
| EF Core | 8.0.22 | ORM |
| Pomelo.EntityFrameworkCore.MySql | 8.0.3 | MySQL 驱动 |
| Microsoft.SemanticKernel | 1.68.0 | AI 编排 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.68.0 | OpenAI 连接器 |
| DocumentFormat.OpenXml | 3.4.1 | Word 文档处理 |
| ClosedXML | 0.104.2 | Excel 文档处理 |
| OpenCCNET | 1.1.0 | 简繁转换 |
| Swashbuckle.AspNetCore | 6.6.2 | Swagger/OpenAPI |

### 前端依赖

| 包 | 版本 | 用途 |
|----|------|------|
| Vue | 3.5.22 | UI 框架 |
| Vue Router | 4.6.3 | 路由 |
| Pinia | 3.0.3 | 状态管理 |
| Axios | 1.12.2 | HTTP 客户端 |
| Element Plus | 2.11.5 | UI 组件库 |
| Tailwind CSS | 4.1.16 | 原子化样式 |
| TypeScript | 5.9.3 | 类型安全 |
| Vite | 7.1.12 | 构建工具 |
| @pureadmin/table | 3.3.0 | 高级数据表格 |

### 测试依赖

| 包 | 版本 | 用途 |
|----|------|------|
| xUnit | 2.5.3 | 测试框架 |
| FluentAssertions | 8.8.0 | 断言库 |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.22 | 集成测试 |
| EF Core SQLite | 8.0.22 | 测试数据库 |
| EF Core InMemory | 8.0.22 | 单元测试数据库 |
| coverlet.collector | 6.0.0 | 代码覆盖率 |

---

## 附录 B：数据流全景

```mermaid
graph TB
    subgraph 导入阶段
        U1[用户上传 Word/Excel] --> UPLOAD[FileStorageService<br/>保存到文件系统]
        UPLOAD --> PARSE[DocumentParser<br/>解析表格结构]
        PARSE --> MAP[ColumnMapping<br/>列映射配置]
        MAP --> IMPORT[逐行创建 AcceptanceSpec<br/>写入数据库]
        IMPORT --> HIST1[记录 OperationHistory]
    end

    subgraph 匹配阶段
        U2[用户上传待填充文档] --> EXTRACT[提取源文本<br/>项目 + 规格]
        EXTRACT --> CAND[加载候选规格<br/>按客户/制程/机型筛选]
        CAND --> PREPROC[文本预处理管道<br/>简繁·同义词·OK/NG]
        PREPROC --> EMBCHK{Embedding 服务可用?}
        EMBCHK -- 是 --> VEC[向量匹配<br/>余弦相似度]
        VEC --> SCORE[综合得分排序]
        SCORE --> LLM_OPT{LLM 启用?}
        EMBCHK -- 否 --> ERR[返回错误]
        LLM_OPT -- 是 --> LLM_PROC[LLM 复核 + 建议]
        LLM_OPT -- 否 --> PREVIEW
        LLM_PROC --> PREVIEW[返回预览结果]
    end

    subgraph 填充阶段
        PREVIEW --> CONFIRM[用户确认/修改]
        CONFIRM --> FILL_OPS[构建 FillOperations<br/>CellWriteOperation 列表]
        FILL_OPS --> WRITER[WordDocumentWriter<br/>写入目标文档]
        WRITER --> SAVE[保存填充结果<br/>filled-files/]
        SAVE --> DL[用户下载]
        SAVE --> HIST2[记录 OperationHistory]
    end
```

---

> 文档生成时间: 2026-03-02
