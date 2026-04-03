## ADDED Requirements
### Requirement: 用户组织关系强制单组织约束
系统 MUST 在数据库层保证每个用户最多只能存在一条组织关系记录。

#### Scenario: 新增或更新用户组织关系
- **WHEN** 系统写入 `AuthUserOrgUnits`
- **THEN** 同一 `UserId` 只能保留一条组织关系记录

### Requirement: 历史多组织用户迁移到单组织
系统 MUST 在迁移阶段将历史多组织或无组织用户修正为单组织状态。

#### Scenario: 迁移存在唯一主组织的多组织用户
- **WHEN** 某用户存在多条组织关系且仅一条记录 `IsPrimary = true`
- **THEN** 迁移后仅保留该主组织关系

#### Scenario: 迁移不存在唯一主组织的多组织用户
- **WHEN** 某用户存在多条组织关系且没有唯一主组织
- **THEN** 迁移后按 `CreatedAt` 升序、`Id` 升序仅保留第一条组织关系

#### Scenario: 迁移无组织用户
- **WHEN** 某用户不存在任何组织关系
- **THEN** 迁移后系统为其补齐公司根组织关系
