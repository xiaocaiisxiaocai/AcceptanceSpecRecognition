# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

<!-- OPENSPEC:START -->
# OpenSpec Instructions

These instructions are for AI assistants working in this project.

Always open `@/openspec/AGENTS.md` when the request:
- Mentions planning or proposals (words like proposal, spec, change, plan)
- Introduces new capabilities, breaking changes, architecture shifts, or big performance/security work
- Sounds ambiguous and you need the authoritative spec before coding

Use `@/openspec/AGENTS.md` to learn:
- How to create and apply change proposals
- Spec format and conventions
- Project structure and guidelines

Keep this managed block so 'openspec update' can refresh the instructions.

<!-- OPENSPEC:END -->

---

## 分支合并保护

- `feat/smart-recognition-simplification` 合并、推送或以任何方式提交到远端 `main` 前，必须先向用户二次确认并取得明确同意。

---

## 项目概述

**验收规格管理系统**（Acceptance Specification System）——帮助企业验收工程师从 Word/Excel 文档提取历史验收规格数据，并通过 AI 智能匹配（相似度 / Embedding / LLM）自动填充到新文档中。

核心数据模型：按 **客户 (Customer) → 制程 (Process)** 层级组织验收规格（项目 + 规格 → 验收 + 备注）；**机型 (MachineModel)** 为可选筛选维度（业务筛选以 customerId + processId + 可选 machineModelId 为主）。

项目约定的权威说明（技术栈、架构模式、领域上下文、重要约束）见 `openspec/project.md`；本地联调步骤见 `docs/DEV.md`。

---

## 常用命令

### 后端（.NET 8 / ASP.NET Core）

```bash
# 启动 API（仓库根目录执行）
dotnet run --project src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Debug --urls http://localhost:5291

# 运行全部测试
dotnet test AcceptanceSpecSystem.sln -c Debug

# 运行单个测试项目
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj

# 运行单个测试（按名称过滤）
dotnet test AcceptanceSpecSystem.sln -c Debug --filter "FullyQualifiedName~TestClassName"

# EF Core 迁移（需同时指定数据项目和启动项目）
dotnet ef migrations add <MigrationName> -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet ef database update -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet ef migrations remove -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
```

### 前端（Vue 3 / Vite / pnpm）

```bash
cd web
pnpm install
pnpm dev           # 开发服务器，http://localhost:8849（端口来自 web/.env 的 VITE_PORT）
pnpm build         # 生产构建（内含 typecheck）
pnpm typecheck     # TypeScript + Vue 类型检查
pnpm lint          # ESLint + Prettier + Stylelint 全量检查
pnpm test          # 全部前端单测 = test:vitest + test:node
pnpm test:vitest   # vitest 跑 src/**/*.test.ts 内联用例（include 配置在 vite.config.ts 的 test 段）
pnpm test:node     # Node 原生 test runner 跑 web/tests/*.test.ts

# 运行单个前端测试文件（两轨命令不同）
pnpm exec vitest run src/views/shared/smart-structure-recognition.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/top-menu.test.ts
```

### E2E 控制台测试工具

```bash
dotnet run --project tools/E2ETest/E2ETest.csproj -c Debug -- \
  --baseUrl http://localhost:5291 \
  --docx docs/example.docx \
  --tableIndex 0 \
  --projectColumnIndex 0 --specificationColumnIndex 1 \
  --acceptanceColumnIndex 2 --remarkColumnIndex 3
```

### 分析与回归工具

```bash
# 匹配回归报告：回放基线样本，对比匹配决策，防止匹配质量回退
dotnet run --project tools/MatchingRegressionReport -- \
  --input tests/AcceptanceSpecSystem.Core.Tests/Fixtures/EvidenceDrivenMatchingBaseline.json \
  --high-confidence 0.95 [--output report.csv]

# 智能填充实情统计（只读连库，分析"确定性直达 vs AI 语义 vs 人工"占比、未识别品牌/单位 Top）
dotnet run --project tools/SmartFillInsightReport -- \
  --connection "Server=localhost;Database=acceptance_spec_db;User=root;Password=***;CharSet=utf8mb4;" \
  [--top 20] [--from yyyy-MM-dd] [--to yyyy-MM-dd] [--output report.json]

# 语义测试数据生成：tools/ParaphraseGenerator（LLM 改写）与 tools/*.ps1（灰区样本提取、改写 Excel 生成等）
```

---

## 架构概览

### 整体分层

```
前端 SPA (Vue 3)  →  IIS 站点/子应用 或 Docker Compose  →  ASP.NET Core API  →  MySQL (3306)
```

