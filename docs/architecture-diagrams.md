# 验收规格管理系统 — 核心架构图

> 以下图表使用 [Mermaid](https://mermaid.js.org/) 语法，可直接在支持 Mermaid 的编辑器中渲染。
>
> **文档版本**: v2.3 | **日期**: 2026-04-09 | **状态**: 已按当前代码核对，聚焦架构主链路并补充失败回退与后续流转

---

## 一、当前真实分层

### 1.1 代码分层与实际依赖

```mermaid
graph TD
    subgraph SRC["src/"]
        API["AcceptanceSpecSystem.Api<br/>HTTP 入口 + 当前多数工作流"]
        APP["AcceptanceSpecSystem.Application<br/>基础数据与规格用例"]
        CORE["AcceptanceSpecSystem.Core<br/>AI / 匹配 / 文档处理"]
        DATA["AcceptanceSpecSystem.Data<br/>EF Core / Repository / 持久化"]
    end

    API --> APP
    API -.->|传递引用下直接使用| CORE
    API -.->|传递引用下直接使用| DATA
    APP --> CORE
    APP --> DATA

    style API fill:#4CAF50,color:#fff
    style APP fill:#9C27B0,color:#fff
    style CORE fill:#2196F3,color:#fff
    style DATA fill:#FF9800,color:#fff
```

**当前结论**：
- `Api` 项目仍承载上传、导入、智能填充、严格复用、批量回复等多数工作流。
- `Application` 当前主要是基础数据与规格查询，不是完整业务编排层。
- `Core` 负责文档解析、匹配计算、Embedding、LLM 协作。
- 文档里如果把“所有业务都已经下沉到 Application”画出来，会和当前代码不一致。

### 1.2 核心职责划分

| 层次 | 当前职责 |
|------|---------|
| `AcceptanceSpecSystem.Api` | 控制器入口、权限中间件、以及多数工作流应用服务 |
| `AcceptanceSpecSystem.Application` | 客户、制程、机型、规格等基础数据用例 |
| `AcceptanceSpecSystem.Core` | Word/Excel 解析写回、匹配引擎、Embedding、LLM 能力 |
| `AcceptanceSpecSystem.Data` | `DbContext`、仓储、迁移、持久化实现 |

### 1.3 核心业务闭环图（增强版）

```mermaid
flowchart TD
    START[用户进入业务入口]
    START --> IMP[数据导入]
    START --> SF[智能填充]
    START --> BR[批量回复]

    subgraph 导入链路
        IMP --> IMP1[上传文件]
        IMP1 --> IMP2{上传成功}
        IMP2 -->|否| IMP_ERR1[提示错误并重新上传]
        IMP_ERR1 --> IMP1
        IMP2 -->|是| IMP3[读表 预览 列映射]
        IMP3 --> IMP4[执行导入]
        IMP4 --> IMP5{存在冲突}
        IMP5 -->|是| IMP6[返回差异确认]
        IMP6 --> IMP7[用户确认后再次导入]
        IMP7 --> IMP8[写入规格库]
        IMP5 -->|否| IMP8
    end

    subgraph 智能填充链路
        SF --> SF1[上传待填充文件]
        SF1 --> SF2[读取表格]
        SF2 --> SF3[batch-preview]
        SF3 --> SF4{预览成功}
        SF4 -->|否| SF_ERR1[检查 AI 服务 列配置 或源文件后重试]
        SF_ERR1 --> SF3
        SF4 -->|是| SF5{低置信或冲突}
        SF5 -->|是| SF6[人工确认 或 llm-stream 复核]
        SF5 -->|否| SF7[batch-execute]
        SF6 --> SF7
        SF7 --> SF8{执行成功}
        SF8 -->|否| SF_ERR2[返回预览结果 调整映射后重试]
        SF_ERR2 --> SF3
        SF8 -->|是| SF9[生成结果文件]
        SF9 --> SF10[下载结果]
        SF9 --> SR1[进入严格复用]
        SF9 -.->|处理另一批独立任务| BR1
    end

    subgraph 严格复用链路
        SR1 --> SR2[strict preview]
        SR2 --> SR3{预检通过}
        SR3 -->|否| SR_ERR1[返回错误 可改走批量回复]
        SR_ERR1 -.-> BR1
        SR3 -->|是| SR4[strict execute]
        SR4 --> SR5[下载严格复用结果]
    end

    subgraph 批量回复链路
        BR --> BR1[创建来源会话]
        BR1 --> BR2[上传目标文件]
        BR2 --> BR3[table-preview]
        BR3 --> BR4{预检通过}
        BR4 -->|否| BR_ERR1[调整目标文件或表配置后重试]
        BR_ERR1 --> BR3
        BR4 -->|是| BR5[execute]
        BR5 --> BR6{执行成功}
        BR6 -->|否| BR_ERR2[回到预检结果继续调整]
        BR_ERR2 --> BR3
        BR6 -->|是| BR7[下载批量回复结果]
    end

    IMP8 -.->|规格库可作为候选数据| SF3
```

**增强说明**：
- 失败分支都回到了真实可继续操作的节点，不画“自动神奇恢复”。
- 智能填充成功后的真实后续是：下载结果、进入严格复用、或转入批量回复模块处理另一批文件。
- 从智能填充到批量回复这里画的是“业务流转”，不是同一个 `taskId` 的直接串行执行。

---

## 二、上传链路架构

### 2.1 上传与导入组件关系

```mermaid
flowchart TD
    FE[前端导入页]
    CTRL[DocumentsController]

    FILE[DocumentFileAppService]
    TABLE[DocumentTableAccessService]
    IMPORT[DocumentImportAppService]
    ACCESS[DocumentFileAccessService]
    DUP[ImportDuplicateDetectionService]

    PARSER[DocumentServiceFactory]
    DB[(MySQL)]
    FS[(uploads 文件系统)]

    FE --> CTRL
    CTRL --> FILE
    CTRL --> TABLE
    CTRL --> IMPORT

    FILE --> ACCESS
    FILE --> DB
    ACCESS --> FS

    TABLE --> PARSER
    IMPORT --> ACCESS
    IMPORT --> TABLE
    IMPORT --> DUP
    IMPORT --> DB
```

**当前结论**：
- 上传接口只负责保存文件并创建记录，返回 `tableCountReady=false`。
- 表格读取、预览、正式导入是后续独立请求，不是一次上传内完成。
- 导入阶段会先做重复检测，命中冲突时先阻断并等待前端确认。

### 2.2 上传与导入时序

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端
    participant Ctrl as DocumentsController
    participant FileSvc as DocumentFileAppService
    participant TableSvc as DocumentTableAccessService
    participant ImportSvc as DocumentImportAppService
    participant FileAccess as DocumentFileAccessService
    participant Parser as DocumentServiceFactory
    participant DupSvc as ImportDuplicateDetectionService
    participant FS as uploads
    participant DB as MySQL

    FE->>Ctrl: POST /api/documents/upload
    Ctrl->>FileSvc: UploadFileAsync(scope, file)
    FileSvc->>FileAccess: SaveUploadedFileAsync(...)
    FileAccess->>FS: 保存文件
    FileSvc->>DB: 创建文件记录
    FileSvc-->>Ctrl: { fileId, fileType, tableCountReady=false }
    Ctrl-->>FE: 上传成功

    FE->>Ctrl: GET /api/documents/{fileId}/tables
    Ctrl->>TableSvc: GetTableInfoDtosAsync(wordFile)
    TableSvc->>Parser: GetTablesAsync(stream)
    Parser-->>TableSvc: tables
    Ctrl-->>FE: 表格列表

    FE->>Ctrl: GET /api/documents/{fileId}/tables/{tableIndex}/preview
    Ctrl->>TableSvc: GetTablePreviewAsync(...)
    TableSvc->>Parser: ExtractTableDataAsync(...)
    Parser-->>TableSvc: preview
    Ctrl-->>FE: 表格预览

    FE->>Ctrl: POST /api/documents/import
    Ctrl->>ImportSvc: ImportWordAsync(...)
    ImportSvc->>TableSvc: ExtractTableDataAsync(...)
    ImportSvc->>DB: 加载范围内已有规格

    alt 存在冲突
        ImportSvc->>DupSvc: CreateSessionAsync(...)
        DupSvc-->>ImportSvc: duplicate session
        ImportSvc-->>Ctrl: RequiresConfirmation
        Ctrl-->>FE: 403 待确认
    else 可直接导入
        ImportSvc->>DB: 事务写入规格
        ImportSvc-->>Ctrl: ImportResult
        Ctrl-->>FE: 导入结果
    end
```

### 2.3 导入冲突分流

```mermaid
flowchart TD
    A[POST /api/documents/import] --> B[解析表格数据]
    B --> C{重复检测}

    C -->|完全一致| D1[跳过]
    C -->|键一致内容不同| D2[返回待确认差异]
    C -->|语义相似且启用AI| D3[召回候选并复核]
    D3 --> D4{复核结果}
    D4 -->|通过| D5[返回待确认差异]
    D4 -->|不通过| D6[按新增处理]

    D2 --> E[前端确认覆盖或跳过]
    D5 --> E
    E --> F[再次提交 ConfirmedDifferences]
    F --> G[事务导入]

    D1 --> H[结束]
    D6 --> G
```

---

## 三、智能填充架构

### 3.1 智能填充组件关系

```mermaid
flowchart TD
    FE[前端智能填充页]

    PREVIEW_CTRL[MatchingPreviewController]
    EXEC_CTRL[MatchingExecutionController]
    TASK_CTRL[MatchingTaskController]
    REUSE_CTRL[MatchingReuseController]

    PREVIEW_APP[MatchingPreviewAppService]
    EXEC_APP[MatchingExecutionAppService]
    TASK_APP[MatchingTaskAppService]
    REUSE_APP[StrictReuseAppService]

    TABLE[DocumentTableAccessService]
    WRITE[MatchingResultWriteBackService]
    SNAPSHOT[MatchingTaskSnapshotService]

    MATCH[SemanticKernelMatchingService]
    EMB[SemanticKernelEmbeddingService]
    LLM[LlmMatchingAssistService]
    PIPE[ITextPreprocessingPipeline]

    DB[(MySQL)]
    FS[(uploads / filled 文件)]
    AI[OpenAI / Azure / Ollama / LM Studio]

    FE --> PREVIEW_CTRL
    FE --> EXEC_CTRL
    FE --> TASK_CTRL
    FE --> REUSE_CTRL

    PREVIEW_CTRL --> PREVIEW_APP
    EXEC_CTRL --> EXEC_APP
    TASK_CTRL --> TASK_APP
    REUSE_CTRL --> REUSE_APP

    PREVIEW_APP --> TABLE
    PREVIEW_APP --> MATCH
    PREVIEW_APP --> EMB
    PREVIEW_APP --> PIPE
    PREVIEW_APP --> DB

    EXEC_APP --> WRITE
    EXEC_APP --> SNAPSHOT
    EXEC_APP --> DB

    TASK_APP --> SNAPSHOT
    TASK_APP --> WRITE
    TASK_APP --> FS

    REUSE_APP --> SNAPSHOT
    REUSE_APP --> WRITE
    REUSE_APP --> FS

    MATCH --> EMB
    MATCH --> LLM
    EMB --> AI
    LLM --> AI
```

**当前结论**：
- `batch-preview` 负责算匹配结果，不直接写回文件。
- `llm-stream` 是独立复核链路，不等于 `batch-preview` 内同步决策。
- `batch-execute` 才会真正写文件并生成下载任务。
- 严格复用走 `MatchingTaskSnapshotService` 快照，不走 `MatchingWorkflowSupportService`。

### 3.2 智能填充完整时序

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端
    participant Doc as DocumentsController
    participant PreviewCtrl as MatchingPreviewController
    participant ExecCtrl as MatchingExecutionController
    participant TaskCtrl as MatchingTaskController
    participant PreviewApp as MatchingPreviewAppService
    participant ExecApp as MatchingExecutionAppService
    participant Match as SemanticKernelMatchingService
    participant Emb as SemanticKernelEmbeddingService
    participant AI as AI 服务
    participant DB as MySQL

    FE->>Doc: POST /api/documents/upload
    Doc-->>FE: { fileId, fileType, tableCountReady=false }
    FE->>Doc: GET /api/documents/{fileId}/tables
    Doc-->>FE: tables

    FE->>PreviewCtrl: POST /api/matching/batch-preview
    PreviewCtrl->>PreviewApp: BatchPreviewAsync(User, request)
    PreviewApp->>DB: 加载范围内规格
    PreviewApp->>Emb: 生成或补齐向量
    Emb->>AI: embeddings
    AI-->>Emb: vectors
    PreviewApp->>Match: BatchMatchAsync(...)
    Match->>AI: LLM 实体判别
    AI-->>Match: entity result
    PreviewApp-->>PreviewCtrl: BatchPreviewResponse
    PreviewCtrl-->>FE: 预览结果

    alt 启用 LLM 复核
        FE->>ExecCtrl: POST /api/matching/llm-stream
        ExecCtrl->>AI: SSE 流式复核
        AI-->>FE: review stream
    end

    FE->>ExecCtrl: POST /api/matching/batch-execute
    ExecCtrl->>ExecApp: BatchExecuteFillAsync(User, request)
    ExecApp->>DB: 保存任务快照
    ExecApp-->>FE: { taskId, filledCount }

    FE->>TaskCtrl: GET /api/matching/download/{taskId}
    TaskCtrl-->>FE: filled file
```

### 3.3 匹配决策与复核分流

```mermaid
flowchart TD
    A[BatchPreviewAsync 产出候选] --> B{存在硬冲突}
    B -->|是| C[reject]
    B -->|否| D{达到中置信门槛}
    D -->|否| E[manualReview]
    D -->|是| F[AI 等价裁决门禁]

    F -->|equivalent 且非高歧义| G[autoApply]
    F -->|different / uncertain| E
    F -->|equivalent 但高歧义| H[manualReview]

    H --> I[POST /api/matching/llm-stream]
    I --> J[SSE 返回 review.start / review.delta / review.done / review.error]
    J --> K[人工确认或复核放行]

    E --> K
    G --> L[POST /api/matching/batch-execute]
    K --> L
```

---

## 四、批量回复与严格复用

### 4.1 批量回复时序

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端
    participant Ctrl as BatchReplyController
    participant App as BatchReplyAppService
    participant Session as BatchReplySessionService
    participant Table as DocumentTableAccessService
    participant Write as MatchingResultWriteBackService
    participant FS as FileStorageService

    FE->>Ctrl: POST /api/batch-reply/source/upload
    Ctrl->>App: UploadSourceAsync(User, file)
    App->>Table: CountTablesAsync(...)
    App->>Session: CreateSourceSessionAsync(...)
    Ctrl-->>FE: { sessionId, sourceFileName, tableCount }

    FE->>Ctrl: POST /api/batch-reply/targets/upload
    Ctrl->>App: UploadTargetsAsync(User, sessionId, files)
    App->>Session: SaveTargetFileAsync(...)
    App->>Session: AddTargetFilesAsync(...)
    Ctrl-->>FE: target files

    FE->>Ctrl: POST /api/batch-reply/table-preview
    Ctrl->>App: TablePreviewAsync(User, request)
    App->>Table: 读取来源表和目标表
    App-->>Ctrl: preview rows
    Ctrl-->>FE: 预检结果

    FE->>Ctrl: POST /api/batch-reply/execute
    Ctrl->>App: ExecuteAsync(User, request)
    App->>Write: GenerateBatchReplyTargetFileAsync(...)
    App->>FS: 保存单文件或 ZIP
    Ctrl-->>FE: { taskId, successCount, failedCount }

    FE->>Ctrl: GET /api/batch-reply/download/{taskId}
    Ctrl-->>FE: result file
```

### 4.2 严格复用时序

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端
    participant Ctrl as MatchingReuseController
    participant App as StrictReuseAppService
    participant Snapshot as MatchingTaskSnapshotService
    participant Write as MatchingResultWriteBackService
    participant FS as FileStorageService

    FE->>Ctrl: POST /api/matching/reuse/strict/preview
    Ctrl->>App: PreviewStrictReuseAsync(sourceTaskId, targetFileIds)
    App->>Snapshot: LoadAsync(user, sourceTaskId)
    Snapshot-->>App: StrictReuseSession
    App-->>Ctrl: 逐文件预检结果
    Ctrl-->>FE: preview

    FE->>Ctrl: POST /api/matching/reuse/strict/execute
    Ctrl->>App: ExecuteStrictReuseAsync(sourceTaskId, targetFileIds)
    App->>Snapshot: LoadAsync(user, sourceTaskId)
    Snapshot-->>App: StrictReuseSession
    App->>Write: GenerateStrictReuseTargetFileAsync(...)
    App->>FS: 保存单文件或 ZIP
    App->>Snapshot: SaveAsync(user, FillTaskResult)
    Ctrl-->>FE: { taskId, successCount, failedCount }
```

### 4.3 三条主链路差异

| 维度 | 智能填充 | 批量回复 | 严格复用 |
|------|---------|---------|---------|
| 输入方式 | 上传待填充文件 | 来源文件 + 多目标文件会话 | 基于已有智能填充任务 |
| 是否使用 AI | 是 | 否 | 否 |
| 核心依据 | 向量召回 + 规则 + LLM | 项目+规格精确对齐 | 来源任务快照 |
| 是否可人工确认 | 是 | 预检后执行 | 预检后执行 |
| 结果产物 | 单文件下载 | 单文件或 ZIP | 单文件或 ZIP |

### 4.4 失败与后续流转规则

| 场景 | 当前真实流向 |
|------|-------------|
| 智能填充 `batch-preview` 失败 | 检查 AI 服务、列配置、源文件内容后重新预览 |
| `llm-stream` 失败 | 回到当前预览结果，继续人工确认，不阻断后续执行 |
| 智能填充 `batch-execute` 失败 | 返回预览阶段调整映射后再执行 |
| 严格复用预检失败 | 当前文件不能直接套用，可改走批量回复 |
| 批量回复 `table-preview` 失败 | 调整目标文件或表配置后重新预检 |
| 批量回复 `execute` 失败 | 回到预检结果继续调整后再执行 |

---

## 五、前端入口与页面映射

```mermaid
flowchart TB
    ROOT["前端路由入口"]
    ROOT --> IMP["/data-import/import<br/>上传 / 选表 / 预览 / 导入"]
    ROOT --> SF["/smart-fill/fill<br/>批量预览 / LLM复核 / 执行 / 下载 / 严格复用"]
    ROOT --> BR["/batch-reply/index<br/>来源上传 / 目标上传 / 预检 / 执行"]
```

---

## 六、核对结论

- 架构图如果重点看“上传”和“智能填充”，当前主编排位置确实在 `AcceptanceSpecSystem.Api/Services`，不是 `Application`。
- 上传链路是 `upload -> tables -> preview -> import` 四段式，不是一次请求内完成。
- 智能填充分成 `batch-preview -> llm-stream(可选) -> batch-execute -> download` 四段式。
- 智能填充成功后，真实后续可以是“下载结果”或“进入严格复用”；如果要处理另一批文档，也可以转入批量回复模块，但这是独立会话，不是同一任务直通。
- 失败回退点主要落在“重新上传 / 重新预览 / 重新执行”这几个真实节点，不存在文档里画一条自动兜底成功链的情况。
- 批量回复是独立会话链路，不走 AI 匹配。
- 严格复用依赖任务快照，不重新匹配。
- 本版已主动移除数据库字段、索引、实体字段清单，避免偏离“架构图”主题。
