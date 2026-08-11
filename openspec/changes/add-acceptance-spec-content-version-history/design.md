## Context

`AcceptanceSpecs.ReferenceVersion` 当前表示验收规格内容代次。项目、规格内容、验收标准或备注发生实质变化时，统一策略会递增该值并清零当前版本的 `ReferenceCount`；`AcceptanceSpecReferenceEvents` 使用同一版本号保留引用时间历史。

当前缺口是没有内容快照。修改发生后，数据库只剩最新正文，旧引用事件虽然仍标记旧版本号，却无法回答旧版本当时是什么内容。现有修改入口包括手工编辑、文档导入覆盖、智能填充回填和部门内备注批量替换；新建入口包括手工新增、批量导入、文档导入和智能填充回填创建。

本变更必须把内容快照与现有版本号、引用统计、权限、事务和缓存失效统一起来，不能形成两套相互漂移的版本机制。

## Goals / Non-Goals

- Goals:
  - 为上线后的每个验收规格内容版本保存不可变正文快照。
  - 所有正式创建和修改入口遵循同一版本规则。
  - 允许用户查看时间线、字段差异并安全恢复旧版本。
  - 恢复操作创建新版本，保留完整审计和引用历史。
  - 通过期望版本校验阻止过期编辑覆盖并发变更。
  - 保持现有引用次数和引用时间按 `ReferenceVersion` 归属的语义。
  - 对迁移前正文缺口提供明确、不可伪造的边界说明。
- Non-Goals:
  - 不推算或重建系统未曾保存的历史正文。
  - 不允许人工编辑、删除或重编号历史快照。
  - 不把验收规格删除改为软删除；删除/隔离由后续验规清理能力单独设计。
  - 不在本变更中提供分支、草稿、审批流或多人协同编辑。
  - 不改变智能填充匹配算法、引用次数单位或引用事件写入规则。

## Decisions

### 1. 复用 ReferenceVersion 作为唯一内容版本号

保留 `AcceptanceSpecs.ReferenceVersion` 和 `AcceptanceSpecReferenceEvents.ReferenceVersion`。新增内容快照的 `Version` 必须与它们使用同一单调递增序列。

不新增独立 `ContentVersion` 字段，避免列表版本、引用版本和快照版本出现映射关系。代码和界面文案逐步将其解释为“内容版本”，但保持现有 API 字段兼容。

### 2. 使用不可变内容快照表

新增 `AcceptanceSpecContentVersions`：

- `Id BIGINT`：主键。
- `AcceptanceSpecId INT`：所属规格，规格删除时级联删除快照。
- `Version BIGINT`：内容版本号。
- `Project VARCHAR(500)`。
- `Specification VARCHAR(4000)`。
- `Acceptance VARCHAR(4000) NULL`。
- `Remark VARCHAR(2000) NULL`。
- `ChangedAtUtc DATETIME(6)`：该版本成功提交时刻。
- `ChangedByUserId INT NULL`：可追溯操作者；系统迁移允许为空，用户删除时置空。
- `ChangedByNameSnapshot VARCHAR(100) NULL`：提交时显示名快照，避免用户改名后历史含义漂移。
- `ChangeSource VARCHAR(40)`：`create`、`manual-update`、`document-import`、`smart-fill-backfill`、`remark-replace`、`restore` 或 `migration-baseline`。
- `ChangeReason VARCHAR(500) NULL`：手工编辑或恢复时可选填写的修改原因；自动入口允许为空。
- `RestoredFromVersion BIGINT NULL`：仅恢复产生的新版本填写来源版本。
- `IsMigrationBaseline BOOLEAN`：是否为迁移生成的当前正文基线。

建立 `(AcceptanceSpecId, Version)` 唯一索引和 `(AcceptanceSpecId, Version DESC, Id DESC)` 查询索引。快照创建后只读，应用层不提供更新或删除接口。

