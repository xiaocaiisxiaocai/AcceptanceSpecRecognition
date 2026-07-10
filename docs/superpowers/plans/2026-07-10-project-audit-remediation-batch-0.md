# Project Audit Remediation Batch 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 解除浏览器 profile 和真实业务样本的 Git 跟踪，提供可公开的合成工具夹具，并消除仓库中可直接复用的固定开发/部署口令。

**Architecture:** 仓库敏感资产继续保留在开发者本地，但通过根目录精确 ignore 规则阻止再次提交。开发工具统一从 `tools/Fixtures/synthetic_specs.json` 读取默认数据，并继续允许命令行显式传入本地真实样本。配置示例保留键名和非敏感结构，所有敏感值为空，开发连接串从被忽略的本地配置提供。

**Tech Stack:** Git、.NET 8/xUnit/FluentAssertions、PowerShell、JSON、Docker Compose 环境变量。

---

## 文件边界

**创建：**

- `tools/Fixtures/synthetic_specs.json`：不含客户信息的工具默认输入。
- `tests/AcceptanceSpecSystem.Api.Tests/RepositoryHygieneTests.cs`：仓库资产和工具默认值守卫。
- `tests/AcceptanceSpecSystem.Api.Tests/DevelopmentConfigurationGuardTests.cs`：开发/部署示例配置守卫。

**修改：**

- `.gitignore`：增加根目录真实样本精确忽略规则。
- `tools/ParaphraseGenerator/Program.cs`：默认输入改为合成夹具。
- `tools/GenerateSemanticTestData.ps1`：默认输入改为合成夹具。
- `tools/GenerateParaphrasedExcelViaApi.ps1`：默认输入改为合成夹具。
- `tools/GenerateParaphrasedExcel.ps1`：默认输入改为合成夹具。
- `tools/FilterGrayZoneSamples.ps1`：默认输入改为合成夹具。
- `tools/ExtractGrayZoneSources.ps1`：默认输入改为合成夹具。
- `.env.docker.example`：敏感值置空，数据库名改为稳定产品名。
- `src/AcceptanceSpecSystem.Api/Properties/launchSettings.json`：删除固定本地连接串。
- `docs/DEV.md`：说明本地开发连接串配置方式。
- `docs/DEPLOY-DOCKER.md`：删除固定口令示例，明确敏感值必须填写。

**仅解除 Git 跟踪、保留物理文件：**

- `output/`
- `huaian_specs.json`
- `huaian_specs_500.json`
- `淮安庆鼎_智能填充测试说明.md`

## Task 1：建立仓库资产失败守卫

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/RepositoryHygieneTests.cs`

- [ ] **Step 1：编写失败测试**

测试应读取仓库文件并验证：

```csharp
[Fact]
public void LocalSensitiveArtifacts_ShouldBeIgnored_AndToolsShouldUseSyntheticFixture()
{
    var ignore = ReadFile(".gitignore");
    ignore.Should().Contain("/huaian_specs.json");
    ignore.Should().Contain("/huaian_specs_500.json");
    ignore.Should().Contain("/淮安庆鼎_智能填充测试说明.md");

    var fixturePath = Path.Combine(GetRepositoryRoot(), "tools", "Fixtures", "synthetic_specs.json");
    File.Exists(fixturePath).Should().BeTrue();

    using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
    var items = document.RootElement.GetProperty("data").GetProperty("items");
    items.GetArrayLength().Should().BeGreaterThanOrEqualTo(6);

    foreach (var relativePath in ToolFiles)
    {
        var content = ReadFile(relativePath);
        content.Should().Contain("tools/Fixtures/synthetic_specs.json");
        content.Should().NotContain("\"huaian_specs.json\"");
    }
}
```

测试 helper 使用与 `ArchitectureBoundaryTests` 相同的仓库根目录向上查找方式，不调用 Git，不读取真实样本内容。

- [ ] **Step 2：运行测试确认失败**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~RepositoryHygieneTests
```

Expected: FAIL，原因包括缺少 ignore 规则、合成夹具不存在和工具仍引用旧默认文件。

