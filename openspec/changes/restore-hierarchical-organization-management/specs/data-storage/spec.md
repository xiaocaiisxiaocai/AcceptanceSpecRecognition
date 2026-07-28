## MODIFIED Requirements

### Requirement: 存储层级组织与 RBAC 关系
系统 MUST 在数据库中存储单公司根节点及其事业部、部门和课别后代，并维护角色、权限、用户单组织归属和角色组织数据范围关系。

#### Scenario: 根组织初始化
- **WHEN** 系统初始化公司组织数据
- **THEN** 每个公司只存在一个类型为公司的根组织节点

#### Scenario: 创建层级组织
- **WHEN** 系统在有效父节点下创建合法下级组织
- **THEN** 节点保存同公司的 `CompanyId`、父节点 `ParentId`、正确 `Depth` 和包含自身 ID 的规范 `Path`

#### Scenario: 用户组织关系保持唯一
- **WHEN** 系统读取或写入用户组织归属
- **THEN** 用户只关联当前公司内一个有效组织节点
- **AND** 数据库继续以 `AuthUserOrgUnits.UserId` 唯一约束阻止多组织归属

#### Scenario: 角色数据范围使用组织层级
- **WHEN** 系统保存角色的组织节点、组织子树或自定义节点集合范围
- **THEN** `AuthRoleDataScopeNodes` 只引用同公司有效组织节点
- **AND** 子树范围通过规范组织路径解析全部后代

#### Scenario: 删除组织保持引用完整
- **WHEN** 组织节点存在下级、用户归属、角色范围或业务数据引用
- **THEN** 系统拒绝删除且不级联移除任何关联数据

#### Scenario: 恢复历史组织可见性
- **WHEN** 数据库已经存在合法历史下级组织
- **THEN** 系统直接按原 ID、父级和路径返回这些节点
- **AND** 不自动重建、重排或删除历史组织数据

## RENAMED Requirements

- FROM: `### Requirement: 存储单公司根组织与 RBAC 关系`
- TO: `### Requirement: 存储层级组织与 RBAC 关系`
