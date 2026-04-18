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

### Requirement: 存储单公司根组织与 RBAC 关系
系统 MUST 在数据库中提供公司、根组织、角色、权限、角色权限、用户角色、用户组织与角色数据范围等关系表，但正式运行契约只支持每公司一个根组织节点。

#### Scenario: 根组织初始化
- **WHEN** 系统初始化公司组织数据
- **THEN** 每个公司只保留一个根组织节点作为正式业务组织

#### Scenario: 用户组织关系单根组织化
- **WHEN** 系统读取或写入用户组织归属
- **THEN** 用户只关联一个有效根组织归属
- **AND** 不再依赖多层级组织树作为正式运行契约

#### Scenario: 角色数据范围收敛
- **WHEN** 系统保存角色数据范围
- **THEN** 系统仅支持与单根组织契约一致的范围表达
- **AND** 不再对外提供多节点自定义范围能力

### Requirement: 旧用户角色数据迁移
系统 MUST 在迁移阶段将旧用户 JSON 角色信息转换到新关系表，并在迁移后移除旧列。

#### Scenario: 迁移已有管理员用户
- **WHEN** 数据库中存在 `RolesJson` 包含 `admin` 的历史账号
- **THEN** 迁移后该账号在 `AuthUserRoles` 中关联 `admin` 角色，并保留可登录能力

#### Scenario: 迁移普通用户
- **WHEN** 历史用户不包含 `admin` 角色
- **THEN** 迁移后该用户默认关联 `common` 角色并获得默认组织归属

### Requirement: 运行时匹配知识不作为用户配置持久化
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，而不是数据库中的用户可编辑配置。

#### Scenario: 当前模型不包含匹配知识配置表
- **WHEN** 系统加载当前数据库模型
- **THEN** 数据库中不存在 `MatchingKnowledgeConfigs` 之类的用户配置表

#### Scenario: 服务重启时不读取匹配知识配置
- **WHEN** 服务启动并初始化匹配引擎
- **THEN** 系统使用代码内置的运行时匹配知识模型
- **AND** 不依赖数据库中的匹配知识配置记录

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

### Requirement: 持久化层保持纯持久化职责
系统 MUST 将 Data 层限制为纯持久化职责，只包含 EF Core 模型、仓储、迁移与持久化映射，不承载 Core 业务抽象或用例适配实现。

#### Scenario: Data 层项目职责稳定
- **WHEN** 开发者检查 Data 层代码
- **THEN** Data 层只包含实体、`DbContext`、Repository、Migration 与持久化相关映射
- **AND** 不直接实现 Core 业务接口

#### Scenario: 持久化模型不引用 Core 业务类型
- **WHEN** 系统为持久化实体表达默认策略、用途或状态
- **THEN** Data 层使用自身的持久化模型或基础值类型表达
- **AND** 不直接引用 Core 业务枚举或模型

