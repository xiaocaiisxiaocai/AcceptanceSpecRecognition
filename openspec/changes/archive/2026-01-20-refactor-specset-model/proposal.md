# Change: refactor-specset-model

## Why
当前系统数据模型按 **Customer → Process → AcceptanceSpec** 组织，但目标业务语义是：
**Customer 与 Process 不建立从属关系**，并且 **(Customer + Process) 这对组合代表“一整份验规”（验规集合）**。
这属于破坏性领域模型调整，需要明确迁移与接口影响。

## What Changes
- **BREAKING**：移除 `Process.CustomerId` 与 `Customer.Processes` 的从属关系
- **BREAKING**：将“验规归属”从 `AcceptanceSpec.ProcessId` 改为 **(CustomerId, ProcessId)** 组合（等价于验规集合键）
- **BREAKING**：前端导入/列表/匹配筛选从“按客户筛制程”改为“分别选择客户与制程，再按组合筛验规集合”
- **Data migration**：提供从旧数据结构迁移到新结构的方案（包括同名制程的处理策略）

## Impact
- Affected specs:
  - `data-storage`
  - `user-interface`
- Affected code (expected):
  - `src/AcceptanceSpecSystem.Data/Entities/*`
  - `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/*`
  - `src/AcceptanceSpecSystem.Api/Controllers/*`
  - `web/src/views/*`
  - `web/src/api/*`
  - EF Core migrations

