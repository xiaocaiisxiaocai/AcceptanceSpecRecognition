# 全项目审查报告

审查日期：2026-03-28

审查基线：`3d6a96f`

审查范围：`src/`、`web/`、`tests/`、运行配置、OpenSpec 落地后的当前工作树

审查方式：本地静态审查 + 并行子代理分域审查 + 命令验证；未使用 `augment-context-engine-mcp`

## 修复回执（2026-03-28 更新）

下文主体保留的是初次审查快照；本节用于标记当前代码状态，避免报告与仓库再次漂移。

本轮已修复：

1. `WordFile` 文件级归属与访问控制缺口。
2. 内置 `admin/common` 角色可被误改导致的 RBAC 自锁风险。
3. `appsettings.Production.json` 的 CORS 通配配置与启动校验冲突。
4. `common` 角色缺失主流程按钮权限、`matching-fill` 权限命名漂移。
5. Prompt 模板页权限绑定错误。
6. 旧 `/config/*` 路由兼容挂载位置错误。
7. `FileHash` 语义不一致。
8. 核心匹配的决策阈值、Embedding 批量短返回保护、复合数值/型号证据聚合。
9. 前端 `build` 未纳入 `typecheck` 的质量门缺口。
10. `WordDocumentParser` / `WordDocumentWriter` 可空引用编译告警。

按你的明确要求，以下项保留为“接受现状”而非修复目标：

1. 数据库连接串明文配置。
2. JWT Key 明文配置。

本轮修复后的验证结果：

- `dotnet build .\\AcceptanceSpecSystem.sln -c Debug`
  结果：通过。`0` 警告，`0` 错误。
- `dotnet test .\\AcceptanceSpecSystem.sln -c Debug`
  结果：通过。`289` 通过，`2` 跳过，`0` 失败。
- `pnpm build`（`web/`）
  结果：通过，且已先执行 `pnpm typecheck`。

## 结论摘要

当前项目不是“整体不可用”，但存在一组明确的高风险问题，主要集中在 4 个方向：

1. 权限与数据边界存在真实缺口，尤其是文件资源与默认角色能力。
2. 生产配置与密钥管理存在直接上线风险。
3. 核心匹配链路的若干边界条件和契约没有完全闭环。
4. 前端质量门不足，已经存在 `typecheck` 失败但 `build` 仍通过的情况。

本次确认的重点问题如下：

- `P1`：6 条
- `P2`：7 条
- `P3`：2 条

## 命令验证

本次实际执行并核实了以下命令：

- `dotnet test .\AcceptanceSpecSystem.sln -c Debug`
  结果：通过。`266` 通过，`2` 跳过，`0` 失败。
  备注：编译阶段仍有 `WordDocumentParser` / `WordDocumentWriter` 的空引用告警。

- `pnpm build`（`web/`）
  结果：通过。
  备注：产物总大小约 `2.53 MB`，最大单个 JS chunk 约 `1.3 MB`；构建期还提示 `baseline-browser-mapping` 数据过旧，以及 `SAA.svg` 回退到默认 loader。

- `pnpm typecheck`（`web/`）
  结果：失败。
  失败点：`web/src/views/smart-fill/index.vue:661` 访问了不存在的 `manualConfirmed` 字段。

## 详细发现

### P1

#### 1. `WordFile` 没有归属字段，文档接口存在跨数据范围访问缺口

影响：

- `WordFile` 实体没有 `CompanyId`、`CreatedByUserId`、`OwnerOrgUnitId` 一类归属字段。
- 文档列表、表格读取、导入、删除都只是按 `id` 直接取文件。
- 规格的数据范围控制没有同步落到文件资源层，导致“规格有 scope，源文件无 scope”。

主要证据：

- `src/AcceptanceSpecSystem.Data/Entities/WordFile.cs:6`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:57`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:203`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:245`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:420`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:647`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:900`

判断依据：