### 3. 当前实体与新版本快照同一事务提交

用应用层的统一版本协调器替代“只递增计数”的静态策略调用。协调器接收：

- 当前规格；
- 归一化后的目标正文；
- 变更来源；
- 操作者 ID 和显示名；
- 可选修改原因；
- 可选恢复来源版本；
- 提交时间。

处理规则：

1. 对四个正文字段执行现有归一化比较。
2. 内容等价时不递增版本、不清零引用次数、不新增快照。
3. 内容实质变化时递增 `ReferenceVersion`、清零 `ReferenceCount`、更新正文和 `UpdatedAt`。
4. 为递增后的版本添加唯一快照。
5. 实体更新、快照、相关缓存删除和调用方其他业务写入在同一工作单元/事务中提交。

新建规格取得数据库主键后，在同一业务提交中保存当前 `ReferenceVersion` 的初始快照。所有生产创建入口必须复用统一快照工厂；测试直接造数不强制自动生成，但涉及版本 API 的夹具必须显式建立一致快照。

`ReferenceVersion` 同时配置为 EF Core 并发令牌。所有跟踪实体的内容更新必须在 SQL `WHERE` 中带原始版本；两个入口并发修改同一规格时只能有一个提交成功，另一方转换为业务 409/冲突结果并整体回滚，不盲目重试可能非幂等的导入、批量替换或回填操作。唯一快照索引作为数据库最后一道重复版本保护。

### 4. 迁移只建立当前正文基线

迁移对每条现有规格插入一条快照：

- `Version = AcceptanceSpecs.ReferenceVersion`
- 正文取迁移时当前值
- `ChangedAtUtc = COALESCE(UpdatedAt, ImportedAt)`
- `ChangedByUserId = NULL`
- `ChangedByNameSnapshot = NULL`
- `ChangeSource = migration-baseline`
- `IsMigrationBaseline = true`

若当前版本为 V1，该快照可作为现有当前正文基线，但仍不声称知道精确操作者。若当前版本大于 V1，V1 到前一版本的正文保持缺失；API 返回 `earliestAvailableVersion` 和 `hasUnavailableEarlierVersions`，前端明确显示“版本记录功能上线前的正文不可追溯”。

迁移不得根据引用事件、更新时间或当前正文复制伪造缺失版本。

### 5. 恢复旧版本创建新版本

`POST /api/specs/{id}/content-versions/{version}/restore` 接收：

- `expectedCurrentVersion`：用户打开恢复确认时看到的当前版本；
- 可选 `reason`：最多 500 字，进入审计安全详情和版本来源说明，不写入规格正文。

恢复流程：

1. 校验读取和恢复权限、数据范围及目标快照存在。
2. 校验当前 `ReferenceVersion == expectedCurrentVersion`，否则返回 409。
3. 若目标快照正文与当前正文等价，返回 422，不创建空版本。
4. 将目标正文作为新内容应用，版本号递增 1，引用次数清零。
5. 新快照标记 `ChangeSource=restore`、`RestoredFromVersion=version`。
6. 可选恢复原因写入新快照的 `ChangeReason`，并以脱敏安全详情进入审计。
7. 清理该规格 Embedding 缓存并写入现有审计链。
8. 提交后返回新的规格详情和新版本号。

恢复不是把当前指针退回旧编号，也不删除恢复之后的版本，因此时间线保持单调且可审计。

### 6. 更新请求增加乐观并发保护

`UpdateSpecRequest` 增加可空 `ExpectedReferenceVersion` 和最长 500 字的可选 `ChangeReason` 以保持旧调用方兼容。正式 Web 编辑表单必须发送打开表单时的版本号，并允许用户填写修改原因；提供期望版本时，服务端在应用修改前校验当前版本，版本不一致返回 409 并提示刷新。

文档导入、智能填充回填和备注批量替换继续使用各自现有事务快照/确认令牌处理并发，同时通过唯一版本快照约束防止重复版本。恢复接口的期望版本为必填。