生产部署两种形态：IIS（`docs/DEPLOY-IIS.md`）或 Docker Compose 三容器 web + api + mysql（`docker-compose.yml`、`docs/DEPLOY-DOCKER.md`）。开发时 Vite 代理 `/api/*` → `http://localhost:5291`，无需手动切换地址。

### 后端项目依赖（严格分层）

API 仅引用 Application；Application 再引用 Core 与 Data。Core/Data 无项目引用，Data 不引用 Core。

```
AcceptanceSpecSystem.Api          ← HTTP 入口、Controllers、Api/Services 编排型 AppService、DI、JWT/RBAC 中间件
  └── AcceptanceSpecSystem.Application   ← 用例/查询服务、统一 DI 扩展 AddAcceptanceApplicationLayer()
        ├── AcceptanceSpecSystem.Core    ← AI、Matching、TextProcessing、Documents 核心业务（可独立单测）
        └── AcceptanceSpecSystem.Data    ← EF Core DbContext、Entities、Migrations、Repository
```

- **应用服务分布在两处**：
  - `Application/Services/`：基础数据与规格用例（`CustomerAppService`、`ProcessAppService`、`MachineModelAppService`、`AcceptanceSpecAppService`）、复杂只读查询 `AcceptanceSpecQueryService`、智能结构识别用例 `SmartConfigurationAppService`（选项类 `SmartConfigurationOptions`）。
  - `Api/Services/`：贴近 HTTP 的编排型用例（`Matching*`、`Document*`、`Dashboard`、`BatchReply`、`Auth*`、`OrgUnit`、`SystemUser`、`ExecutionHistory` 等），均以 `IXxxAppService` 接口注入控制器。
- **Core** 不依赖上层，可独立单元测试。控制器只依赖 AppService/接口，不直接操作 `DbContext` / `IUnitOfWork`。
- **Data** 通过 `IUnitOfWork` + 泛型 `IRepository<T>` 抽象持久化。
- 启动时 `DatabaseInitializer.InitializeAsync()` 自动应用待执行迁移（`Testing` 环境跳过）。
- 启动时 `SystemPromptTemplateInitializer.EnsureAsync()` 确保默认 Prompt 模板存在。

### Core 核心模块

| 模块 | 路径 | 职责 |
|------|------|------|
| AI / Semantic Kernel | `Core/AI/SemanticKernel/` | 多 AI 提供商（OpenAI / Azure / Ollama / LM Studio）工厂与服务选择 |
| 匹配引擎 | `Core/Matching/` | 相似度、Embedding 向量、LLM 混合匹配；阈值过滤 |
| 文本预处理 | `Core/TextProcessing/` | 简繁转换、同义词替换、OK/NG 标准化、关键词提取管道 |
| 文档处理 | `Core/Documents/` | Word/Excel 解析与 Word 填充写入 |
| 智能结构识别 | `Core/Documents/Intelligence/` | `RuleBasedMappingStrategy` 规则列映射、`DocumentStructureFusion`/`HealthCheck` 结构融合与体检、LLM 结构裁决模型 |
| 诊断/脱敏 | `Core/Diagnostics/` | 敏感信息日志脱敏（`SensitiveLogFormatter`）|

### 前端模块

| 路由 | 功能 |
|------|------|
| `/base-data/` | 客户、制程、规格、机器型号基础数据 CRUD |
| `/data-import/` | Word/Excel 导入验收规格 |
| `/smart-fill/` | 匹配预览 → 执行填充 → 下载结果文档 |
| `/file-compare/` | 填充前后文件对比 |
| `/batch-reply/` | 批量回填预览与执行 |
| `/dashboard/` | 仪表板 / 统计概览 |
| `/rbac/` | 用户、角色、权限、组织架构管理 |
| `/config/` | AI 服务配置、提示词模板、列映射规则、智能结构路由规则 |

API 调用封装在 `web/src/api/`，路径别名 `@` 指向 `web/src/`。智能结构识别的共享 UI 与逻辑在 `web/src/views/shared/`（`SmartStructureConfirmCard.vue`、`SmartStructureSummaryBanner.vue`、`smart-structure-recognition.ts`、composable `useSmartStructureRecognition.ts`），由 data-import 与 smart-fill 两页复用，各页流程逻辑在自己的 `*.smartRecognition.ts` 中。

### 关键 API 端点