- `GetFiles` 会枚举所有文件，只是额外按当前用户 scope 统计 `SpecCount`，并没有对文件本身做过滤。
- `GetTables` / `GetTablePreview` / `ImportData` / `ImportExcelData` / `DeleteFile` 都没有文件级 scope 校验。

建议：

- 给 `WordFile` 增加归属字段并补迁移回填。
- 上传时记录当前公司、组织、用户归属。
- 所有文件接口统一执行文件级 scope 校验。
- 增加“普通用户访问越权文件返回 `403` 或 `404`”的集成测试。

#### 2. 内置角色可被停用或清空权限，存在 RBAC 自锁风险

影响：

- 当前接口允许直接修改 `admin/common` 这类内置角色。
- 可以把内置角色改成 `IsActive = false`，也可以把权限清空。
- 更新后又会提升相关用户的 `PermissionVersion`，强制旧 token 失效。
- 登录上下文只装载激活角色的权限，最终可能把后台彻底锁死，只能人工改库恢复。

主要证据：

- `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs:143`
- `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs:161`
- `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs:164`
- `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs:319`
- `src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs:104`
- `tests/AcceptanceSpecSystem.Api.Tests/AuthRolesTests.cs:57`

判断依据：

- `Update` 里没有对 `IsBuiltIn` 做保护。
- `BuildContext` 只采集激活角色及其激活权限。
- 现有测试还把“内置角色允许被更新并停用”当成成功路径。

建议：

- 禁止停用内置角色，或至少禁止停用最后一个管理员能力集。
- 禁止把内置角色权限清空到不可恢复状态。
- 把当前相反的测试基线改成回归保护。

#### 3. 生产配置与启动校验冲突，当前 `Production` 配置会直接启动失败

影响：

- `Program` 明确禁止 `Cors:AllowedOrigins` 为空或 `*`。
- 但 `appsettings.Production.json` 正好配置了 `["*"]`。
- 只要按仓库内默认生产配置启动，就会在启动期抛异常，服务无法起来。

主要证据：

- `src/AcceptanceSpecSystem.Api/Program.cs:228`
- `src/AcceptanceSpecSystem.Api/appsettings.Production.json:13`

建议：

- 把生产环境 CORS 改为显式白名单。
- 增加一条配置装配测试，验证 `Production` 环境下不会因配置自相矛盾而启动失败。

#### 4. 数据库连接串和 JWT 签名密钥被硬编码，而且还是运行时回退值

影响：

- 代码库直接提交了数据库口令和 JWT signing key。
- 连接串缺失时，程序不会 fail fast，而是回退到 `AppDbContext.DefaultConnectionString`。
- 设计时工厂也直接使用该默认连接串。

主要证据：

- `src/AcceptanceSpecSystem.Api/Program.cs:113`
- `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs:109`
- `src/AcceptanceSpecSystem.Data/Context/AppDbContextFactory.cs:19`
- `src/AcceptanceSpecSystem.Api/appsettings.json:10`
- `src/AcceptanceSpecSystem.Api/appsettings.json:16`
- `src/AcceptanceSpecSystem.Api/appsettings.Production.json:10`
- `src/AcceptanceSpecSystem.Api/appsettings.Production.json:16`

建议：

- 移除源码中的口令和签名密钥，改用环境变量或 secret store。
- 缺少连接串时直接启动失败，不要回退到本地 root 库。
- 立即轮换现有数据库口令和 JWT key。

#### 5. 默认 `common` 角色没有前端主流程所需的按钮权限

影响：

- 默认 `common` 角色只分配了 `menu:*`、`page:*`、`api:*`，没有对应的 `btn:*`。
- 前端关键动作统一按 `btn:*` 做显隐和守卫。
- 结果是用户能进入页面，但核心 CTA 被隐藏或拦住，尤其是导入、填充、文件对比。

主要证据：

