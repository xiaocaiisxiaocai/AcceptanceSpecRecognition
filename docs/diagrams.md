# AcceptanceSpecificationSystem — 流程图与时序图

> 以下图表使用 [Mermaid](https://mermaid.js.org/) 语法，可直接在支持 Mermaid 的编辑器（如 VS Code + Mermaid 插件、Typora、Obsidian）中渲染。

---

## 一、整体业务流程总览

```mermaid
flowchart TD
    subgraph 前端 SPA
        A[用户登录]
        B[上传 Word/Excel 文件]
        C[配置列映射]
        D[预览导入数据]
        E[执行导入]
        F[智能匹配填充]
        G[下载结果文档]
    end

    subgraph 后端 API 层
        H[DocumentsController]
        I[MatchingController]
        J[AuthController]
    end

    subgraph 核心业务层
        K[DocumentServiceFactory]
        L[MatchingService]
        M[SpecSemanticSearchService]
    end

    subgraph AI 服务层
        N[EmbeddingService]
        O[LLM Matching]
        P[EmbeddingCache]
    end

    subgraph 数据层
        Q[(MySQL Db)]
        R[FileStorage]
    end

    A --> J
    B --> H
    H --> K
    K --> R
    C --> H
    D --> H
    E --> H
    H --> Q
    F --> I
    I --> M
    M --> N
    N --> P
    P --> Q
    I --> L
    L --> O
    G --> I
    I --> R
```

---

## 二、文件上传与导入流程

### 2.1 文件上传时序图

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端 (Vue)
    participant API as DocumentsController
    participant FS as FileStorageService
    participant DS as DocumentServiceFactory
    participant Parser as Word/Excel Parser
    participant DB as MySQL (WordFile)

    FE->>API: POST /api/documents/upload (IFormFile)
    API->>API: 校验文件类型 (.docx/.xlsx)
    API->>FS: SaveUploadedWord/ExcelAsync(bytes)
    FS-->>API: 返回 FilePath
    API->>DB: WordFile record (FilePath=path, FileContent=empty)
    DB-->>API: 保存成功
    API->>DS: GetParser(DocumentType)
    DS-->>API: Parser instance
    API->>Parser: GetTablesAsync(stream)
    Parser-->>API: List<TableInfo>
    API-->>FE: FileUploadResponse { fileId, tableCount }
```

### 2.2 导入验收规格流程（Word）

```mermaid
flowchart TD
    A[用户发起 POST /api/documents/import] --> B{检测 Pending 差异行?}
    B -- 是 --> C[返回 RequiresConfirmation]
    B -- 否 --> D{有新增数据?}
    D -- 否 --> E[返回空结果]
    D -- 是 --> F[BeginTransaction]
    F --> G[AddRangeAsync specsToInsert]
    G --> H[SaveChangesAsync]
    H --> I[CommitTransaction]
    I --> J[按需清理源文件]
    J --> K[返回 ImportResult]

    C --> L[前端逐条确认差异]
    L --> M[再次调用 /import<br/>携带 ConfirmedDifferenceKeys]
    M --> D
```

### 2.3 导入事务边界说明

```mermaid
sequenceDiagram
    autonumber
    participant Ctrl as DocumentsController
    participant UoW as UnitOfWork
    participant DB as MySQL
    participant FS as FileStorageService

    Ctrl->>UoW: BeginTransactionAsync()
    UoW->>DB: BEGIN TRANSACTION
    DB-->>UoW: OK
    Ctrl->>Ctrl: 逐行处理 tableData.Rows
    Note over Ctrl: 对比 existingSpecsInScope<br/>构建 specsToInsert 列表
    Ctrl->>UoW: AddRangeAsync(specsToInsert)
    Ctrl->>UoW: SaveChangesAsync()
    DB-->>UoW: INSERT n rows
    Ctrl->>UoW: CommitTransactionAsync()
    UoW->>DB: COMMIT
    alt CleanupSourceFile == true
        Ctrl->>FS: DeleteIfExistsAsync(filePath)
        Ctrl->>Ctrl: WordFile.FilePath = null
        Ctrl->>UoW: SaveChangesAsync()
    end
    Note over Ctrl,UoW: 任何异常均 RollbackTransactionAsync()
```

---

## 三、智能匹配填充流程

### 3.1 匹配预览（BatchPreview）完整时序

```mermaid
sequenceDiagram
    autonumber
    participant FE as 前端
    participant Ctrl as MatchingController
    participant Svc as MatchingService
    participant Sem as SpecSemanticSearchService
    participant Emb as EmbeddingService
    participant Cache as EmbeddingCache
    participant TP as TextPipeline
    participant DB as MySQL

    FE->>Ctrl: POST /api/matching/batch-preview
    Ctrl->>Ctrl: 校验表格数量 ≤ 500
    Ctrl->>Ctrl: ResolveSpecScopeAsync()
    Ctrl->>Sem: GetCandidatesAsync(customerId, processId, scope, embeddingSvcId)
    Sem->>DB: ApplyScopeToQuery + 条件过滤
    DB-->>Sem: List<AcceptanceSpec> (含 Include)
    Sem->>Cache: GetCachedEmbeddings(specIds)
    Cache-->>Sem: Dictionary<int, float[]>
    Sem-->>Ctrl: List<SpecCandidateDto>
    Ctrl->>TP: CreateSessionAsync()
    Ctrl->>Ctrl: 预处理候选项 (TP.Process)
    loop 遍历 request.Tables
        Ctrl->>Ctrl: ExtractMatchSourceItemsFromFileAsync
        Note over Ctrl: 读取文件 → 提取行列数据 → 过滤空行
    end
    Ctrl->>Ctrl: 合并 allSources
    Ctrl->>Svc: BatchMatchAsync(allSources, processedCandidates, config)
    loop allSources × processedCandidates
        Svc->>Emb: GenerateEmbeddingsAsync(sourceTexts, serviceId)
        Emb-->>Svc: List<float[]>
        Svc->>Svc: 计算余弦相似度
    end
    Svc-->>Ctrl: BatchMatchResult
    Ctrl->>Ctrl: 按表格分发结果
    Ctrl-->>FE: BatchPreviewResponse
```

### 3.2 匹配执行（BatchExecuteFill）流程

```mermaid
flowchart TD
    A[用户发起 POST /api/matching/batch-execute] --> B{校验 request}
    B --> C[GetCandidatesAsync<br/>获取候选规格]
    C --> D[合并所有表格的源数据]
    D --> E[调用 BatchMatchAsync<br/>执行批量匹配]
    E --> F[对每个表格执行 FillTableDataAsync]
    F --> G[WordWriter 写入 docx]
    G --> H[保存到 FileStorage]
    H --> I[创建 MatchingTask 记录]
    I --> J[异步清理源文件]
    J --> K[返回 ExecuteFillResponse<br/>含 taskId]
```

### 3.3 Embedding 缓存命中流程

```mermaid
sequenceDiagram
    autonumber
    participant Sem as SpecSemanticSearchService
    participant Cache as EmbeddingCache
    participant Emb as EmbeddingService
    participant DB as MySQL

    Sem->>Cache: GetCachedEmbeddings(specIds)
    Cache->>DB: FindAsync(specIds in cached keys)
    DB-->>Cache: List<SpecEmbeddingCacheEntry>
    Cache-->>Sem: missingSpecIds + cachedEmbeddings
    alt 有缺失的 specIds
        Sem->>Emb: GenerateEmbeddingsAsync(missingTexts, serviceId)
        Emb-->>Sem: missingEmbeddings
        Sem->>Cache: SaveMissingEmbeddingsAsync
        Cache->>DB: UPSERT cache entries
        Sem->>Sem: 合并 cached + missing
    end
    Sem-->>Ctrl: 完整 embedding 列表
```

---

## 四、用户认证与数据权限流程

### 4.1 登录与 JWT 颁发时序

```mermaid
sequenceDiagram
    autonumber
    participant User as 用户
    participant FE as 前端
    participant Auth as AuthController
    participant DB as MySQL (SystemUser)
    participant JWTSvc as JwtService
    participant Seed as AuthUserSeedService

    User->>FE: 输入用户名/密码
    FE->>Auth: POST /api/auth/login
    Auth->>DB: FindAsync(userId, hashedPassword)
    DB-->>Auth: SystemUser?
    Auth->>Auth: VerifyPassword(user, password)
    Auth->>Seed: GetScopeAsync(userId, companyId, "spec")
    Seed->>DB: 查询 AuthUserOrgLinks + AuthUserRoles
    DB-->>Seed: DataScopeResult
    Seed-->>Auth: DataScopeResult
    Auth->>JWTSvc: GenerateTokens(userId, companyId, scope)
    JWTSvc-->>Auth: { accessToken, refreshToken }
    Auth-->>FE: LoginResponse
    FE->>FE: 保存 token 到 localStorage
```

### 4.2 数据权限范围解析流程

```mermaid
flowchart TD
    A[请求进入 API] --> B[从 JWT 解析<br/>userId + companyId]
    B --> C[AuthDataScopeService<br/>GetScopeAsync]
    C --> D{查询 AuthUserOrgLinks}
    D --> E[获取用户所属组织单元列表]
    E --> F{查询 AuthUserRoles}
    F --> G[获取角色关联的 DataScopes]
    G --> H{遍历每个 Scope}
    H --> I{ScopeType = IsAll?}
    I -- 是 --> J[返回 IsAll = true]
    I -- 否 --> K{ScopeType = OrgNode?}
    K -- 是 --> L[返回 OrgUnitIds<br/>= 直接关联的组织单元]
    K -- 否 --> M{ScopeType = OrgSubtree?}
    M -- 是 --> N[构建 idToAncestors<br/>向上回溯所有祖先节点]
    M -- 否 --> O[IncludeSelf 扩展]
    N --> P[返回完整祖先链集合]
    O --> P
    P --> Q[合并所有 Scope<br/>去重后返回 DataScopeResult]

    subgraph 数据库查询
    D1[SELECT FROM AuthUserOrgLinks<br/>WHERE UserId = ?]
    D2[SELECT FROM AuthUserRoles<br/>WHERE UserId = ?]
    D3[SELECT FROM AuthRoleDataScopes<br/>WHERE RoleId IN (...)]
    D4[SELECT FROM OrgUnits<br/>WHERE CompanyId = ?]
    end

    D --> D1
    F --> D2
    D3 --> G
    D4 --> N
```

---

## 五、EmbeddingCache 缓存失效与更新

```mermaid
sequenceDiagram
    autonumber
    participant Svc as 业务服务
    participant Repo as SpecEmbeddingCacheRepository
    participant DB as MySQL

    Svc->>Repo: FindAsync(specId)
    Repo->>DB: SELECT ... WHERE SpecId = @id
    DB-->>Repo: SpecEmbeddingCacheEntry?

    alt EmbeddingCache.GetCachedEmbeddings
        Svc->>Repo: FindAsync(specIds)
        Repo->>DB: SELECT ... WHERE SpecId IN (...)
        DB-->>Repo: List<Entry>
        Repo-->>Svc: missingIds + cachedEntries
    end

    alt 规格数据变更时（AcceptanceSpec 更新）
        Svc->>Repo: DeleteBySpecIdsAsync(specIds)
        Repo->>DB: DELETE FROM cache WHERE SpecId IN (...)
        Note over Repo: 下次查询自动重新生成
    end

    alt 定时清理过期缓存
        Repo->>Repo: CleanupExpiredAsync()
        Repo->>DB: DELETE ... WHERE UpdatedAt < threshold
    end
```

---

## 六、前后端交互全景时序

```mermaid
sequenceDiagram
    autonumber
    participant Browser as 浏览器
    participant FE as Vue 前端
    participant Vite as Vite DevServer
    participant API as ASP.NET Core API
    participant AI as AI 服务<br/>(OpenAI/Azure/Ollama)
    participant DB as MySQL

    Browser->>FE: 访问 /
    FE->>Vite: 请求静态资源
    Vite-->>Browser: index.html + bundle.js
    Browser->>FE: 用户登录
    FE->>API: POST /api/auth/login
    API->>DB: 验证用户
    DB-->>API: SystemUser
    API-->>FE: JWT tokens
    FE->>FE: 保存 token

    Note over Browser,FE: 智能匹配填充完整流程

    FE->>API: POST /api/documents/upload
    API-->>FE: { fileId, tableCount }

    FE->>API: POST /api/matching/batch-preview
    API->>API: ResolveSpecScopeAsync
    API->>DB: ApplyScopeToQuery + 过滤
    API->>API: 预处理候选项
    API->>API: BatchMatchAsync
    API->>AI: GenerateEmbeddings (if cache miss)
    AI-->>API: embeddings
    API-->>FE: BatchPreviewResponse

    FE->>API: POST /api/matching/batch-execute
    API->>API: 执行填充
    API-->>FE: { taskId }

    FE->>API: GET /api/matching/download/{taskId}
    API-->>FE: 文件流 (application/vnd.openxmlformats-officedocument.wordprocessingml.document)
    FE->>Browser: 触发下载
```

---

## 七、核心数据模型关系

```mermaid
erDiagram
    Customer ||--o{ Process : "1:N"
    Process ||--o{ AcceptanceSpec : "1:N"
    Process ||--o{ MachineModel : "1:N"
    Customer ||--o{ AcceptanceSpec : "1:N"
    MachineModel ||--o{ AcceptanceSpec : "1:N"
    WordFile ||--o{ AcceptanceSpec : "1:N"

    Customer {
        int Id PK
        string Name
        int CompanyId FK
    }
    Process {
        int Id PK
        string Name
        int CustomerId FK
    }
    AcceptanceSpec {
        int Id PK
        int CustomerId FK
        int ProcessId FK "nullable"
        int MachineModelId FK "nullable"
        string Project
        string Specification
        string Acceptance
        string Remark
        int WordFileId FK "nullable"
        int CreatedByUserId FK
        int OwnerOrgUnitId FK "nullable"
        DateTime ImportedAt
    }
    SpecEmbeddingCache {
        int Id PK
        int SpecId FK
        string EmbeddingVector
        string EmbeddingModel
        DateTime CreatedAt
        DateTime UpdatedAt
    }
    SystemUser ||--o{ AuthUserOrgLink : "1:N"
    AuthUserOrgLink }o--|| OrgUnit : "N:1"
    SystemUser ||--o{ AuthUserRole : "1:N"
    AuthUserRole }o--|| AuthRole : "N:1"
    AuthRole ||--o{ AuthRoleDataScope : "1:N"
    AuthRoleDataScope }o--|| OrgUnit : "N:1"
```

---

*图表生成时间：2026-03-23 | 基于 AcceptanceSpecificationSystem 项目当前代码状态*
