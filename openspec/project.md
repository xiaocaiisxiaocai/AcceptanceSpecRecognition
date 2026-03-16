# Project Context

## Purpose
验收规格管理系统（Acceptance Specification System）是一个基于 Web 的验规数据管理平台，用于帮助验收工程师将历史 Word/Excel 文档中的验收规格沉淀为结构化数据，并通过 Embedding 匹配与可选 LLM 辅助，自动填充到新的验收文档中。

### 核心目标
1. 从 Word / Excel 文档中批量提取验收规格数据并导入数据库。
2. 按客户、机型、制程维度组织和检索验收规格。
3. 通过 Embedding 主匹配能力对新文档进行智能填充。
4. 在低置信度场景下提供可选的 LLM 复核和建议生成能力。
5. 支持文件对比、配置管理、历史追踪等配套能力。
6. 同时支持在线 AI 服务与本地私有化 AI 服务接入。

## Tech Stack

### 核心架构
| 层次 | 技术选型 | 说明 |
|------|----------|------|
| 前端 | Vue 3 + TypeScript + Vite + Element Plus | 管理后台 SPA |
| API | ASP.NET Core 8 Web API | HTTP 接口、Swagger、中间件 |
| Core | C# .NET 8 类库 | 匹配、AI、文本预处理、文档处理 |
| Data | EF Core 8 + Pomelo MySQL | 数据访问、迁移、仓储 |
| 数据库 | MySQL 8 | 主业务数据存储 |
| 文档处理 | OpenXML + ClosedXML | Word / Excel 解析与写入 |
| AI 编排 | Microsoft Semantic Kernel | LLM 与 Embedding 服务统一接入 |

### 当前运行形态
| 场景 | 端口 / 形态 | 说明 |
|------|-------------|------|
| 前端开发 | Vite Dev Server | 本地开发调试 |
| 后端开发 | `http://localhost:5290` | 见 `src/AcceptanceSpecSystem.Api/Properties/launchSettings.json` |
| 生产部署 | Docker Compose | `web + api + mysql` 三容器 |
| 文件存储 | `uploads/` 目录 | Word、Excel、填充结果文件 |

### AI 服务支持
| 服务 | 用途 | 说明 |
|------|------|------|
| OpenAI | LLM / Embedding | 云端通用方案 |
| Azure OpenAI | LLM / Embedding | 企业合规场景 |
| Ollama | LLM / Embedding | 本地私有化优先方案 |
| LM Studio | LLM / Embedding | 本地桌面式接入 |
| OpenAI 兼容端点 | LLM / Embedding | 自定义服务接入 |

## Project Conventions

### Code Style
- 交流、注释、文档统一使用中文。
- C# 类型、方法、属性使用 PascalCase；局部变量使用 camelCase。
- TypeScript / Vue 遵循现有仓库风格，优先保持已有命名和组件拆分方式。
- 异步方法统一使用 `Async` 后缀。
- 复杂业务逻辑可以加简短中文注释，但避免解释显而易见的代码。

### Architecture Patterns
- **分层架构**：`Api -> Core -> Data`，禁止反向依赖。
- **仓储 + 工作单元**：通过 `IUnitOfWork` 聚合仓储访问。
- **工厂模式**：文档解析/写入与 Semantic Kernel 服务构建都通过工厂统一封装。
- **管道模式**：文本预处理通过可配置 pipeline 执行。
- **显式失败策略**：当前匹配主流程以 Embedding 为准，Embedding 服务不可用时直接报错，不做静默降级。

### Data & Persistence Conventions
- EF Core 使用 Code First 和 Migration 管理 Schema。
- 启动时自动应用迁移；测试环境使用 SQLite InMemory 替代 MySQL。
- 业务主数据集中在 `AcceptanceSpec`、`Customer`、`Process`、`MachineModel`、`WordFile`、`EmbeddingCache` 等实体。
- 文件内容优先落地到文件系统，数据库二进制字段作为兼容兜底。

### Frontend Conventions
- 页面主流程以向导式交互为主，降低操作门槛。
- API 封装集中在 `web/src/api`。
- 匹配、导入等长耗时请求要显式考虑超时、加载状态和错误提示。
- LLM 流式输出基于 SSE，前后端改动时要同步考虑代理超时与客户端中断处理。

### Testing Strategy
- API 集成测试：`AcceptanceSpecSystem.Api.Tests`，基于 `WebApplicationFactory`。
- Core 单元测试：`AcceptanceSpecSystem.Core.Tests`，覆盖匹配、文档、异常路径。
- Data 测试：`AcceptanceSpecSystem.Data.Tests`，覆盖仓储与工作单元行为。
- 前端至少保证 `pnpm build` 可通过；后端至少保证 `dotnet test AcceptanceSpecSystem.sln -c Debug` 可通过。

### Git Workflow
- 主分支为 `main`。
- 提交信息使用中文，格式建议为 `type: 描述`，如 `feat: 完善智能填充配置`。
- 大改动优先拆成多个可回溯的主题提交，避免把文档、功能、清理混在一起。

## Domain Context

### 核心业务对象
- **项目（Project）**：匹配主键的一部分。
- **规格（Specification）**：匹配主键的一部分。
- **验收（Acceptance）**：填充目标字段。
- **备注（Remark）**：填充目标字段。

### 数据组织方式
- 业务筛选维度为：**客户 → 机型 → 制程**。
- 导入和匹配时，`项目 + 规格` 是主要查找键。
- 文档来源保留到 `WordFile`，用于溯源、预览、填充和后续比对。

### 文档处理范围
- 当前正式支持 `.docx` 与 `.xlsx`。
- Word 与 Excel 都支持上传、表格预览、导入与智能填充。
- Excel 已支持“直接写回源文件”模式；Word 仍保留“生成结果文件供下载”的模式。

### 匹配与 AI 规则
- 当前主匹配策略是 **Embedding 主匹配**。
- LLM 仅作为复核和建议生成能力，不替代主匹配排序。
- 低于阈值的结果会被过滤或触发建议生成，具体由配置决定。

## Important Constraints
- 必须保持中文界面与中文业务语义一致，不要把核心术语改成英文主导。
- 涉及 API、数据库、架构、匹配行为的实质变更，优先通过 OpenSpec 变更提案管理。
- 对已有用户数据和导入流程的修改要优先考虑兼容性，避免破坏历史文件和历史数据。
- 文档解析与回写需要同时考虑 Word 和 Excel，不要只修一端。
- AI 服务配置包含敏感信息，必须走现有加密与配置管理路径，不能明文扩散。
- 长耗时接口、SSE、文件写入这三类改动都要补充验证，避免只看编译通过。

## External Dependencies

### 基础设施
| 类型 | 依赖 | 说明 |
|------|------|------|
| 数据库 | MySQL 8 | 生产主库 |
| 容器 | Docker / Docker Compose | 生产部署 |
| 文件系统 | 本地目录或容器卷 | 上传与填充文件存储 |

### 第三方库
| 分类 | 依赖 |
|------|------|
| 后端 | ASP.NET Core, EF Core, Pomelo MySQL, Semantic Kernel, OpenXML, ClosedXML, OpenCCNET |
| 前端 | Vue 3, Vite, Element Plus, Pinia, Axios, Tailwind CSS, Pure Admin Thin |
| 测试 | xUnit, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing |

### 推荐本地开发检查
1. `dotnet test AcceptanceSpecSystem.sln -c Debug`
2. `pnpm build`（目录：`web/`）
3. 必要时再启动前后端做上传、导入、匹配、填充的冒烟验证