- `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs:499`
- `src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs:113`
- `web/src/utils/auth.ts:160`
- `web/src/views/data-import/index.vue:60`
- `web/src/views/file-compare/index.vue:55`
- `web/src/views/smart-fill/index.vue:47`

建议：

- 给默认业务角色补齐前端真实消费的 `btn:*` 权限。
- 至少覆盖上传、导入、预览、执行、下载这些主流程按钮。
- 增加一条普通用户登录后的 UI 权限回归测试。

#### 6. `matching-fill` 权限命名没有收敛，前后端与默认权限种子已漂移

影响：

- 后端已经把 `llm-stream`、`execute`、`execute-batch` 归到 `matching-fill` 资源。
- 前端仍然在部分地方使用旧的 `btn:matching:*`。
- 默认 `common` 权限种子也还保留旧的 `api:matching:execute*` / `api:matching:llm-stream`。
- 这会导致界面显隐和接口鉴权不一致，表现为“有权限但看不到按钮”或“页面可见但点击 403”。

主要证据：

- `src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs:20`
- `src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs:27`
- `src/AcceptanceSpecSystem.Api/Controllers/MatchingExecutionController.cs:36`
- `src/AcceptanceSpecSystem.Api/Controllers/MatchingReuseController.cs:28`
- `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs:513`
- `web/src/views/smart-fill/index.vue:49`
- `web/src/views/smart-fill/index.vue:50`

建议：

- 统一权限资源命名来源，前端、后端、权限种子、角色模板全部收口到 `matching-fill`。
- 增加一条权限矩阵回归，覆盖 `preview / llm-stream / execute / execute-batch / download`。

### P2

#### 7. 核心匹配决策和工作流阈值是两套规则，语义已经分裂

影响：

- 核心层 `DetermineDecision` 只看冲突、警告和歧义，不看 `Score`，也不看 `MatchingConfig.HighConfidenceThreshold`。
- 工作流层又在 `CanApplyMatchedSpec` 里单独用 `highConfidenceThreshold` 决定是否允许真正落盘。
- 结果是“预览决策 / DTO 决策”和“最终可执行条件”不是一套规则，容易误导上层。

主要证据：

- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:533`
- `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs:174`
- `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs:344`
- `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs:575`
- `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs:1961`
- `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs:2002`

建议：

- 统一“最佳候选”“自动采用”“高置信度”的判定规则。
- 把阈值显式传入核心决策，或者明确规定核心层不负责最终自动采用决策。
- 补一条低分候选与自定义阈值的端到端回归测试。

#### 8. 文档上传接口返回的 `FileHash` 与实际入库值不一致，且与其他流程语义冲突

影响：

- 上传时保存的 `FileHash` 是随机 GUID。
- 返回给前端的 `FileHash` 又是另一个随机 GUID。
- 但文件对比等流程把 `FileHash` 当作真实内容哈希使用。
- 同一字段同时承担“随机占位符”和“内容指纹”两种语义，后续去重、追踪和联动都容易出错。

主要证据：

- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:157`
- `src/AcceptanceSpecSystem.Api/Controllers/DocumentsController.cs:186`
- `src/AcceptanceSpecSystem.Api/Controllers/FileCompareController.cs:151`

建议：

- 统一 `FileHash` 语义为真实内容哈希。
- 上传接口返回持久化后的同一值，不要再次生成。
- 如果确实需要临时占位标识，单独建字段。

#### 9. Embedding 批量返回数量异常时，没有做严格校验

影响：

- 核心代码注释写的是“不会静默降级到文本相似度”。
- 但如果 embedding 服务短返回，当前实现不会 fail fast。
- 候选侧会保留 `null`，后续退化为空向量；源侧甚至可能把 `null` 直接带入相似度计算。
- 这类问题要么悄悄改写排序，要么在运行时以非预期方式炸掉。

主要证据：

- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:64`
- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:96`
- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:120`
- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelEmbeddingService.cs:87`

建议：

