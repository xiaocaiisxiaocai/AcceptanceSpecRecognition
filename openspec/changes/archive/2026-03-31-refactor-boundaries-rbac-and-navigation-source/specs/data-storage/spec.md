## ADDED Requirements

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

## MODIFIED Requirements

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
