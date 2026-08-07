# Change: 增加验收规格引用时间历史

## Why

验收规格列表目前只能看到当前内容版本的累计引用次数，无法回答这些引用分别在什么时候发生。用户需要从引用次数进入时间明细，确认规格最近是否仍在被智能填充采用，并保留内容修改前后的历史轨迹。

现有 `ReferenceCount` 按最终采用行数累计，且项目、规格、验收或备注发生实质变化时会清零。因此时间历史必须同时表达“内容版本”和“单次执行采用行数”，否则内容变更后会出现累计值与明细不一致。

## What Changes

- 为验收规格增加单调递增的引用内容版本号，并持久化逐次引用历史。
- 每次智能填充最终成功提交时，按每个最终采用行写入一条引用历史，记录成功提交时间；历史写入与引用次数累计使用同一事务。
- 同一执行中同一规格被多行采用时分别记录并分配稳定的任务内序号；这些引用共享同一个成功提交时刻，但仍能作为第 1 次、第 2 次等独立引用查询。
- 验收规格内容发生实质变化时递增引用内容版本并将当前版本引用次数清零，但保留旧版本历史。
- 迁移时不伪造已有引用次数的时间；已有非零次数写入“时间不可追溯”的基线历史。
- 新增分页引用历史 API，默认查询当前内容版本，并允许包含历史版本；沿用验收规格读取权限和数据范围。
- 验收规格列表返回所有内容版本中最近一次可追溯的成功引用时间，便于直接判断最近使用情况；仅有迁移前不可追溯次数时保持为空。
- 验收规格列表和详情中的引用次数可打开时间明细，支持最早/最新排序并展示引用序号、内容版本和引用时间，同时明确标识无法追溯时间的迁移前次数。

## Impact

- Prerequisite change: `add-acceptance-spec-reference-count`
- Affected specs: `matching-engine`, `data-storage`, `api`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/MatchingWorkflowSupportService.*.cs`
  - `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecAppService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpecReferenceCountPolicy.cs`
  - `src/AcceptanceSpecSystem.Application/Contracts/AcceptanceSpecDtos.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/AcceptanceSpec.cs`
  - `src/AcceptanceSpecSystem.Data/Entities/`
  - `src/AcceptanceSpecSystem.Data/Repositories/`
  - `src/AcceptanceSpecSystem.Data/Migrations/`
  - `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
  - `web/src/api/spec.ts`
  - `web/src/views/base-data/specs/components/SpecTable.vue`
- Database: 为 `AcceptanceSpecs` 增加引用内容版本字段，并新增引用历史表、外键和查询/幂等索引。
- Compatibility: 现有列表和详情响应保持兼容；引用历史为新增只读接口。已有引用次数保留，但其具体时间明确标记为不可追溯。