- 对批量 embedding 返回数量做严格一致性校验。
- 任一批次数量不符时直接抛 `AiServiceUnavailableException`。
- 补“短返回”“错位返回”的 fake embedding service 测试。

#### 10. 结构化证据只吃第一条数值约束和第一处型号，复合规格会漏判

影响：

- 数值解析使用的是 `Regex.Match`，只取第一条。
- 型号解析也只取第一个命中。
- 如果规格里同时出现多个尺寸、电压或多个型号，后续冲突不会进入证据模型，评分会被高估。

主要证据：

- `src/AcceptanceSpecSystem.Core/Matching/Services/NumericConstraintParser.cs:18`
- `src/AcceptanceSpecSystem.Core/Matching/Services/MatchEvidenceBuilder.cs:29`
- `src/AcceptanceSpecSystem.Core/Matching/Services/MatchEvidenceBuilder.cs:96`

建议：

- 改成多命中聚合。
- 冲突优先级取最强，而不是“遇到第一条就结束”。
- 补多约束、多型号的测试样例。

#### 11. Prompt 模板页把“预览/恢复默认”错误绑定到了 `update` 权限

影响：

- 前端把“预览”和“恢复默认”都按 `btn:prompt-template:update` 控制。
- 后端实际上有独立动作：`preview` 和 `reset-system`。
- 这会压扁细粒度授权：有预览权限但无更新权限的用户无法使用合法能力；反过来有更新权限但无预览权限的用户会看到按钮，点击后再被后端拒绝。

主要证据：

- `web/src/views/config/prompt-templates/index.vue:99`
- `web/src/views/config/prompt-templates/index.vue:163`
- `src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs:193`
- `src/AcceptanceSpecSystem.Api/Controllers/PromptTemplatesController.cs:254`

建议：

- 前端分别改用 `btn:prompt-template:preview`、`btn:prompt-template:reset-system`。
- 如果产品不需要细粒度，那就回到后端统一简化权限模型，而不是只在前端合并。

#### 12. 旧 `/config/*` 兼容路由实际上会先被父级权限拦住

影响：

- 旧书签重定向路由挂在 `Config` 父路由下。
- 路由守卫会检查整条 `matched` 链。
- 只有 `menu:rbac` 没有 `menu:config` 的用户，即使访问旧地址，也不会先重定向，而是直接被判无权限。

主要证据：

- `web/src/router/modules/config.ts:56`
- `web/src/router/modules/config.ts:66`
- `web/src/router/modules/config.ts:76`
- `web/src/router/modules/rbac.ts:16`
- `web/src/router/index.ts:168`

建议：

- 如果目标是兼容旧书签，把这些跳转放到不带业务父权限的壳路由下。
- 如果不再兼容，就删除旧入口并在变更说明里明确写成 breaking change。

#### 13. 前端 `typecheck` 已失败，但 `build` 仍通过，而且未见前端自动化测试链路

影响：

- 当前仓库已经存在类型错误：`manualConfirmed` 字段不存在。
- 但 `build` 不会阻断该问题。
- `web/package.json` 也没有 `test` 脚本，本地未发现 `web/src` 下的前端测试文件。
- 路由、权限、表单映射这类容易漂移的逻辑缺少自动保护。

主要证据：

- `web/src/views/smart-fill/index.vue:656`
- `web/package.json:9`
- `web/package.json:14`

本地验证：

- `pnpm typecheck` 失败，报 `manualConfirmed` 不存在。
- `pnpm build` 通过。

建议：

- 把 `typecheck` 并入 `build` 或 CI 必经门。
- 增加至少一层前端自动化校验，优先覆盖权限显隐、旧路由跳转、导入/填充主流程。

### P3

#### 14. 当前“归一化”测试没有覆盖真实匹配主路径

影响：

- 现有归一化测试只验证 `MinimalTextPreprocessingPipeline`。
- 但实际匹配主链路主要使用的是 `SemanticKernelMatchingService.NormalizeComparableText`。
- 这类测试对真实排序和证据回归的拦截能力有限。

