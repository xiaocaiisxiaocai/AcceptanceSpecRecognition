# Change: 强制系统用户采用单角色模型

## Why
当前系统从数据库到 API 再到前端都按“一个用户可挂多个角色”设计，但实际业务规则已经明确为“一个用户只允许一个角色”。继续保留多角色模型会让角色分配、权限范围调整和用户认知长期错位。

## What Changes
- 将系统用户创建、编辑、查询及登录返回统一改为单角色字段 `roleCode`，不再暴露 `roles` 数组。
- 保留 `AuthUserRoles` 关系表，但通过迁移清洗历史多角色数据，并为 `UserId` 增加唯一约束。
- 历史多角色数据按“`admin` 优先，否则保留第一条角色关系”迁移；无角色用户补齐到 `common`。
- 用户管理页面角色选择由多选改为单选，列表展示改为单角色展示。
- JWT 与前端登录态只保留单个角色字段，权限数组保持不变。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/*`（用户管理、登录/刷新、认证上下文）
  - `src/AcceptanceSpecSystem.Data/*`（用户角色迁移、唯一约束、仓储查询）
  - `web/src/api/*`、`web/src/store/*`、`web/src/utils/auth.ts`、`web/src/views/config/system-users/*`