```
POST /api/auth/login                     登录，返回 accessToken + refreshToken
POST /api/auth/refresh-token             刷新 token
POST /api/documents/upload               上传 docx/xlsx
POST /api/documents/import               解析并导入 Word 表格（需 customerId + processId）
POST /api/documents/excel/import         导入 Excel 表格
POST /api/smart-config/recognize         智能结构识别（表头/列映射/行范围识别 + 裁决）
POST /api/smart-config/confirm           确认识别结果，沉淀客户模板与学习列映射
CRUD /api/smart-structure-routing-rules  智能结构路由规则管理（Manual 优先于 Learned）
POST /api/matching/batch-preview         批量匹配预览（进度轮询 GET batch-preview-progress/{requestId}）
POST /api/matching/llm-stream            高歧义行流式 LLM 复核（SSE）
POST /api/matching/batch-execute         执行填充，返回 taskId
GET  /api/matching/download/{taskId}     下载填充结果
POST /api/batch-reply/preview            批量回填预览
POST /api/batch-reply/execute            批量回填执行
GET  /api/execution-history              执行历史记录
GET  /swagger                            Swagger UI
GET  /health                             健康检查
```

### 认证与权限

系统采用 **JWT + RBAC** 双层控制：
- `JwtAuth` 配置节提供 SigningKey / Issuer / Audience；SigningKey 最短 32 字符，启动时强制检查。
- 权限通过 `ApiPermissionMiddleware` 拦截，细粒度到控制器 Action 级别。
- 前端 `http` 实例（`web/src/utils/http/index.ts`）自动在请求过期时静默刷新 token，并注入审计 header（`X-Client-Trace-Id` / `X-Client-Id` / `X-Frontend-Route`）。
- 组织架构 (`OrgUnit`) 控制数据可见范围（`OwnerOrgUnitId`），每个验收规格归属一个组织节点。

### AI 匹配流水线

`SemanticKernelMatchingService` 执行三阶段匹配：
1. **精确命中快速路径**：Project + Specification 完全一致 → `ExactShortcut`，直接 AutoApply。
2. **Embedding 召回 + 重排**：取 Top-K（默认 2）候选，高置信阈值默认 `0.95`；Embedding 分达到阈值且无硬冲突 → `AutoApply`（`EnableDeterministicAutoApply` 开关控制）。
3. **LLM 等价裁决**（`EnableLlmEquivalenceAdjudication`，默认**开启**）：灰区行（分 < 高置信或有歧义）经 LLM 裁决；单批次调用上限 `LlmMaxCallsPerBatch`（默认 **1000**，本地部署无费用限制）防止超时；LLM 置信度下限 `LlmEquivalenceMinConfidence`（默认 **0.5**），低于此值时即使 LLM 判 Equivalent 也转人工。

`MatchDecision` 枚举决定最终行为：`AutoApply`（自动填充）/ `ManualReview`（人工确认）。`IsHighConfidence` 依赖 `Score >= HighConfidenceThreshold` 或（LLM 裁定为 `Equivalent` 且置信度 ≥ `LlmEquivalenceMinConfidence`）。

### 智能结构识别流水线

`SmartConfigurationAppService.RecognizeAsync`（`POST /api/smart-config/recognize`）在导入/填充前识别文档结构，Word/Excel 统一为扁平表格后：
1. `RuleBasedMappingStrategy` 按列映射规则与表头关键词给出各列字段候选（项目/规格/验收/备注）。
2. `DocumentStructureFusion` + `DocumentStructureHealthCheck` 融合并体检结构（表头行、表头行数、数据行范围）。
3. 灰区结果交 LLM 结构裁决；超时由 `SmartConfiguration:StructureAdjudicationTimeoutSeconds` 控制（默认 20 秒，Clamp 1–300）；裁决失败/超时保留规则识别的"待确认"状态，不阻断流程。
4. **多表自适应路由**：`SmartConfigurationTableRoutingService` 为每张表给出路由决策（表类型、综合排序分、建议 Process/Confirm/Skip）。路由规则外置为数据库实体 `SmartStructureRoutingRule`（Manual 手工规则优先于 Learned 学习规则），经 `/api/smart-structure-routing-rules` CRUD，前端管理页在 `/config/smart-structure-routing-rules`。
5. 每张表输出字段级裁决（自动采用 / 需确认 / 拒绝），前端 `SmartStructureConfirmCard` 呈现供人工修正；确认后经 `/api/smart-config/confirm` 沉淀客户模板、学习到的列映射与学习型路由规则。

---

## 测试策略

