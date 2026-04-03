# 代码审查报告 — replace-text-preprocessing-with-matching-knowledge

> **审查分支：** `feat/evidence-driven-matching-engine`
> **审查日期：** 2026-03-27
> **审查对象：** Codex 对 `docs/superpowers/plans/2026-03-27-matching-knowledge-replacement.md` 的实现完成度

---

## 总体结论

**主体完成，存在 2 处实质遗漏，1 处 tasks.md 漏勾。评级：B+**

---

## 一、tasks.md checkbox 状态错误

**Task 1.4 实际已完成，但仍标 `[ ]`（漏勾）**

`src/AcceptanceSpecSystem.Data/Migrations/20260327091604_RemoveLegacyTextProcessingTables.cs:192-202` 的 `Up()` 中明确执行了：

```
DropTable("Keywords")
DropTable("SynonymWords")
DropTable("TextProcessingConfigs")
DropTable("SynonymGroups")
```

四张旧表全部删除，`Down()` 也有完整回滚逻辑。这是 Codex 的疏忽，实际不欠技术债。

**修复方式：** 将 `tasks.md` 中 `1.4` 的 `[ ]` 改为 `[x]`。

---

## 二、实质遗漏（Task 4.1 偷工）

### 缺口 1：旧接口 404 测试只覆盖了 1/3 条（高危）

`tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs:77-82` 的 `LegacyTextProcessingApis_ShouldReturnNotFound` **只断言了一个端点**：

```csharp
var response = await _client.GetAsync("/api/text-processing/config");
response.StatusCode.Should().Be(HttpStatusCode.NotFound);
```

`openspec/changes/replace-text-preprocessing-with-matching-knowledge/specs/api/spec.md` 明确要求移除三条旧接口：

| 旧端点 | 测试覆盖状态 |
|--------|----------|
| `GET /api/text-processing/config` | ✅ 已覆盖 |
| `GET /api/synonyms` | ❌ **未测** |
| `GET /api/keywords` | ❌ **未测** |

对应 Controller 文件虽已被删除（git status 标记 `D`），但没有回归测试保护，将来误加路由不会被发现。

**修复方式：** 在 `LegacyTextProcessingApis_ShouldReturnNotFound` 补加：

```csharp
(await _client.GetAsync("/api/synonyms")).StatusCode
    .Should().Be(HttpStatusCode.NotFound);
(await _client.DeleteAsync("/api/synonyms/1")).StatusCode
    .Should().Be(HttpStatusCode.NotFound);
(await _client.GetAsync("/api/keywords")).StatusCode
    .Should().Be(HttpStatusCode.NotFound);
```

---

### 缺口 2：MinimalTextPreprocessingPipeline 测试只有正向断言，缺少否定断言（中危）

`tests/AcceptanceSpecSystem.Core.Tests/MatchingKnowledgeDrivenNormalizationTests.cs` 只有 **2 个正向断言**：

```csharp
session.Process("  PASS \r\n NG\t ").Should().Be("PASS NG");
session.Process("宽尺寸   <  0.5cm").Should().Be("宽尺寸 < 0.5cm");
```

`specs/matching-engine/spec.md` 要求最小归一化**不执行**繁简转换、同义词替换、单位展开。当前没有任何否定用例验证旧行为已被剔除，若有人意外引入旧逻辑，测试不会失败。

**修复方式：** 补加 3 个否定断言：

```csharp
// 繁体不被转换
session.Process("寬度").Should().Be("寬度");
// 同义词不被替换
session.Process("松下").Should().Be("松下");
// 单位不被展开
session.Process("厘米").Should().Be("厘米");
```

---

## 三、合格项（无问题）

| 项目 | 文件 | 结论 |
|------|------|------|
| MatchingKnowledgeConfig 实体/仓储/迁移 | `Data/Entities/`, `Data/Repositories/`, `Data/Migrations/` | ✅ 完整 |
| 数据迁移（旧同义词 → 新结构） | `20260327091604_RemoveLegacyTextProcessingTables.cs` | ✅ 动态 JSON_MERGE_PATCH，含 WHERE NOT EXISTS 幂等保护 |
| GET / PUT / POST reset API | `Api/Controllers/MatchingKnowledgeController.cs` | ✅ 完整，有权限标注 |
| ConfigurationMatchingKnowledgeProvider | `Api/Services/ConfigurationMatchingKnowledgeProvider.cs` | ✅ 数据库读取 + 默认值 fallback |
| MinimalTextPreprocessingPipeline 实现 | `Core/TextProcessing/Services/MinimalTextPreprocessingPipeline.cs` | ✅ 仅做空白折叠，不做语义处理 |
| MatchingWorkflowService 三处 tpSession | `Api/Services/MatchingWorkflowService.cs:192,873,1646` | ✅ 均注入 MinimalTextPreprocessingPipeline，符合 spec |
| 前端页面 | `web/src/views/config/matching-knowledge/index.vue` | ✅ 591 行完整实现，五个编辑区域均可用 |
| 旧路由/菜单入口清除 | `web/src/router/modules/other.ts` | ✅ 已只剩 audit-logs |
| 权限种子 + 权限测试 | `tests/AcceptanceSpecSystem.Api.Tests/AuthPermissionsTests.cs` | ✅ 三个新权限存在，三个旧权限 IsActive=false，登录快照也验证 |
| 旧 Controller 移除 | `SynonymsController.cs`、`KeywordsController.cs`、`TextProcessingController.cs` | ✅ git status 标记 D，已删除 |
| 迁移 Down() 回滚逻辑 | `20260327091604_RemoveLegacyTextProcessingTables.cs:206+` | ✅ 可回滚 |

---

## 四、待办汇总

| 优先级 | 文件 | 操作 |
|--------|------|------|
| 🔴 必须 | `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs` | 补加 `/api/synonyms`、`/api/keywords` 的 404 断言 |
| 🟡 建议 | `tests/AcceptanceSpecSystem.Core.Tests/MatchingKnowledgeDrivenNormalizationTests.cs` | 补加繁简/同义词/单位不转换的否定断言 |
| ⚪ 文档 | `openspec/changes/replace-text-preprocessing-with-matching-knowledge/tasks.md` | 将 `1.4` 标记为 `[x]` |
