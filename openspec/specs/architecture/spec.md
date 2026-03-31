# architecture Specification

## Purpose
TBD - created by archiving change refactor-boundaries-rbac-and-navigation-source. Update Purpose after archive.
## Requirements
### Requirement: 显式应用层边界
系统 MUST 提供显式 `Application` 层承载用例编排，并固定依赖方向为 `Api -> Application -> Core / Data`；其中 `Core` 与 `Data` MUST 互不依赖。

#### Scenario: 解决方案依赖方向稳定
- **WHEN** 开发者查看解决方案项目引用关系
- **THEN** API 项目只依赖 Application
- **AND** Application 项目依赖 Core 与 Data
- **AND** Core 项目不依赖 Data
- **AND** Data 项目不依赖 Core 或 Api

### Requirement: 工作流编排不得停留在协议层
系统 MUST 将导入、匹配、填充、下载、严格复用和 RBAC 变更等跨资源工作流放入按用例拆分的 Application 服务，而不是控制器或单一巨型服务。

#### Scenario: 文档导入工作流拆分
- **WHEN** 系统处理文件导入
- **THEN** 控制器只负责 HTTP 参数接收与响应包装
- **AND** Application 用例服务负责解析、校验、去重决策和持久化编排

#### Scenario: 智能填充工作流拆分
- **WHEN** 系统处理匹配预览、执行填充或下载结果
- **THEN** 每个动作委派到独立的 Application 用例服务
- **AND** 不再由单个巨型服务同时承担全部匹配相关流程

### Requirement: 协议层不得直接编排持久化细节
系统 MUST 禁止控制器、中间件和协议适配层直接编排 `AppDbContext` 访问；数据读写边界只能由 Application 内部组件统一管理。

#### Scenario: 控制器依赖收敛
- **WHEN** 开发者查看控制器构造函数依赖
- **THEN** 控制器依赖的是 Application 用例服务或查询服务
- **AND** 不直接依赖 `AppDbContext`

#### Scenario: 数据访问集中在 Application 内部
- **WHEN** 系统执行写入或复杂查询
- **THEN** Application 层决定使用 Repository、UnitOfWork 或专用查询组件
- **AND** API 层不感知持久化实现细节

