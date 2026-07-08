## 1. 后端种子（Initializer 幂等补齐）
- [x] 1.1 新增 `src/AcceptanceSpecSystem.Core/Documents/Intelligence/ColumnMappingRuleDefaults.cs`：静态默认词 catalog（Project/Specification/Acceptance/Remark 四组，合并去重）
- [x] 1.2 新增 `src/AcceptanceSpecSystem.Api/Services/ColumnMappingRuleInitializer.cs`：按字段 bootstrap 幂等 `EnsureAsync`（Builtin、全局、MatchMode=Contains、Priority=0）
- [x] 1.3 DI 注册 `AddScoped<ColumnMappingRuleInitializer>()`（`ServiceCollectionExtensions.cs`）
- [x] 1.4 `Program.cs` 启动块在 `SystemPromptTemplateInitializer.EnsureAsync()` 之后调用 `ColumnMappingRuleInitializer.EnsureAsync()`

## 2. 后端去硬编码（保留内容特征词不动）
- [x] 2.1 `RuleBasedMappingStrategy.DefaultSynonyms` 改为 4 键空数组；保留 `LooksLikeAcceptanceStandard/MethodColumn`、`GuessColumnTypeByData`、Levenshtein
- [x] 2.2 删 `HeaderKeywordMatcher.BuiltInKeywords`；`FromExtraSynonyms` 仅从 `extraSynonyms` 构造
- [x] 2.3 `SmartConfigurationTableRoutingService.HasStructureHeaderSignal` 改签名接收 `HeaderKeywordMatcher`，删内联 Contains 链；调用链透传 matcher
- [x] 2.4 `IsSpecificationOnlyCandidate` 内联词改查 `extraSynonyms[ColumnType.Project]`
- [x] 2.5 确认 `SpecificationLikelihoodScorer` 内容特征词全部保留不动

## 3. 空库/全删风险兜底
- [x] 3.1 Initializer 补齐逻辑抽成可注入方法，供端点复用
- [x] 3.2 `ColumnMappingRulesController` 新增 `POST /api/column-mapping-rules/restore-defaults`（可选 `targetField`），仅补齐缺失的 Builtin 全局词，不动 Manual/Learned/客户级
- [x] 3.3 前端 `web/src/api/column-mapping-rules.ts` 加 `restoreColumnMappingRuleDefaults`；`config/column-mapping-rules/index.vue` 加"恢复默认词"按钮 + 语义提示文案

## 4. 前端 Excel 统一走后端（删前端词表）
- [x] 4.1 `dataImport.types.ts` 的 `TableImportConfig` 加可选 `recognizedExcelMapping?`
- [x] 4.2 `dataImport.smartRecognition.ts` 构造 Excel config 时存入后端派生 excelMapping 快照
- [x] 4.3 `ExcelColumnMapping.vue` 加 `detectedMapping?` prop；删 `detectedFieldMapping` computed 与 `detectExcelFieldMappingFromPreview` import；按钮改"重置为识别结果"、`:disabled="!detectedMapping"`
- [x] 4.4 `index.vue` 给 `<ExcelColumnMapping>` 传 `:detected-mapping="cfg.recognizedExcelMapping"`
- [x] 4.5 `buildExcelColumnOptions` 脱离 `getBestExcelFieldRowCandidate`，`displayHeaders` 改用 `previewData?.headers`
- [x] 4.6 删死代码：`scoreFieldHeader`/`evaluateExcelFieldRow`/`getBestExcelFieldRowCandidate`/`detectExcelFieldMappingFromPreview`/`normalizeHeaderText`/`containsAny` 及相关类型；清理残留 import

## 5. 测试与验证
- [x] 5.1 后端 `ColumnMappingRuleInitializerTests`：幂等、按字段 bootstrap、不碰 Manual/Learned/客户级/disabled
- [x] 5.2 `RuleBasedMappingStrategy` 单测：仅 `extraSynonyms` 正确映射；`extraSynonyms` 空走 Unknown/样本推断
- [x] 5.3 更新 `HasStructureHeaderSignal`/`ShouldUseStructureAdjudication`/`IsSpecificationOnlyCandidate` 相关单测签名；`SmartConfigRecognizeApiTests` 依赖播种验证
- [x] 5.4 前端 `dataImport.smartRecognition.test.ts` / `data-import-excel-range.test.ts` 用例更新；删死代码后无残留 import
- [x] 5.5 `dotnet test AcceptanceSpecSystem.sln -c Debug`、`cd web && pnpm test && pnpm typecheck`、`openspec validate migrate-column-mapping-keywords-to-db --strict` 全绿
