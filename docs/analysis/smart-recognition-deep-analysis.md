# 验收规格管理系统智能结构识别修复收口报告

- **分析范围**：智能结构识别、Core Matching 架构治理、前端识别流程、测试入口与临时文件治理
- **分析对象**：当前分支 `feat/smart-recognition-simplification`
- **更新日期**：2026-07-03
- **结论摘要**：原报告列出的 1-7 项修复已完成代码落地。当前剩余不是功能阻断项，主要是提交拆分策略与后续持续治理。

## 一、修复状态总览

| 编号 | 原问题 | 当前状态 | 处理结果 |
|------|--------|----------|----------|
| 1 | 根目录 `tmp-*.log` 调试日志未治理 | 已处理 | `.gitignore` 已加入 `tmp-*.log`，本轮测试输出另补 `tmp-*.out`；未删除任何日志文件 |
| 2 | Core Matching 巨型服务脱离 500 行治理 | 已处理 | `Core/Matching/Services` 已拆分为 partial 文件，架构测试强制每个 `*.cs` 小于 500 行 |
| 3 | `ColumnMapping` / `DocumentStructureCandidate` / `SmartConfigurationRecognizedTable` 转换散落 | 已处理 | 新增 `SmartConfigurationRecognizedTableFactory` 与内部结构 `SmartConfigurationTableStructure`，集中处理转换边界 |
| 4 | `SmartConfigurationAppService` 与 `useDataImportPage.ts` 编排过重 | 已处理核心部分 | 后端学习逻辑拆到 `SmartConfigurationLearningService`；前端智能识别流程拆到 `useDataImportSmartStructureRecognition.ts` |
| 5 | `ai-services/config-selection.ts` 属范围外改动 | 已隔离覆盖 | 已补独立测试 `config-selection.test.ts`，提交层面是否拆分仍由最终提交策略决定 |
| 6 | 前端依赖损坏导致无法完整验证 | 已处理 | 已通过重新安装依赖恢复本地验证链路 |
| 7 | `web/tests` 的 `node:test` 与 Vitest 混用 | 已处理 | Vitest 只收集 `src/**/*.test.ts`；`web/tests/*.test.ts` 统一走 `pnpm test:node` |

## 二、关键修复明细

### 2.1 仅规格模式误判

`DocumentStructureHealthCheck.Evaluate` 已支持 `allowMissingProjectColumn`，调用方按识别结果传入上下文：

- 规则识别路径：无项目列时允许仅规格模式，不再被 `MissingProjectColumn` 误伤。
- LLM 融合路径：按 `candidate.IsSpecificationOnly` 判断是否允许缺项目列。
- 测试覆盖：`SmartConfigRecognizeApiTests.Recognize_WhenHighConfidenceResultIsSpecificationOnly_ShouldReturnAutoApply` 与 `DocumentStructureHealthCheckTests` 已覆盖。

### 2.2 评分逻辑收敛

已新增并复用 `SpecificationLikelihoodScorer` 相关能力，减少多处重复启发式：

- `DocumentIntelligenceService`
- `DocumentStructureHealthCheck`
- `RuleBasedMappingStrategy`
- 表格结构/规格特征相关测试

后续如继续治理，可再把阈值和权重做成更细粒度配置，但当前重复实现问题已收敛。

### 2.3 归档公开 API 移除

已删除旧的 `auto-detect` / `AutoConfigureAsync` 兼容链路：

- `SmartConfigController` 不再保留归档 Action。
- `SmartConfigurationAppService` 不再保留归档单表自动配置入口。
- `IDocumentIntelligenceService` 不再暴露 `AutoConfigureAsync`。
- 架构测试 `SmartConfiguration_ShouldNotKeepArchivedAutoDetectEndpoint` 已覆盖。

### 2.4 Core Matching 文件体量治理

`src/AcceptanceSpecSystem.Core/Matching/Services` 已拆分，当前最大文件行数：

| 文件 | 行数 |
|------|------|
| `SpecCanonicalizer.Knowledge.cs` | 458 |
| `PromptTemplateValidationService.cs` | 420 |
| `SemanticKernelMatchingService.Decisions.cs` | 404 |

架构测试 `CoreMatchingServiceFiles_ShouldNotGrowBeyondCurrentLargeFileBaseline` 已要求该目录所有 `*.cs` 文件小于 500 行。

### 2.5 智能结构识别内部表示收敛

新增 `SmartConfigurationRecognizedTableFactory`，统一以下转换：

- `FromTemplate`
- `FromColumnMapping`
- `FromCandidate`
- `ToStructureCandidate`
- `ToColumnMappingResult`
- `ToRecognizedTable`

`SmartConfigurationAppService` 不再散落 DTO 构造和候选转换逻辑。

### 2.6 应用层与前端编排拆分

后端：

- `SmartConfigurationAppService.cs` 当前约 380 行。
- 学习规则写入与全局规则晋升已拆到 `SmartConfigurationLearningService.cs`。
- `SmartConfigurationOptions` 已包含：
  - `StructureAdjudicationTimeoutSeconds`
  - `AutoApplyConfidenceThreshold`
  - `MinimumSpecificationNonEmptyRate`
  - `GlobalRulePromotionCustomerThreshold`

前端：

- `useDataImportPage.ts` 当前约 1107 行。
- 智能识别状态、识别调用、应用识别结果、确认回写、勾选同步与重置逻辑已拆到 `useDataImportSmartStructureRecognition.ts`。
- 行列换算能力已提取到 shared，供 `data-import` 与 `smart-fill` 复用。

### 2.7 前端测试入口统一

`web/package.json` 当前测试入口：

```json
{
  "test": "pnpm test:vitest && pnpm test:node",
  "test:vitest": "vitest run",
  "test:node": "node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/*.test.ts"
}
```

`web/vite.config.ts` 已限制 Vitest 只收集 `src/**/*.test.ts`，避免误收集 `web/tests` 的 `node:test` 静态源码断言测试。

## 三、剩余未修复项

当前没有必须继续修复的功能阻断项。

仍建议后续单独处理：

1. `ai-services/config-selection.ts` 是否拆成独立提交：这是提交组织问题，不影响当前代码正确性。
2. `useDataImportPage.ts` 仍超过 1000 行：智能识别相关职责已拆出，但导入预览、差异处理、分页等职责仍可后续继续拆分。
3. Core Matching 已满足 500 行架构红线，但仍是 partial 拆分形态；如后续维护成本继续升高，可再演进为显式 Handler/策略对象。
4. 根目录既有 `tmp-api-*.log`、`tmp-vite-*.log` 已被 `.gitignore` 覆盖；是否删除物理文件需用户另行确认。

## 四、验证记录

已执行并通过的关键验证：

- `pnpm typecheck`
- `pnpm test`
- `dotnet build src\AcceptanceSpecSystem.Application\AcceptanceSpecSystem.Application.csproj -c Debug --no-restore`
- `dotnet test tests\AcceptanceSpecSystem.Core.Tests\AcceptanceSpecSystem.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~DocumentIntelligenceServiceTests|FullyQualifiedName~SpecificationLikelihoodScorerTests|FullyQualifiedName~DocumentStructure|FullyQualifiedName~RuleBasedMappingStrategyTests" --logger "console;verbosity=minimal"`
- `dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~SmartConfigRecognize|FullyQualifiedName~SmartConfigConfirmLearningTests" --logger "console;verbosity=minimal"`

本报告更新后，应重新跑前端关键验证确认文档与忽略规则改动未影响测试链路。
