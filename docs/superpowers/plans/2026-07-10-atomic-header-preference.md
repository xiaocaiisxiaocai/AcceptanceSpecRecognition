# 原子表头优先识别 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让重复分组结构中的完整叶级表头优先作为单行表头，并把确认卡中的项目列放到规格列之前。

**Architecture:** 在 `SmartConfigurationAppService` 的表头范围决策入口增加窄范围判定：候选行覆盖四类字段且存在重复字段标题时，直接采用该候选行；否则继续现有多行扩展。列识别和左右优先级不改动，前端只调整字段布局顺序。

**Tech Stack:** .NET 8、xUnit、ClosedXML、Vue 3、Element Plus、Node test runner

---

### Task 1: 增加真实结构回归测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeHeaderApiTests.cs`

- [ ] **Step 1: 写入失败的 API 回归测试**

构造带前置说明、分组标题、重复 `規格`/`OK/NG` 叶级标题和首条数据的 Excel，断言：

```csharp
table.GetProperty("headerRowIndex").GetInt32().Should().Be(7);
table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(8);
table.GetProperty("projectColumnIndex").GetInt32().Should().Be(2);
table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(3);
table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(8);
table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(9);
```

- [ ] **Step 2: 运行测试并确认按旧行为失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~SmartConfigRecognizeMultiHeaderApiTests.Recognize_WhenRepeatedLeafHeadersAreComplete_ShouldPreferSingleLeafHeaderRow"`

Expected: FAIL，旧逻辑返回更早的表头行和多行表头。

### Task 2: 实现重复叶级表头优先

**Files:**
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeHeaderApiTests.cs`

- [ ] **Step 1: 增加最小识别条件**

在 `HeaderKeywordMatcher` 内基于现有规则判断候选行：

```csharp
public bool IsCompleteRepeatedLeafHeader(RowData row)
```

条件为：非空单元格合计覆盖 `Project`、`Specification`、`Acceptance`、`Remark`，并且至少一个已命中规则的标题文本在该行重复出现。

- [ ] **Step 2: 在表头范围检测入口优先返回单行**

```csharp
if (headerKeywordMatcher.IsCompleteRepeatedLeafHeader(detectionTable.Rows[anchorRowIndex]))
{
    return new HeaderProfile(anchorRowIndex, 1);
}
```

- [ ] **Step 3: 运行新增测试和既有多行表头测试**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~SmartConfigRecognizeMultiHeaderApiTests"`

Expected: PASS，新增样本采用单行，既有普通多行表头仍保持原结果。

### Task 3: 调整确认卡字段顺序

**Files:**
- Modify: `web/src/views/shared/SmartStructureConfirmCard.vue`
- Modify: `web/tests/data-import-confirm-layout.test.ts`

- [ ] **Step 1: 增加失败的源码结构测试**

断言确认表单中的 `项目列` 节点位于 `规格列` 节点之前。

- [ ] **Step 2: 运行测试并确认失败**

Run: `cd web; node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts`

Expected: FAIL，当前规格列位于项目列之前。

- [ ] **Step 3: 交换两个字段块并运行测试**

仅交换对应 `el-col`，不调整数据模型、校验和样式。

Run: `cd web; node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts`

Expected: PASS。

### Task 4: 真实文件与关键路径验证

**Files:**
- Test: `C:\Users\SAC\Desktop\验收规范\苏州群策\翻板机验收规范-SAC20260514.xlsx`

- [ ] **Step 1: 运行真实样本验证**

Run: 设置 `SMART_CONFIG_REAL_SAMPLE_PATH` 后运行 `SmartConfigRealSampleValidationTests`。

Expected: 工作表 1 返回 `headerRowIndex=7`、`headerRowCount=1`、`dataStartRowIndex=8`、列索引 `2/3/8/9`。

- [ ] **Step 2: 运行类型检查和定向测试**

Run: `cd web; pnpm typecheck`

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~SmartConfigRecognizeMultiHeaderApiTests|FullyQualifiedName~SmartConfigRealSampleValidationTests"`

Expected: 全部通过且无失败。
