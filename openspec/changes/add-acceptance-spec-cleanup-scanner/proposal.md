# Change: 增加验收规格扫毒清理

## Why

验收规格持续导入和迭代后，会积累从未被智能填充采用、或多年未再采用的内容。现有列表虽然能显示当前版本引用次数和最近引用时间，但无法区分“真正从未使用”“旧版本曾使用”“迁移前使用过但时间不可追溯”等情况；现有单条和批量删除又是立即物理删除，不适合承担批量清理。

系统需要提供一个类似杀毒软件的受控扫描流程：用真实引用历史和内容版本判断候选项，展示扫描进度，让用户逐项或批量决定保留、忽略或清理，并通过可恢复隔离区降低误删风险。

## What Changes

- 在验收规格页面增加“扫毒清理”入口，以紧凑工作台展示真实扫描进度、分类统计和分页结果。
- 扫描按可配置的“新数据保护期”和“长期未引用阈值”判断候选项，同时使用当前版本引用数、全版本引用历史、最近可追溯引用时间、内容更新时间和迁移前不可追溯次数。
- 将结果分为“建议清理”“人工确认”“正常”；迁移前存在不可追溯引用、当前版本尚未使用但旧版本近期使用等不确定情况不得自动归入建议清理。
- 用户可逐项或批量选择“保留”“忽略后续扫描”或“移入隔离区”；任何结果默认不预选，系统不得自动清理。
- 新增可恢复隔离状态。隔离规格立即从常规列表、智能填充候选、语义搜索和缓存预热等业务消费者中排除，但保留内容版本与引用历史。
- 隔离期满后才允许永久删除；永久删除必须二次确认、重新校验权限与版本，并保留不含正文的删除审计记录。
- 扫描结果保存判定快照。执行隔离或永久删除时使用内容版本和状态做并发校验，变化过的规格退回重新扫描，不按旧结论处理。
- 新增独立的扫描、隔离、恢复和永久删除权限，并继续应用公司与组织数据范围。

## Impact

- Affected specs: `api`, `data-storage`, `user-interface`, `architecture`
- Affected code:
  - `src/AcceptanceSpecSystem.Application/Services/`
  - `src/AcceptanceSpecSystem.Application/Contracts/`
  - `src/AcceptanceSpecSystem.Data/Entities/`
  - `src/AcceptanceSpecSystem.Data/Repositories/`
  - `src/AcceptanceSpecSystem.Data/Migrations/`
  - `src/AcceptanceSpecSystem.Api/Controllers/`
  - `shared/navigation/navigation-manifest.json`
  - `web/src/api/`
  - `web/src/router/modules/base-data.ts`
  - `web/src/views/base-data/specs/`
- Database: 增加扫描任务、扫描结果、隔离/忽略状态和永久删除留痕所需字段或表及索引。
- Compatibility: 现有验收规格读取接口默认继续只返回可用规格；现有直接删除接口不作为扫毒清理入口，具体兼容收敛在实施时以自动化测试锁定。
