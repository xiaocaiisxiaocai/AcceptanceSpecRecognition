## Context
表头字段识别词硬编码在后端 4 处，`ColumnMappingRules` 表虽有配置页与 CRUD API 但无种子，硬编码词只作叠加补充、用户不可管。Excel 另有一份前端词表绕过后端。本次将表头字段词收敛为数据库单一来源、代码不再硬编码，并统一 Excel 识别走后端。约束：不改 `ColumnMappingRule` 表结构（已够用）；不动内容特征词（`SpecificationLikelihoodScorer`）；识别匹配语义保持 `Contains`。

## Goals / Non-Goals
- Goals：表头字段词由 DB 唯一驱动、界面可管全部（含内置）、启动幂等播种、误删可恢复、Excel 与 Word 识别口径统一。
- Non-Goals：迁移内容特征词/单位符号；改动 Embedding/召回/重排/LLM 匹配主链；改 `ColumnMappingRule` schema；改客户级规则优先级语义。

## Decisions
- **Decision：默认词 catalog 放 Core（`ColumnMappingRuleDefaults`），Initializer 放 Api/Services（仿 `SystemPromptTemplateInitializer`）。** 运行期权威是 DB，catalog 仅作种子与"恢复默认"数据源，Core 便于单测与复用。
- **Decision：启动按字段 bootstrap 幂等，手动恢复按词补齐。** 启动时某 `TargetField` 无任何 `Source=Builtin && CustomerId==null` 规则才播种该字段全部默认词；已存在至少一条则整组跳过，避免"用户删单词后每次重启复活"。`restore-defaults` 是显式人工恢复动作，按词补齐缺失的默认词。
  - Alternative（逐词补齐，仿 PromptTemplate）：会导致删单词后重启复活，违背"界面可维护"。放弃。
- **Decision：MatchMode=Contains、Priority=0。** 现有匹配全是 `text.Contains(keyword)`；Equals 会漏"检验项目/规格要求"等复合表头；命中即固定 0.95、与顺序无关，Priority 不影响正确性。
- **Decision：去硬编码后 `DefaultSynonyms` 保留 4 个空键（非删整字典）。** `BuildEffectiveSynonyms` 合并结构与 `IdentifyColumn` 类型遍历不受影响，DB 词成为唯一来源。
- **Decision：`HasStructureHeaderSignal`、`IsSpecificationOnlyCandidate` 改为接收/查询已构造的 DB 词（`HeaderKeywordMatcher` / `extraSynonyms`）。** 调用链在 `RecognizeAsync` 已持有这些对象，透传即可，不重复构造。
- **Decision：Excel"自动识别字段行"复用后端派生结果快照（`TableImportConfig.recognizedExcelMapping`），不发新请求。** 智能模式 Excel 已走后端，按钮改为本地复用同源结果，杜绝前后端识别分歧。

## 与现有学习机制的协同
表头字段词迁库后并非"只能人工维护"。系统已有的自增长学习机制（`SmartConfigurationLearningService.ApplyLearningAsync`，在 `SmartConfigurationAppService.ConfirmAsync` 即 `/api/smart-config/confirm` 时触发）写入的正是同一张 `ColumnMappingRules` 表，与本次迁移天然同源、无需改造。迁库后表头词有四个来源，其中两个自动：
- **启动内置播种（本次新增）**：`Source=Builtin`、全局、`MatchMode=Contains`、`Priority=0`。管"泛化识别"。
- **AI 客户级学习（已有，自动）**：用户确认识别结果时，把"表头文本→字段"沉淀为 `Source=Learned`、客户级、`MatchMode=Equals`、`Priority=100` 的规则。管"该客户这一确切写法"。
- **AI 全局晋升（已有，自动）**：同一 (词, 字段) 被 ≥ `GlobalRulePromotionCustomerThreshold`（默认 2）个不同客户学习后，自动晋升为 `Source=Learned`、全局、`Priority=80` 规则，跨客户共享。
- **人工兜底**：配置页 `Source=Manual`，用于一次性/特殊/需立即生效的调整。

关键协同保证：
- 运行期 `GetEffectiveForCustomerAsync` 统一合并全局与客户级规则，四类来源同表共存、客户级优先。
- 本次启动 bootstrap 与 `restore-defaults` **只识别并补齐 `Source=Builtin && CustomerId==null`**，绝不增删改 Learned / Manual / 客户级规则，与学习机制零冲突。
- `Equals`（学习/精确）与 `Contains`（内置/泛化）互补，覆盖"确切写法"与"关键词泛化"两种场景。
- 去硬编码放大了学习价值：硬编码词表 AI 学不进、旧机制只能在 DB 侧叠加；迁库后内置与学习完全同源于 DB，人工可维护 + AI 可喂养才真正打通。

学习触发点是**智能识别确认流程**；未走该流程（纯手动配列）的导入不会自动学习，属预期（避免误学）。

## Risks / Trade-offs
- **误删/误禁内置词即时影响识别** → 三重缓解：Builtin 全局标记（bootstrap 只碰它）、启动按字段复活、`restore-defaults` 端点即时恢复；`GuessColumnTypeByData` 样本推断仍兜底不整表崩。
- **"重启不复活单词、恢复按钮会复活单词"语义需说清** → 界面文案提示启动兜底与手动恢复的差异。
- **多副本并发首启可能重复插入**（无唯一约束）→ bootstrap 前查存在；catalog 相同，最坏少量重复，可后续加唯一索引清理。
- **Excel 表头行差异**（前端 `headerRowStart` 1-based vs 后端 `headerRowIndex` 0-based）→ 复用后端派生 mapping 消除分歧，用例断言与智能模式初始 config 逐字段相等。

## Migration Plan
1. 先落 catalog + Initializer + 启动播种（新老库自动补齐 Builtin 词）。
2. 再去后端硬编码（DB 成唯一来源）。
3. 加 restore-defaults 端点 + 前端按钮。
4. 前端 Excel 改走后端、删死代码。
5. 测试 + `openspec validate --strict`。
- 回滚：Initializer 与去硬编码为同一提交范围；若识别退化，可临时恢复 `DefaultSynonyms` 内容并保留 DB 播种（叠加语义）过渡。

## Open Questions
- 无唯一约束是否本次补一个 `(TargetField, Pattern, CustomerId)` 唯一索引迁移以彻底防并发重复——倾向下一次单独处理，本次以查存在规避。