## Task 2：添加合成夹具并切换工具默认输入

**Files:**

- Create: `tools/Fixtures/synthetic_specs.json`
- Modify: `.gitignore`
- Modify: 上述 6 个工具入口文件

- [ ] **Step 1：添加根目录精确 ignore 规则**

在本地 scratch 区增加：

```gitignore
# Local real-world samples (keep outside version control)
/huaian_specs.json
/huaian_specs_500.json
/淮安庆鼎_智能填充测试说明.md
```

- [ ] **Step 2：创建合成 JSON**

结构必须保持工具现有契约：

```json
{
    "code": 0,
    "message": "ok",
    "data": {
        "items": [
            {
                "id": 1,
                "customerId": 1,
                "processId": 1,
                "machineModelId": 1,
                "customerName": "示例客户",
                "processName": "示例制程",
                "machineModelName": "示例机型",
                "project": "输送方向",
                "specification": "物料从左侧进入并从右侧送出",
                "acceptance": "方向检查通过",
                "remark": null
            }
        ],
        "total": 8,
        "page": 1,
        "pageSize": 8,
        "totalPages": 1,
        "hasNext": false,
        "hasPrevious": false
    }
}
```

实际夹具提供至少 8 条记录，并包含同项目近义规格，使灰区提取工具仍有可用输入。不得包含真实客户、厂区、人员、URL、数据库 ID 或原样本文本。

- [ ] **Step 3：切换工具默认值**

6 个工具统一使用：

```text
tools/Fixtures/synthetic_specs.json
```

仅修改默认参数；显式参数优先级保持不变。

- [ ] **Step 4：运行失败守卫确认通过**

Run: Task 1 的定向测试命令。

Expected: PASS。

- [ ] **Step 5：验证合成 JSON 契约**

Run:

```powershell
$json = Get-Content -Raw tools/Fixtures/synthetic_specs.json | ConvertFrom-Json
if ($json.data.items.Count -lt 8) { throw "synthetic fixture item count is too small" }
```

Expected: exit 0。

## Task 3：解除敏感资产 Git 跟踪

**Files:**

- Git index only: `output/` 和 3 个真实样本文件

- [ ] **Step 1：记录本地文件存在状态**

Run:

```powershell
Test-Path output
Test-Path huaian_specs.json
Test-Path huaian_specs_500.json
Test-Path '淮安庆鼎_智能填充测试说明.md'
```

Expected: 全部为 `True`。

- [ ] **Step 2：仅从索引移除**

Run:

```powershell
git rm -r --cached -- output
git rm --cached -- huaian_specs.json huaian_specs_500.json '淮安庆鼎_智能填充测试说明.md'
```

- [ ] **Step 3：验证本地文件仍存在且不再跟踪**

Run:

```powershell
Test-Path output
Test-Path huaian_specs.json
git ls-files output
git ls-files huaian_specs.json huaian_specs_500.json '淮安庆鼎_智能填充测试说明.md'
```

Expected: `Test-Path` 为 `True`，两个 `git ls-files` 命令无输出。

- [ ] **Step 4：提交仓库资产治理**

```powershell
git add .gitignore `
  tools/Fixtures/synthetic_specs.json `
  tools/ParaphraseGenerator/Program.cs `
  tools/GenerateSemanticTestData.ps1 `
  tools/GenerateParaphrasedExcelViaApi.ps1 `
  tools/GenerateParaphrasedExcel.ps1 `
  tools/FilterGrayZoneSamples.ps1 `
  tools/ExtractGrayZoneSources.ps1 `
  tests/AcceptanceSpecSystem.Api.Tests/RepositoryHygieneTests.cs
git commit -m "chore: 收敛本地样本与浏览器产物"
```

提交前确认暂存删除只涉及 `output/` 和已批准的 3 个真实样本。

