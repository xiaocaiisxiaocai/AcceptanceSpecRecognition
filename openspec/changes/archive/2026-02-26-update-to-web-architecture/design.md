# 技术设计文档：验收规格管理系统（Web架构版）

## Context

### 背景
企业验收流程中存在大量重复性工作：每次新产品验收都需要手动填写验收规格表，而这些数据往往可以从历史验收记录中找到参考。本系统旨在通过智能匹配技术，自动化这一流程。

### 约束条件
- Web应用，支持内网部署
- 前后端分离架构
- 支持在线AI服务（OpenAI、Azure OpenAI）
- 支持本地私有化AI部署（Ollama、LM Studio）
- 数据集中存储（MySQL + EF Core）
- 支持多用户同时访问

### 利益相关者
- 验收工程师：主要用户，执行日常验收工作
- 质量管理人员：配置匹配规则和阈值
- IT管理员：配置AI服务、管理系统、部署维护

## Goals / Non-Goals

### Goals
- 提供现代化的Web管理界面，支持主流浏览器
- 提供RESTful API，便于系统集成
- 支持多用户同时访问和数据共享
- 支持多种匹配算法，适应不同场景需求
- 配置可复用，减少重复设置
- 匹配结果透明可解释，显示得分计算过程
- 支持批量处理，提高效率
- 支持在线和本地私有化AI部署，满足不同安全需求

### Non-Goals
- 不支持.doc旧格式（仅.docx）
- 不集成ERP/MES等外部系统（但提供API可供集成）
- 暂不实现Excel格式支持（架构预留，后续版本添加）
- 暂不实现用户认证和权限管理（可选功能，后续添加）

## 系统架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Frontend Layer                                 │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │              Vue3 + pure-admin + Element Plus                      │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────┐ │  │
│  │  │ 数据导入 │ │ 智能填充 │ │ 配置管理 │ │ 历史记录 │ │ 系统设置│ │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └─────────┘ │  │
│  │  技术栈: Vue3 + TypeScript + Pinia + Vite + Tailwind CSS          │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                              │ HTTP/REST API
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         API Layer (ASP.NET Core 8)                       │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                       Controllers                                  │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────────┐ │  │
│  │  │ Customer   │ │ Document   │ │ Matching   │ │ Configuration  │ │  │
│  │  │ Controller │ │ Controller │ │ Controller │ │ Controller     │ │  │
│  │  └────────────┘ └────────────┘ └────────────┘ └────────────────┘ │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────────┐ │  │
│  │  │ Process    │ │ AiService  │ │ TextProc   │ │ History        │ │  │
│  │  │ Controller │ │ Controller │ │ Controller │ │ Controller     │ │  │
│  │  └────────────┘ └────────────┘ └────────────┘ └────────────────┘ │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Middleware: CORS | Swagger | ExceptionHandler | Logging          │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        Business Layer (Core)                             │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────────────┐ │
│  │ DocumentSvc  │ │ MatchService │ │ TextProcessingService            │ │
│  │ - WordParser │ │ - 相似度计算 │ │ - 简繁转换  - 同义词替换         │ │
│  │ - WordWriter │ │ - 向量匹配   │ │ - OK/NG转换 - 关键字高亮         │ │
│  └──────────────┘ └──────────────┘ └──────────────────────────────────┘ │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    AI Integration (Semantic Kernel)               │   │
│  │    OpenAI | Azure OpenAI | Ollama | LM Studio | 自定义端点        │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        Data Layer (EF Core + MySQL)                      │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    AppDbContext (Pomelo.MySql)                    │   │
│  │  Customers | Processes | AcceptanceSpecs | EmbeddingCaches        │   │
│  │  WordFiles | OperationHistories | AiServiceConfigs | ...          │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    MySQL 8.0 Database                             │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

## Decisions

### D1: Word处理库选择
**决策**: 使用 `DocumentFormat.OpenXml`（保持不变）
**理由**:
- 微软官方库，兼容性最佳
- 支持复杂表格操作（合并单元格、嵌套表格）
- 无需安装Office

### D2: 相似度算法
**决策**: 提供多种算法供用户选择（保持不变）
- Levenshtein距离：适合短文本、拼写差异
- Jaccard系数：适合词集合比较
- 余弦相似度：适合向量化后的文本

### D3: AI框架选择
**决策**: 使用 Microsoft Semantic Kernel（保持不变）
**理由**:
- 微软官方AI编排框架
- 原生支持.NET 8
- 统一的Connector抽象，易于切换提供商
- 同时支持在线API和本地私有化部署

### D4: ORM框架选择
**决策**: 使用 Entity Framework Core + Pomelo.EntityFrameworkCore.MySql
**理由**:
- 微软官方ORM，与.NET 8深度集成
- Pomelo是最成熟的MySQL Provider
- Code First便于版本控制和团队协作
- 自动迁移管理数据库Schema变更

