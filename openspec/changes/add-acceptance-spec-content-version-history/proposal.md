# Change: 增加验收规格完整内容版本历史

## Why

验收规格目前已有单调递增的 `ReferenceVersion`，但它只用于区分引用次数和引用时间所属的内容版本。系统不会保存每个版本当时的项目、规格内容、验收标准和备注，因此用户虽然能看到 `V1`、`V2`，仍无法查看版本正文、比较变化、确认修改来源或恢复旧版本。

这会直接影响后续验规清理：仅凭当前版本引用次数和最近引用时间无法判断历史内容是否仍有业务价值，也无法在误改或误清理前提供可恢复依据。完整版本管理必须保持引用历史语义不变，并明确承认上线前未保存的旧版本正文无法重建。

## What Changes

- 新增不可变的验收规格内容版本快照，保存每个可追溯版本的完整业务正文、变更时间、变更来源、可选修改原因、操作者快照和恢复来源。
- 复用现有 `ReferenceVersion` 作为统一内容版本号，不再引入第二套版本编号。
- 所有正式新建入口在同一提交中生成初始版本快照；所有实质内容修改入口在同一事务内递增版本、保存新快照并维持现有引用次数清零规则。
- 为现有规格迁移当前正文基线；若当前版本大于 1，只保存当前版本并明确标识更早正文不可追溯，不伪造缺失版本。
- 新增受数据范围保护的版本列表、版本详情、版本差异和恢复 API。
- 恢复旧版本时创建一个新的当前版本，并记录 `RestoredFromVersion`；不得覆盖、删除或改写已有历史。
- 将当前内容版本配置为数据库并发令牌；手工更新和恢复额外使用期望当前版本校验，过期页面或并发写入返回冲突，防止静默覆盖和重复版本。
- 新增独立的版本恢复 API/按钮权限；查看版本历史沿用验收规格读取权限和数据范围，所有恢复操作进入现有审计链。
- 前端将版本标签作为版本历史入口，提供紧凑时间线、字段级差异和恢复确认；引用历史继续作为独立能力展示引用次数与时间。

## Impact

- Prerequisite change: `add-acceptance-spec-reference-history`
- Affected specs: `data-storage`, `api`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Data/Entities/`
  - `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/`
  - `src/AcceptanceSpecSystem.Data/Migrations/`
  - `src/AcceptanceSpecSystem.Application/Services/AcceptanceSpec*.cs`
  - `src/AcceptanceSpecSystem.Application/Services/DocumentImportAppService*.cs`
  - `src/AcceptanceSpecSystem.Application/Services/SmartFillSpecBackfillAppService.cs`
  - `src/AcceptanceSpecSystem.Application/Contracts/AcceptanceSpecDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
  - `web/src/api/spec.ts`
  - `web/src/views/base-data/specs/components/SpecTable.vue`
  - new version-history frontend components and focused tests
- Database: 新增 `AcceptanceSpecContentVersions`、唯一索引、分页索引和现有规格当前正文基线数据。
- Compatibility: 现有列表、详情和引用历史响应保持兼容；更新请求新增可选期望版本字段，正式 Web 客户端必须发送。新增版本接口为增量能力。
- Migration boundary: 上线前已经丢失的旧版本正文不会被推算或重建，只能从迁移时当前正文开始形成完整版本链。
