# 开发与联调（Web 架构版）

## 1. 启动后端 API

在仓库根目录：

```powershell
dotnet run --project src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj -c Debug --urls http://localhost:5014
```

健康检查：

```powershell
curl http://localhost:5014/health
```

> 注意：后端默认会在启动时执行 EF Core 迁移（Development/Production）。测试环境（`ASPNETCORE_ENVIRONMENT=Testing`）会跳过迁移，由测试自行初始化数据库。

## 2. 启动前端 Web

```powershell
cd web
pnpm install
pnpm dev
```

前端通过 Vite 代理访问 `/api/*` 后端接口（见 `web/vite.config.ts`）。

## 3. 常用联调接口

- `GET /swagger`：Swagger UI
- `GET /api/customers` / `GET /api/processes` / `GET /api/specs`：基础数据
- `POST /api/documents/upload`：上传 docx
- `POST /api/documents/import`：导入表格到验规（需要同时传 `customerId` + `processId`）
- `POST /api/matching/preview`：匹配预览（支持 fileId/tableIndex + 手动列索引）
- `POST /api/matching/execute` / `GET /api/matching/download/{taskId}`：填充与下载

> 领域模型说明：**Customer 与 Process 是两份独立基础数据**；系统用 **(customerId + processId)** 的组合来表示“一整份验规”的范围，因此创建/导入/匹配时通常需要同时指定这两个值。

## 4. 运行测试

### 4.1 单元/集成测试（.NET）

```powershell
dotnet test AcceptanceSpecSystem.sln -c Debug
```

其中 `tests/AcceptanceSpecSystem.Api.Tests` 使用 `WebApplicationFactory` + SQLite in-memory 跑 API 集成测试。

### 4.2 前端构建验证

```powershell
cd web
pnpm build
```

## 5. 端到端测试（Console 工具）

项目内置 `tools/E2ETest`，可对“上传→表格→预览→填充→下载”做一次端到端验证：

```powershell
dotnet run --project tools/E2ETest/E2ETest.csproj -c Debug -- `
  --baseUrl http://localhost:5014 `
  --docx docs/example.docx `
  --tableIndex 0 `
  --projectColumnIndex 0 `
  --specificationColumnIndex 1 `
  --acceptanceColumnIndex 2 `
  --remarkColumnIndex 3
```

