## MODIFIED Requirements

### Requirement: 显式应用层边界
系统 MUST 提供显式 `Application` 层承载用例编排，并固定依赖方向为 `Api -> Application -> Core / Data`；其中 `Core` 与 `Data` MUST 互不依赖。项目引用、源码目录与编译归属 MUST 表达同一依赖事实，不得通过跨项目 `Compile Include/Link`、源码复制或等价方式绕过边界。

#### Scenario: 解决方案依赖方向稳定
- **WHEN** 开发者查看解决方案项目引用关系
- **THEN** API 项目只依赖 Application
- **AND** Application 项目依赖 Core 与 Data
- **AND** Core 项目不依赖 Data
- **AND** Data 项目不依赖 Core 或 Api

#### Scenario: 源码编译归属唯一
- **WHEN** CI 检查项目文件和编译输入
- **THEN** Application 不编译 Api 或 Data 目录下的源文件
- **AND** 每个生产源码文件只由其所属项目编译
- **AND** DTO、应用端口和 provider adapter 均有明确且符合依赖方向的唯一归属

### Requirement: 工作流编排不得停留在协议层
系统 MUST 将导入、比较、匹配、填充、下载、批量回复、严格复用、配置管理和 RBAC 变更等跨资源工作流放入按用例与模块拆分的 Application 服务，而不是控制器、filter、后台协议适配器或单一巨型服务。

#### Scenario: 文档导入工作流拆分
- **WHEN** 系统处理文件上传、预览、比较或导入
- **THEN** 控制器只负责 HTTP 参数接收与响应包装
- **AND** Application 用例服务负责解析、校验、去重决策和持久化编排

#### Scenario: 智能填充工作流拆分
- **WHEN** 系统处理匹配预览、执行填充或下载结果
- **THEN** 每个动作委派到独立的 Application 用例服务
- **AND** 不再由单个巨型服务同时承担全部匹配相关流程

#### Scenario: 批量回复与运维工作流拆分
- **WHEN** 系统处理 BatchReply 会话、预览、执行、下载、过期清理或后台运维任务
- **THEN** Application 用例或生命周期服务拥有业务编排
- **AND** Api hosted adapter 只负责宿主调度和取消信号转发

### Requirement: 协议层不得直接编排持久化细节
系统 MUST 禁止控制器、中间件、Action Filter、Endpoint Filter 和协议/宿主适配层直接编排 `AppDbContext`、`IUnitOfWork` 或 Repository 访问；数据读写、事务和审计持久化边界只能由 Application 内部组件统一管理。

#### Scenario: 控制器与过滤器依赖收敛
- **WHEN** 开发者查看控制器、filter 和中间件构造函数依赖
- **THEN** 这些组件依赖的是 Application 用例、查询或审计端口
- **AND** 不直接依赖 `AppDbContext`、`IUnitOfWork` 或 Repository

#### Scenario: 数据访问集中在 Application 内部
- **WHEN** 系统执行写入、审计记录或复杂查询
- **THEN** Application 层决定使用 Repository、UnitOfWork 或专用查询组件
- **AND** API 层不感知持久化实现细节

## ADDED Requirements

### Requirement: 生产运行环境可复现且最小权限
系统 MUST 使用不可变基础镜像标识构建生产容器，并让 API 与 Web runtime 以专用非 root 身份运行，同时保留健康检查和持久卷所需的最小权限。

#### Scenario: 基础镜像可复现
- **WHEN** CI 构建 API 与 Web 镜像
- **THEN** .NET、Node 和 Nginx 基础镜像使用明确版本及 digest 或等价不可变标识
- **AND** 基础镜像升级通过显式依赖更新完成

#### Scenario: 非 root 容器运行
- **WHEN** 生产容器启动并执行健康检查与卷读写 smoke
- **THEN** API 与 Web runtime 进程不以 root 身份运行
- **AND** API 仅能写入文件、DataProtection keys、备份等声明目录
- **AND** 健康检查和持久卷读写成功

### Requirement: 分层质量门禁包含生产等价信号
系统 MUST 在快速单元/集成测试之外提供真实 MySQL、关键浏览器工作流与覆盖率趋势信号，并在信号稳定后分阶段设置为合并门禁。

#### Scenario: 真实 MySQL 契约验证
- **WHEN** CI 运行生产等价数据测试
- **THEN** 系统在真实 MySQL 8 上执行 migration 与关键 repository/query 测试
- **AND** 验证时区、排序、唯一约束和 provider 特有行为

#### Scenario: 覆盖率趋势可追踪
- **WHEN** CI 完成后端与前端测试
- **THEN** 系统生成可机读覆盖率报告并保留 artifact
- **AND** 基线稳定后对约定的总体或变更代码覆盖率执行不回退检查
