## ADDED Requirements
### Requirement: 角色管理 API
系统 SHALL 提供角色管理接口，并对内置角色执行“可编辑、不可删除”的规则。

#### Scenario: 更新内置角色
- **WHEN** 前端发送 PUT 请求到 `/api/auth-roles/{id}` 更新内置角色
- **THEN** 系统保存角色名称、描述、状态、权限配置与数据范围修改

#### Scenario: 删除内置角色
- **WHEN** 前端发送 DELETE 请求到 `/api/auth-roles/{id}` 删除内置角色
- **THEN** 系统返回删除受限错误，且不删除该角色
