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

## 项目概述

**验收规格管理系统**（Acceptance Specification System）——帮助企业验收工程师从 Word/Excel 文档提取历史验收规格数据，并通过 AI 智能匹配（相似度 / Embedding / LLM）自动填充到新文档中。

核心数据模型：按 **客户 (Customer) → 制程 (Process)** 层级组织验收规格（项目 + 规格 → 验收 + 备注）。

---

## 常用命令

### 后端（.NET 8 / ASP.NET Core）

```bash
# 启动 API（仓库根目录执行）
dotnet run --project src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Debug --urls http://localhost:5014

# 运行全部测试
dotnet test AcceptanceSpecSystem.sln -c Debug

# 运行单个测试项目
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj

# EF Core 迁移（需同时指定数据项目和启动项目）
dotnet ef migrations add <MigrationName> -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet ef database update -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet ef migrations remove -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
```

### 前端（Vue 3 / Vite / pnpm）

```bash
cd web
pnpm install
pnpm dev           # 开发服务器，http://localhost:8848
pnpm build         # 生产构建
pnpm typecheck     # TypeScript + Vue 类型检查
pnpm lint          # ESLint + Prettier + Stylelint 全量检查
```

### Docker

```bash
docker compose up -d --build   # 一键启动（前端 8080 / 后端 5014 / MySQL 3306）
docker compose logs -f api     # 查看 API 日志
docker compose down -v         # 停止并清除数据卷
```

### E2E 控制台测试工具

```bash
dotnet run --project tools/E2ETest/E2ETest.csproj -c Debug -- \
  --baseUrl http://localhost:5014 \
  --docx docs/example.docx \
  --tableIndex 0 \
  --projectColumnIndex 0 --specificationColumnIndex 1 \
  --acceptanceColumnIndex 2 --remarkColumnIndex 3
```

---

## 架构概览

### 整体分层

```
前端 SPA (Vue 3)  →  Nginx 反代 (8080)  →  ASP.NET Core API (5014)  →  MySQL (3306)
```

开发时 Vite 代理 `/api/*` → `http://localhost:5014`，无需手动切换地址。

### 后端项目依赖

```
AcceptanceSpecSystem.Api          ← HTTP 入口、DI 注册、Program.cs
  ├── AcceptanceSpecSystem.Core   ← AI、Matching、TextProcessing、Documents 核心业务
  └── AcceptanceSpecSystem.Data   ← EF Core DbContext、Entities、Migrations、Repository
```

- **Core** 不依赖 API 层，可独立单元测试。
- **Data** 通过 `IUnitOfWork` + 泛型 `IRepository<T>` 抽象持久化，控制器不直接操作 DbContext。
- 启动时 `DatabaseInitializer.InitializeAsync()` 自动应用待执行迁移（`Testing` 环境跳过）。

### Core 核心模块

| 模块 | 路径 | 职责 |
|------|------|------|
| AI / Semantic Kernel | `Core/AI/SemanticKernel/` | 多 AI 提供商（OpenAI / Azure / Ollama / LM Studio）工厂与服务选择 |
| 匹配引擎 | `Core/Matching/` | 相似度、Embedding 向量、LLM 混合匹配；阈值过滤 |
| 文本预处理 | `Core/TextProcessing/` | 简繁转换、同义词替换、OK/NG 标准化、关键词提取管道 |
| 文档处理 | `Core/Documents/` | Word/Excel 解析与 Word 填充写入 |

### 前端模块

| 路由 | 功能 |
|------|------|
| `/base-data/` | 客户、制程、规格、机器型号基础数据 CRUD |
| `/data-import/` | Word/Excel 导入验收规格 |
| `/smart-fill/` | 匹配预览 → 执行填充 → 下载结果文档 |
| `/file-compare/` | 填充前后文件对比 |
| `/config/` | AI 服务配置、提示词模板、同义词、关键词 |

API 调用封装在 `web/src/api/`，路径别名 `@` 指向 `web/src/`。

### 关键 API 端点

```
POST /api/documents/upload           上传 docx
POST /api/documents/import           解析并导入表格（需 customerId + processId）
POST /api/matching/preview           匹配预览（fileId / tableIndex + 列索引）
POST /api/matching/execute           执行填充，返回 taskId
GET  /api/matching/download/{taskId} 下载填充结果
GET  /swagger                        Swagger UI
GET  /health                         健康检查
```

---

## 测试策略

- `tests/AcceptanceSpecSystem.Api.Tests`：`WebApplicationFactory` + SQLite In-Memory 跑 API 集成测试，覆盖 E2E 填充流程、LLM 辅助匹配、Excel 导入等。
- `tests/AcceptanceSpecSystem.Core.Tests`：匹配算法、文本处理纯单元测试。
- `tests/AcceptanceSpecSystem.Data.Tests`：Repository + EF Core 数据层测试。
- 测试环境通过 `ASPNETCORE_ENVIRONMENT=Testing` 标识，绕过迁移自动化。

---

## 开发规范

### 命名约定

- **C#**：类/方法/属性 PascalCase，接口前缀 `I`，异步方法后缀 `Async`，局部变量 camelCase。
- **TypeScript**：组件文件 PascalCase.vue，函数/变量 camelCase。
- **注释**：中文优先，类和公开方法需有 XML doc（C#）或 JSDoc（TS）。

### 重要约束

- **Schema 变更必须通过 EF Core 迁移**，禁止直接修改数据库。
- **匹配查找键**：`项目 + 规格` 组合；**填充目标**：`验收 + 备注` 列。
- AI 服务 Key 等敏感配置加密存储，禁止硬编码。
- 支持文件格式：仅 `.docx`（Word）和 `.xlsx`（Excel）。
- Git 提交信息格式：`类型: 中文描述`（如 `feat: 添加 Embedding 匹配功能`）。
