## ADDED Requirements
### Requirement: 存储组织层级与 RBAC 关系
系统 MUST 在数据库中提供公司、组织节点、角色、权限、角色权限、用户角色、用户组织、角色数据范围等关系表。

#### Scenario: 组织层级可跳级
- **WHEN** 某业务仅配置到事业部或部门层级（未配置下级课别）
- **THEN** 系统仍可保存组织节点并正常完成用户组织归属

### Requirement: 旧用户角色数据迁移
系统 MUST 在迁移阶段将旧用户 JSON 角色信息转换到新关系表，并在迁移后移除旧列。

#### Scenario: 迁移已有管理员用户
- **WHEN** 数据库中存在 `RolesJson` 包含 `admin` 的历史账号
- **THEN** 迁移后该账号在 `AuthUserRoles` 中关联 `admin` 角色，并保留可登录能力

#### Scenario: 迁移普通用户
- **WHEN** 历史用户不包含 `admin` 角色
- **THEN** 迁移后该用户默认关联 `common` 角色并获得默认组织归属