### 7. 版本查询和差异由服务端提供稳定契约

新增接口：

- `GET /api/specs/{id}/content-versions?page=1&pageSize=20&sort=newest`
- `GET /api/specs/{id}/content-versions/{version}`
- `GET /api/specs/{id}/content-version-diff?fromVersion=1&toVersion=2`
- `POST /api/specs/{id}/content-versions/{version}/restore`

列表只返回摘要和变更字段集合，不重复传输长正文。详情返回单个版本完整正文。差异接口返回四个字段的 before/after、是否变化和文本值，不返回服务端生成 HTML；前端负责安全渲染文本，避免把历史内容作为 HTML 注入。

分页限制 1 到 100，排序仅允许 `newest`/`oldest`。所有接口先通过 `SpecAccessContext` 校验规格读取范围；不存在与无权访问遵循项目既有错误语义。

### 8. 权限、审计和删除语义

版本列表、详情和差异沿用 `api:spec:read`。恢复新增 `api:spec:restore-version` 和 `btn:spec:restore-version`，默认只授予管理员，不自动加入普通角色。

恢复控制器使用 `[AuditOperation("restore-version", "spec")]`，审计安全详情仅记录规格 ID、来源版本、新版本和可选原因，不记录完整正文。

规格物理删除继续级联删除内容版本和引用历史，并沿用现有删除权限及二次确认。这一风险将在后续“验规清理/隔离”提案中通过隔离期解决，本变更不偷偷改变删除生命周期。

### 9. 前端采用紧凑的运维型版本工作台

列表中的 `Vn` 标签改为可点击的版本历史入口，不新增营销式卡片或大面积装饰。右侧抽屉包含：

- 顶部：当前版本、最早可用版本、历史完整性提示。
- 左侧或上部：按时间排列的紧凑版本列表，展示版本、来源、操作者和时间。
- 主区：选择版本后的四字段快照；选择两个版本后显示字段级差异。
- 恢复按钮：仅有权限且选择非当前版本时显示，打开二次确认并明确“将创建 Vn+1”。

桌面宽度使用不超过视口的宽抽屉；窄屏切换为上下布局。长文本必须换行，不与版本列表、操作按钮或抽屉底部重叠。引用历史抽屉保持独立入口，避免把“正文变化”和“被引用时间”混为一谈。

## Risks / Trade-offs

- 快照会增加数据库容量；正文上限明确且只在实质变化时写入，分页和索引限制查询成本。上线后根据真实增长量再决定归档，不提前引入压缩或外部存储。
- 所有写入入口必须统一接入，否则会出现版本号已变但快照缺失；通过入口枚举测试、数据库唯一约束和不变量测试阻断遗漏。
- `ChangedByNameSnapshot` 是有意的审计去规范化；它只保存内部显示名，不保存凭据或额外个人资料。
- 迁移前旧正文永久不可恢复；界面持续明确这一边界，比伪造完整历史更可靠。
- 直接物理删除仍会删除全部历史；该生命周期问题属于后续清理/隔离能力，不在本变更中扩大。

## Migration Plan

1. 创建 `AcceptanceSpecContentVersions`、外键和索引。
2. 以当前正文为每条现有规格插入迁移基线，并核对每条规格恰有当前版本快照。
3. 部署统一版本协调器和所有创建/修改入口接入。
4. 部署版本查询、差异、恢复 API 和权限种子。
5. 部署前端版本工作台和乐观并发更新。
6. 验证迁移历史、快照不变量、入口一致性、权限、并发冲突、恢复、缓存失效和浏览器交互。
7. 回滚会删除内容快照表；执行前必须备份并明确接受上线后版本正文历史丢失。

## Open Questions

- 无。默认采用“恢复旧版本时创建新版本”的审计友好语义；不提供覆盖历史或回退版本号。
