# Change: 恢复层级组织管理

## Why

当前系统的数据模型与数据范围服务仍保留公司、事业部、部门、课别及组织子树能力，但正式 API 和界面被收敛为仅维护公司根节点，导致管理员无法维护真实组织结构，也无法将用户和角色范围配置到实际部门。

## What Changes

- 恢复当前公司下组织树与平铺列表的完整查询结果。
- 恢复下级组织节点的新增、编辑、启停和安全删除接口，并保留唯一公司根节点。
- 允许组织类型按公司、事业部、部门、课别向下跳级，但禁止反向或同级挂载，课别禁止拥有子节点。
- 保持每个用户只能归属一个组织节点，但允许归属任意有效层级节点。
- 恢复角色按组织节点、组织子树及自定义节点集合配置数据范围。
- 恢复组织新增、删除对应的 API 与按钮权限种子，并记录组织写操作审计。
- 组织删除采用引用保护，不级联删除用户、角色范围或业务数据。
- 不引入新的组织表，不清空或重建历史组织数据。

## Impact

- Affected specs: `api`, `user-interface`, `data-storage`
- Affected backend: `OrgUnitsController`, `OrgUnitAppService`, `SystemUserAppService`, `AuthRoleAppService`, 权限种子与相关测试
- Affected frontend: 组织管理页、角色数据范围选择、系统用户组织选择及相关契约测试
- Database: 复用现有 `OrgUnits`、`AuthUserOrgUnits`、`AuthRoleDataScopeNodes`；继续保留 `AuthUserOrgUnits.UserId` 唯一约束
- Compatibility: 历史下级组织重新可见；旧的单组织用户请求字段保持不变
