# Change: 增加组织节点安全移动

## Why

管理员新增事业部后，当前只能重新创建部门，无法把已有部门及其下级整体迁移到事业部下。手工重建会破坏用户归属、角色数据范围和业务数据的稳定引用，也无法保证组织路径一次性更新正确。

## What Changes

- 增加组织节点移动接口，允许管理员为非公司根节点选择当前公司内新的有效上级组织。
- 移动时在同一事务内更新节点的 `ParentId`，并重写该节点及全部后代的 `Path`、`Depth` 和更新时间。
- 保持组织节点 ID、用户组织归属、角色范围节点引用及业务数据 `OwnerOrgUnitId` 不变。
- 拒绝跨公司移动、移动公司根节点、移动到自身或后代、移动到停用节点及违反组织类型层级的目标。
- 为移动操作增加独立 API、按钮权限和审计记录。
- 在组织管理页面增加明确的“移动”操作和目标上级选择弹窗，移动前提示子树范围变化影响。
- 不增加数据库表或迁移，不提供拖拽移动，不修改组织类型。

## Impact

- Affected specs: `api`, `data-storage`, `user-interface`
- Affected backend: `OrgUnitsController`, `OrgUnitAppService`, `OrgUnitDtos`, 权限种子与组织集成测试
- Affected frontend: `web/src/api/org-unit.ts`, 组织管理页面及前端契约测试
- Database: 复用 `OrgUnits.ParentId/Path/Depth/UpdatedAt`，不新增 Schema 迁移
- Security: 移动使用独立 `api:org-unit:move` / `btn:org-unit:move` 权限，且只能操作当前公司组织
- Compatibility: 节点 ID 和所有外键引用保持不变；依赖组织子树的可见范围会按新层级即时变化