## Task 4：建立配置失败守卫

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/DevelopmentConfigurationGuardTests.cs`

- [ ] **Step 1：编写失败测试**

```csharp
[Fact]
public void TrackedExamples_ShouldNotContainReusableCredentials()
{
    var values = ReadEnvFile(".env.docker.example");
    values["MYSQL_ROOT_PASSWORD"].Should().BeEmpty();
    values["MYSQL_PASSWORD"].Should().BeEmpty();
    values["JWT_SIGNING_KEY"].Should().BeEmpty();
    values["AUTH_SEED_ADMIN_PASSWORD"].Should().BeEmpty();
    values["AUTH_SEED_COMMON_PASSWORD"].Should().BeEmpty();
    values["MYSQL_DATABASE"].Should().Be("acceptance_spec_db");

    ReadFile("src/AcceptanceSpecSystem.Api/Properties/launchSettings.json")
        .Should().NotContain("ConnectionStrings__DefaultConnection");
}
```

- [ ] **Step 2：运行测试确认失败**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~DevelopmentConfigurationGuardTests
```

Expected: FAIL，当前示例包含固定值且 launchSettings 含连接串。

## Task 5：清理配置示例并更新文档

**Files:**

- Modify: `.env.docker.example`
- Modify: `src/AcceptanceSpecSystem.Api/Properties/launchSettings.json`
- Modify: `docs/DEV.md`
- Modify: `docs/DEPLOY-DOCKER.md`

- [ ] **Step 1：将敏感示例值置空**

保留以下键但值为空：

```dotenv
MYSQL_ROOT_PASSWORD=
MYSQL_DATABASE=acceptance_spec_db
MYSQL_USER=acceptance
MYSQL_PASSWORD=
JWT_SIGNING_KEY=
AUTH_SEED_ADMIN_PASSWORD=
AUTH_SEED_COMMON_PASSWORD=
```

- [ ] **Step 2：删除 launchSettings 固定连接串**

`environmentVariables` 仅保留：

```json
"ASPNETCORE_ENVIRONMENT": "Development"
```

- [ ] **Step 3：更新开发和部署文档**

- `docs/DEV.md` 增加被忽略的 `appsettings.Development.json` 配置示例，密码使用 `REPLACE_WITH_LOCAL_PASSWORD` 占位符。
- `docs/DEPLOY-DOCKER.md` 不再展示任何固定密码，明确复制 `.env.docker.example` 后必须填写 5 个敏感键，否则容器启动或健康检查应失败。

- [ ] **Step 4：运行配置守卫确认通过**

Run: Task 4 的定向测试命令。

Expected: PASS。

- [ ] **Step 5：运行批次 0 回归**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RepositoryHygieneTests|FullyQualifiedName~DevelopmentConfigurationGuardTests"
```

Expected: 全部通过。

Run:

```powershell
dotnet build AcceptanceSpecSystem.sln -c Release --no-restore -p:TreatWarningsAsErrors=true
```

Expected: 0 警告、0 错误。

- [ ] **Step 6：提交配置治理**

```powershell
git add .env.docker.example src/AcceptanceSpecSystem.Api/Properties/launchSettings.json docs/DEV.md docs/DEPLOY-DOCKER.md tests/AcceptanceSpecSystem.Api.Tests/DevelopmentConfigurationGuardTests.cs
git commit -m "chore: 清理可复用开发示例口令"
```

## Task 6：批次收口

- [ ] **Step 1：检查工作树和提交范围**

```powershell
git status --short
git log -3 --oneline
git diff HEAD~2 HEAD --check
```

Expected: 工作树干净；最近提交只包含计划文档和批次 0 两个主题提交；diff check 无输出。

- [ ] **Step 2：更新审核报告状态**

在 `docs/项目深度审核与优化建议-2026-07-10.md` 追加实施状态，准确记录：

- P0-01 当前树已解除跟踪，但旧历史未清理。
- P1-01 当前树已解除跟踪并由合成夹具替代，但旧历史未清理。
- P2-12 已完成配置治理。

- [ ] **Step 3：提交状态记录**

```powershell
git add docs/项目深度审核与优化建议-2026-07-10.md
git commit -m "docs: 记录仓库治理实施结果"
```