### D5: 数据库配置（MySQL）

```csharp
// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=acceptance_spec_db;User=root;Password=abc+123;CharSet=utf8mb4;"
  }
}

// DbContext配置
services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()
    )
);
```

### D6: API设计规范

#### 路由设计
| 模块 | 路由前缀 | 说明 |
|------|----------|------|
| 客户管理 | /api/customers | CRUD操作 |
| 制程管理 | /api/processes | CRUD操作 |
| 验收规格 | /api/specs | CRUD + 批量导入 |
| 文档处理 | /api/documents | 上传、解析、填充 |
| 智能匹配 | /api/matching | 匹配预览、执行 |
| AI服务 | /api/ai-services | 配置、测试连接 |
| 文本处理 | /api/text-processing | 配置管理 |
| 同义词 | /api/synonyms | CRUD操作 |
| 关键字 | /api/keywords | CRUD操作 |
| 操作历史 | /api/history | 查询、撤销 |

#### 统一响应格式
```json
{
  "success": true,
  "code": 200,
  "message": "操作成功",
  "data": { },
  "timestamp": "2024-01-01T00:00:00Z"
}
```

#### 分页响应格式
```json
{
  "success": true,
  "code": 200,
  "message": "查询成功",
  "data": {
    "items": [ ],
    "total": 100,
    "page": 1,
    "pageSize": 20
  }
}
```

### D7: 前端架构设计

#### 基于pure-admin模板
- 使用pure-admin-thin精简版作为基础
- 集成Element Plus组件库
- 使用Pinia进行状态管理
- TypeScript强类型支持

#### 页面结构
```
views/
├── dashboard/              # 仪表盘
├── data-import/            # 数据导入
│   ├── index.vue           # 导入主页面
│   └── components/
│       ├── FileUpload.vue  # 文件上传
│       ├── TablePreview.vue # 表格预览
│       └── ColumnMapping.vue # 列映射
├── smart-fill/             # 智能填充
│   ├── index.vue
│   └── components/
│       ├── MatchPreview.vue # 匹配预览
│       └── ScoreDetail.vue  # 得分详情
├── master-data/            # 基础数据
│   ├── customers/          # 客户管理
│   ├── processes/          # 制程管理
│   └── specs/              # 验收规格
├── text-processing/        # 文本处理
│   ├── synonyms/           # 同义词
│   └── keywords/           # 关键字
├── ai-config/              # AI配置
│   ├── services/           # 服务配置
│   └── prompts/            # Prompt模板
├── history/                # 操作历史
└── settings/               # 系统设置
```

### D8: 配置文件格式
**决策**: 使用JSON格式（保持不变）

### D9: 文件存储策略
**决策**: Word文件存储在服务器文件系统，数据库存储元信息
```
uploads/
├── word-files/           # 原始上传文件
│   └── {yyyy-MM-dd}/
│       └── {guid}.docx
└── filled-files/         # 填充后文件
    └── {yyyy-MM-dd}/
        └── {guid}.docx
```

## Risks / Trade-offs

### R1: 文件上传大小限制
**风险**: Word文档可能较大，默认限制可能导致上传失败
**缓解**:
- 配置Kestrel最大请求体大小
- 配置Nginx代理大小限制
- 前端添加文件大小校验

### R2: 并发访问
**风险**: 多用户同时操作可能导致数据冲突
**缓解**:
- 使用数据库事务
- 乐观锁控制（RowVersion字段）
- 文档处理时锁定机制

### R3: MySQL字符集
**风险**: 中文存储可能出现乱码
**缓解**:
- 使用utf8mb4字符集
- 连接字符串指定CharSet=utf8mb4
- 数据库和表级别设置utf8mb4

### R4: 跨域请求
**风险**: 前后端分离部署时CORS问题
**缓解**:
- 配置ASP.NET Core CORS策略
- 开发环境使用Vite代理
- 生产环境使用Nginx反向代理

## Migration Plan

### 数据库迁移策略
```bash
# 添加MySQL Provider
dotnet add package Pomelo.EntityFrameworkCore.MySql

# 删除旧迁移
rm -rf Migrations/

# 创建MySQL迁移
dotnet ef migrations add InitialCreate_MySQL

# 应用迁移
dotnet ef database update
```

### 部署架构
```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Nginx         │────▶│  ASP.NET Core   │────▶│    MySQL        │
│   (前端静态)    │     │  Web API        │     │    Database     │
└─────────────────┘     └─────────────────┘     └─────────────────┘
       :80                    :5000                   :3306
```

## Open Questions

1. **Q**: 是否需要实现用户认证？
   **A**: 暂不实现，预留JWT认证接口，后续版本添加

2. **Q**: 是否需要Docker部署支持？
   **A**: 提供Dockerfile和docker-compose配置

3. **Q**: 大文件上传如何处理？
   **A**: 使用分块上传，限制单文件最大50MB
