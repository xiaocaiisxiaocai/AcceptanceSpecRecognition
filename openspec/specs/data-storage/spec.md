# Data Storage Capability

## Purpose
定义当前系统的数据存储方式、核心数据组织与索引策略，确保验收规格数据在MySQL中可按客户与制程组合稳定查询并支持迁移。
## Requirements
### Requirement: MySQL数据库存储
系统 SHALL 使用MySQL数据库存储业务数据，并通过Entity Framework Core访问。

#### Scenario: 数据库连接
- **WHEN** 系统启动时
- **THEN** 系统使用配置的连接字符串建立MySQL连接

#### Scenario: 字符集支持
- **WHEN** 存储包含中文的数据
- **THEN** 系统以utf8mb4字符集正确存储与读取

---

### Requirement: 数据库迁移管理
系统 SHALL 使用EF Core Migrations管理数据库结构变更。

#### Scenario: 自动应用迁移
- **WHEN** 系统启动且存在待执行迁移
- **THEN** 系统应用所有待执行迁移

---

### Requirement: 验收规格按客户制程组合组织
系统 MUST 将验收规格按“客户 + 制程”的组合维度组织。

#### Scenario: 组合维度查询
- **WHEN** 用户选择一个客户与一个制程
- **THEN** 系统以该 (CustomerId, ProcessId) 组合为边界查询验收规格条目

---

### Requirement: 客户与制程独立维护
系统 SHALL 保持Customer与Process为独立基础数据。

#### Scenario: 制程无客户外键
- **WHEN** 系统维护制程数据
- **THEN** Process不包含CustomerId外键

---

### Requirement: 组合筛选索引
系统 SHALL 为验收规格建立用于组合筛选的索引。

#### Scenario: 组合索引
- **WHEN** 系统按 (CustomerId, ProcessId) 查询验收规格
- **THEN** 查询使用对应索引或等价实现

### Requirement: AI服务配置持久化思考模式开关
系统 SHALL 持久化存储 AI 服务的关闭思考模式配置。

#### Scenario: 保存关闭思考模式
- **WHEN** 管理员为某个 LLM AI 服务开启关闭思考模式
- **THEN** 系统将该开关随 AI 服务配置一起保存到数据库

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

### Requirement: 匹配知识配置数据库持久化
系统 SHALL 在 MySQL 中持久化当前生效的单例匹配知识配置。

#### Scenario: 服务重启后仍保持相同配置
- **GIVEN** 用户已保存一套匹配知识配置
- **WHEN** 服务重启后再次读取匹配知识
- **THEN** 系统返回与重启前一致的配置内容

#### Scenario: 始终只有一套生效配置
- **WHEN** 系统读取当前匹配知识配置
- **THEN** 系统返回唯一的当前生效配置
- **AND** 不要求用户在多个版本之间手工切换

### Requirement: 旧文本预处理数据迁移与清理
系统 SHALL 在引入数据库化匹配知识配置时，保守迁移可识别旧同义词并直接清理旧表。

#### Scenario: 迁移可识别的旧同义词
- **GIVEN** 旧同义词数据中存在能够明确归类为实体别名、单位别名或字段别名的词组
- **WHEN** 系统执行迁移
- **THEN** 系统将这些可识别词组迁移到对应的匹配知识槽位

#### Scenario: 丢弃不可识别旧数据
- **GIVEN** 旧同义词、关键字或文本预处理配置中存在无法明确映射到结构化知识槽位的数据
- **WHEN** 系统执行迁移
- **THEN** 系统直接丢弃这些旧数据
- **AND** 不将其写入新的匹配知识配置

#### Scenario: 删除旧配置表
- **WHEN** 数据迁移完成
- **THEN** 系统删除 `SynonymGroups`、`SynonymWords`、`Keywords` 和 `TextProcessingConfigs` 旧表

### Requirement: 系统用户数据持久化
系统 SHALL 在数据库中持久化系统用户账号信息，而不是通过配置文件存储账号密码。

#### Scenario: 用户表结构
- **WHEN** 系统应用数据库迁移
- **THEN** 数据库中创建 `SystemUsers` 表并包含用户名、密码哈希、角色权限、启用状态等字段

#### Scenario: 用户名唯一
- **WHEN** 系统保存系统用户账号
- **THEN** `Username` 字段保持唯一约束

#### Scenario: 密码哈希存储
- **WHEN** 系统写入或更新用户密码
- **THEN** 数据库存储 PBKDF2 哈希值而非明文密码

---

### Requirement: 默认账号初始化
系统 SHALL 在用户表为空时自动写入默认账号，确保首次部署后可登录。

#### Scenario: 首次启动初始化
- **WHEN** 系统启动且 `SystemUsers` 表为空
- **THEN** 系统自动写入默认 `admin` 与 `common` 账号

#### Scenario: 非首次启动不重复初始化
- **WHEN** 系统启动且 `SystemUsers` 表已有数据
- **THEN** 系统不重复写入默认账号

### Requirement: Prompt 模板场景化持久化
系统 SHALL 持久化存储 Prompt 模板的场景、显示名称与系统模板标记。

#### Scenario: 存储系统模板元数据
- **WHEN** 系统保存系统 Prompt 模板
- **THEN** 数据库保存该模板的场景标识、显示名称和系统模板标记

#### Scenario: 旧模板自动映射
- **GIVEN** 数据库中存在仅包含旧名称的 Prompt 模板数据
- **WHEN** 系统完成迁移或首次读取
- **THEN** 系统将旧模板映射到对应系统场景
- **AND** 为缺失系统模板的场景补齐默认内容

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

