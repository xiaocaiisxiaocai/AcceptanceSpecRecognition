## Context
- 需要在单公司边界内支持细粒度授权、跨部门协作与临时授权过期回收。
- 要求一次性切换，不保留旧 `RolesJson/PermissionsJson` 运行时兼容路径。

## Goals / Non-Goals
- Goals:
  - 页面级、按钮级、API 级权限统一编码。
  - 后端作为最终授权裁决方，未命中权限默认拒绝。
  - 支持组织树与用户多组织归属，组织层级可空（跳级挂载）。
  - 支持角色/组织授权有效期（临时协作）。
- Non-Goals:
  - 本次不引入 Redis，不做分布式鉴权缓存。
  - 本次不引入额外身份源（如 AD/LDAP）。

## Decisions
- Decision: 采用 `api|btn|page:resource:action` 统一权限码。
- Decision: API 权限通过中间件按控制器动作自动解析，避免逐个动作手工维护策略。
- Decision: 用户权限由「有效期内角色」聚合获得，数据范围规则独立建模。
- Decision: 初始内置角色为 `admin/common`，并通过种子逻辑自动补齐基础数据。

## Risks / Trade-offs
- 风险: 自动权限码解析与固定白名单可能出现命名偏差。
  - Mitigation: 通过集成测试覆盖关键接口与 `get-async-routes`。
- 风险: 一次性硬切迁移涉及旧 JSON 字段到关系表的转换。
  - Mitigation: 在迁移中先导入角色关系，再删除旧字段。

## Migration Plan
1. 执行迁移，创建新表与新列。
2. 插入默认公司与组织根节点。
3. 根据旧 `RolesJson` 映射 `AuthUserRoles`，补齐 `AuthUserOrgUnits`。
4. 删除旧 `RolesJson/PermissionsJson` 字段。
5. 启动后执行种子初始化补全权限字典与内置角色权限关系。

## Open Questions
- 下一阶段是否需要提供“角色管理/组织管理”可视化界面（当前主要完成权限底座与用户接口改造）。
