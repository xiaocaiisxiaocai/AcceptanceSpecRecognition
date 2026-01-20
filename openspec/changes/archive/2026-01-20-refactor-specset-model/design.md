## Context
系统需要表达的业务语义是：
- Customer 与 Process 是两份独立的基础数据
- **(CustomerId, ProcessId)** 的组合定义“一整份验规”（验规集合）

当前实现为：
- `Process.CustomerId` 外键（Customer → Process 1:N）
- `AcceptanceSpec.ProcessId`（验规挂在 Process 之下）

这与目标语义不一致，且会影响导入、匹配、列表筛选与历史数据迁移。

## Goals / Non-Goals

### Goals
- 移除 Customer→Process 从属关系
- 验规数据以 **(CustomerId, ProcessId)** 组合为边界组织、查询与导入
- 前端选择器改为“客户选择 + 制程选择”（不再是级联）
- 提供可执行的数据迁移策略（保证已有数据可用）

### Non-Goals
- 不在本变更内实现 “Undo 撤销逻辑” 或新的 AI 能力
- 不在本变更内做权限/登录体系调整

## Decisions

### Decision: 不引入新表 `SpecSet`，直接在 `AcceptanceSpec` 增加 `CustomerId`
**理由**：最小化变更面与迁移复杂度。`AcceptanceSpec` 已是验规条目载体，通过 `(CustomerId, ProcessId)` 组合即可表达“验规集合”。

**备选方案**：增加 `SpecSet`（CustomerId, ProcessId 唯一）并让 `AcceptanceSpec.SpecSetId` 外键指向它。优点是语义更强；缺点是多一张表 + 更多 API/前端改动。

### Decision: Process 名称允许重名（不强制全局唯一）
**理由**：旧系统中 Process 名称通常依赖 Customer 语境；拆开后仍可能重名。前端用 ID 选择即可避免歧义。

（如需强制全局唯一，可在后续变更中加入唯一索引。）

## Risks / Trade-offs
- **破坏性变更**：现有 API/前端对 Customer→Process 的假设需要整体调整
- **迁移策略**：历史数据中的 Process 需要保留，但其 CustomerId 外键移除后需要给每条 AcceptanceSpec 填充 CustomerId
- **查询性能**：`AcceptanceSpec` 增加 CustomerId 后，需建立复合索引 `(CustomerId, ProcessId)` 以保持查询性能

## Migration Plan
- 新增迁移：为 `AcceptanceSpecs` 增加 `CustomerId` 外键；移除 `Processes.CustomerId`
- 数据迁移：
  - 对每个 `AcceptanceSpec`：通过其旧 `ProcessId -> Process.CustomerId` 填充 `AcceptanceSpec.CustomerId`
- 索引调整：
  - 新增 `AcceptanceSpecs(CustomerId, ProcessId)` 索引
  - 将 `Processes` 的唯一约束从 `(CustomerId, Name)` 改为 `(Name)`（如不强制唯一，则不加唯一索引，仅保留普通索引）
- API/前端：
  - 由“按客户筛制程”改为“分别选择客户/制程，再按组合筛验规”

## Open Questions
- 是否需要引入显式的“验规集合”实体（`SpecSet`）以便后续扩展（统计/版本/权限）？
- Process 是否需要全局唯一名称（UI 体验 vs 业务现实）？

