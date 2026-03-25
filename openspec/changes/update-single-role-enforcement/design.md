## Context
现有 RBAC 底层关系模型允许一个用户关联多条 `AuthUserRole` 记录，用户管理 API 使用 `roles: string[]`，登录和刷新令牌也会返回角色数组并写入多个 `ClaimTypes.Role`。这与当前业务规则“一个用户只能有一个角色”不一致，也会让“调整权限范围只能切换角色版本”这类业务规则难以稳定落地。

## Goals / Non-Goals
- Goals:
  - 从数据库、API、认证返回到前端表单统一为单角色模型
  - 为历史多角色和无角色用户提供确定性的迁移规则
  - 保留现有 RBAC 表结构和权限码体系，避免扩大改动范围
- Non-Goals:
  - 不在本次变更中开放内置角色的权限范围编辑
  - 不调整权限码、数据范围计算规则和组织模型
  - 不重构角色、权限、数据范围的底层关系表设计

## Decisions
- Decision: 保留 `AuthUserRoles` 表，不新增 `SystemUsers.RoleId`
  - Why: 现有查询、种子、权限聚合和迁移都已经围绕关系表展开；本次只需要收口为单角色，不值得为此推倒模型。
- Decision: 通过数据迁移 + 唯一约束强制单角色
  - Why: 只改 API 无法防止后台或历史数据再次写入多角色；必须由数据库兜底。
- Decision: 历史多角色按固定规则裁剪
  - Rule:
    1. 若用户包含 `admin`，保留 `admin`
    2. 否则按 `CreatedAt` 升序、`Id` 升序保留第一条
  - Why: 规则可重复执行，且符合管理员优先保留的业务要求。
- Decision: 历史无角色用户迁移到 `common`
  - Why: 系统本身已把 `common` 视为普通用户兜底角色，迁移到 `common` 风险最低。
- Decision: API、认证返回与前端缓存统一使用 `roleCode`
  - Why: 用户明确要求“不再暴露数组”；仅在用户管理接口改单值而继续在登录返回中保留数组会造成双重口径。
- Decision: 权限字段继续保持数组
  - Why: 权限校验本来就是多项集合，单角色并不等于单权限。

## Risks / Trade-offs
- 风险: 前端登录态、SSO 和用户缓存当前依赖 `roles` 数组
  - Mitigation: 同步调整登录 API、用户 store 和本地缓存结构，避免页面刷新后状态不一致。
- 风险: 旧数据裁剪后，少数历史账号会失去原先叠加出的附加权限
  - Mitigation: 这是业务规则本身要求的纠偏；迁移后由管理员通过切换到正确的单角色版本处理。
- 风险: 最后一个启用中的 `admin` 用户保护逻辑目前按角色列表判断
  - Mitigation: 改为直接基于单个 `roleCode` 判断，并补测试锁定行为。

## Migration Plan
1. 迁移前读取 `AuthUserRoles`，按“`admin` 优先，否则第一条”规则清洗每个用户的角色关系。
2. 为无角色用户补齐 `common` 角色关系。
3. 在 `AuthUserRoles.UserId` 上增加唯一约束，形成数据库硬边界。
4. 后端 DTO、控制器、认证上下文与 JWT 切到 `roleCode`。
5. 前端用户管理、登录缓存和类型定义切到 `roleCode`。

## Open Questions
- 无。