主要证据：

- `tests/AcceptanceSpecSystem.Core.Tests/MatchingKnowledgeDrivenNormalizationTests.cs:9`
- `src/AcceptanceSpecSystem.Core/TextProcessing/Services/MinimalTextPreprocessingPipeline.cs:9`
- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:634`

建议：

- 增加直接走 `BatchMatchAsync` 的归一化敏感用例。
- 覆盖空白、大小写、全半角括号、停用词、别名和冲突词等真实路径。

#### 15. Word 文档解析/写入存在空引用告警，潜在运行时稳定性风险未消除

影响：

- 整解测试虽然通过，但编译阶段仍出现 `CS8602`。
- 风险点集中在 `doc.MainDocumentPart?.Document.Body` 这类位置。
- 对异常或损坏文档，可能仍有空引用崩溃风险。

主要证据：

- `src/AcceptanceSpecSystem.Core/Documents/Parsers/WordDocumentParser.cs:77`
- `src/AcceptanceSpecSystem.Core/Documents/Parsers/WordDocumentParser.cs:119`
- `src/AcceptanceSpecSystem.Core/Documents/Parsers/WordDocumentParser.cs:144`
- `src/AcceptanceSpecSystem.Core/Documents/Writers/WordDocumentWriter.cs:140`
- `src/AcceptanceSpecSystem.Core/Documents/Writers/WordDocumentWriter.cs:183`
- `src/AcceptanceSpecSystem.Core/Documents/Writers/WordDocumentWriter.cs:208`

建议：

- 把 `MainDocumentPart`、`Document`、`Body` 的判空链做完整。
- 增加损坏文档、空文档、最小文档的异常路径测试。

## 其他观察

以下问题值得关注，但本次没有把它们列为主 finding：

- `vite.config.ts` 通过 `chunkSizeWarningLimit: 4000` 压制了大包告警，当前构建结果里最大 JS chunk 已超过 `1 MB`。
- 前端构建期提示 `baseline-browser-mapping` 数据过旧。
- `web/src/views/welcome/index.vue` 仍保留，但当前路由已经改成重定向，存在清理不彻底的信号。
- API 集成测试主要走 SQLite `EnsureCreated`，Data 单测大量走 InMemory；对 MySQL 方言特有行为和真实迁移链的覆盖仍偏弱。

## 已排除的误报

下面两点在本次复核后不成立，或至少不能按原说法成立：

1. “`FinalScore` 正向权重只加到 `0.85`”
   当前代码里正向权重是 `0.55 + 0.15 + 0.15 + 0.10 + 0.05 = 1.0`。
   证据：`src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs:357`

2. “`FilterEmptySourceRows` 完全未生效”
   核心匹配服务本身不会过滤，但工作流在抽取源数据时已经按该开关过滤空项目/空规格行。
   证据：`src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs:2603`
   说明：这个配置仍然没有下沉到核心服务层，但不能再说它“完全未生效”。

## 建议的修复顺序

建议按下面顺序处理：

1. 先修安全与上线阻断项：文件越权、内置角色自锁、生产配置冲突、硬编码密钥。
2. 再修权限模型漂移：`common` 角色按钮权限、`matching-fill` 命名漂移、Prompt 模板权限映射。
3. 然后修核心匹配边界：决策阈值契约、embedding 短返回、复合规格证据抽取。
4. 最后补质量门：前端 `typecheck`/测试链路、MySQL 迁移覆盖、文档异常路径稳定性。

## 结尾判断

当前项目具备继续演进的基础，分层结构和测试规模都不算差，但还没有达到“可以放心上线或放心扩功能”的状态。

如果只做最少量收口，我认为至少应先完成下面 4 件事：

- 修复文件资源越权问题
- 修复生产配置和硬编码密钥问题
- 统一 `matching-fill` 权限命名并补齐 `common` 角色按钮权限
- 把前端 `typecheck` 纳入必经质量门
