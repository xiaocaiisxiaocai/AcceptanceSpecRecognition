## ADDED Requirements
### Requirement: 用户角色关系强制单角色约束
系统 MUST 在数据库层保证每个用户最多只能存在一条角色关系记录。

#### Scenario: 新增或更新用户角色关系
- **WHEN** 系统写入 `AuthUserRoles`
- **THEN** 同一 `UserId` 只能保留一条角色关系记录

### Requirement: 历史多角色用户迁移到单角色
系统 MUST 在迁移阶段将历史多角色或无角色用户修正为单角色状态。

#### Scenario: 迁移包含 admin 的多角色用户
- **WHEN** 某用户存在多条角色关系且其中包含 `admin`
- **THEN** 迁移后仅保留 `admin` 角色关系

#### Scenario: 迁移不含 admin 的多角色用户
- **WHEN** 某用户存在多条角色关系且不包含 `admin`
- **THEN** 迁移后按 `CreatedAt` 升序、`Id` 升序仅保留第一条角色关系

#### Scenario: 迁移无角色用户
- **WHEN** 某用户不存在任何角色关系
- **THEN** 迁移后系统为其补齐 `common` 角色关系