- `tests/AcceptanceSpecSystem.Api.Tests`：`WebApplicationFactory` + SQLite In-Memory 跑 API 集成测试，覆盖 E2E 填充流程、LLM 辅助匹配、Excel 导入、智能结构识别等。
- `tests/AcceptanceSpecSystem.Core.Tests`：匹配算法、文本处理纯单元测试。
- `tests/AcceptanceSpecSystem.Data.Tests`：Repository + EF Core 数据层测试。
- 测试环境通过 `ASPNETCORE_ENVIRONMENT=Testing` 标识，绕过迁移自动化。
- 前端单测双轨：`web/src/**` 内联用例（工具函数、智能识别流程等）由 **vitest** 执行，include 限定为 `src/**/*.test.ts`（配置在 `web/vite.config.ts` 的 `test` 段）；`web/tests/`（30+ 跨页面行为用例）由 **Node 原生 test runner** 执行（`node:test` + `--experimental-strip-types`，`setup-node-test-cwd.mjs` 会把 cwd 切到仓库根）。`pnpm test` 依次跑两轨。
- CI（`.github/workflows/ci.yml`）：后端 `dotnet test AcceptanceSpecSystem.sln --no-restore -m:1`；前端 `pnpm typecheck` + `pnpm test` + `pnpm build`（pnpm 10 / Node 22）；并验证 api 与 web 两个 Docker 镜像可构建。

### 架构边界测试（强约束，改动前必读）

`ArchitectureBoundaryTests` / `CoreProviderBoundaryTests` / `FrontendViewBoundaryRefactorTests` 会在违反约定时直接失败，提交前务必本地通过：

- **分层引用**：API 项目只能引用 Application；Data 不得引用 Core。
- **控制器瘦身**：RBAC/基础数据控制器必须委派对应 AppService，禁止出现 `AppDbContext` / `IUnitOfWork`。
- **接口化 DI**：`Api/Services` 用例服务须 `public interface IXxxAppService` + `public sealed class XxxAppService : IXxxAppService`，按 `AddScoped<IXxx, Xxx>()` 注册，控制器注入接口。
- **取消令牌**：控制器内 EF 异步查询禁止裸写 `.ToListAsync();` / `.CountAsync();`，必须透传 `CancellationToken`。
- **文件体量**：`Api/Services` 下 `MatchingWorkflow*`、`BatchReplyAppService*`、`DocumentImportAppService*` 每个文件须 < 500 行（巨型服务已按职责拆分，勿回灌）。
- **前端视图拆分**：`ScoreDetailDialog`、`data-import` 等大页面须保持 壳 + 区块组件 + composable 的拆分结构。
- **导航/权限单一来源**：页面/菜单/权限码统一来自 `shared/navigation/navigation-manifest.json`，前后端共同消费（不再使用运行时 async-routes）。

---

## 开发规范

### 命名约定

- **C#**：类/方法/属性 PascalCase，接口前缀 `I`，异步方法后缀 `Async`，局部变量 camelCase。
- **TypeScript**：组件文件 PascalCase.vue，函数/变量 camelCase。
- **注释**：中文优先，类和公开方法需有 XML doc（C#）或 JSDoc（TS）。

### 重要约束

- **Schema 变更必须通过 EF Core 迁移**，禁止直接修改数据库。
- **匹配查找键**：`项目 + 规格` 组合；**填充目标**：`验收 + 备注` 列。
- **Word 与 Excel 双端同改**：文档解析与回写要同时考虑两种格式，不要只修一端；Excel 支持"直接写回源文件"，Word 为"生成结果文件供下载"。
- **不静默降级**：匹配主流程以 Embedding 为准，Embedding 服务不可用时直接报错。
- **SSE 流式输出**：LLM 流式复核基于 SSE，相关改动需同步考虑代理超时与客户端中断处理。
- AI 服务 Key 等敏感配置加密存储，禁止硬编码。
- 支持文件格式：仅 `.docx`（Word）和 `.xlsx`（Excel）。
- 涉及 API、数据库、架构、匹配行为的实质变更，优先通过 OpenSpec 变更提案管理（见 `openspec/AGENTS.md`）。
- 全仓库启用 **Nullable 引用类型**与 .NET Analyzers（`Directory.Build.props`），新增 C# 代码需通过可空性检查。
- Git 提交信息格式：`类型: 中文描述`（如 `feat: 添加 Embedding 匹配功能`）；husky + commitlint 强制校验，类型限定 feat/fix/perf/style/docs/test/refactor/build/ci/chore/revert/wip/workflow/types/release，标题 ≤ 108 字符。
