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

### Requirement: 批量回复与严格复用采用分离的来源模型
系统 MUST 将“智能填充完成后的严格复用”和“基于已回复文档的批量回复”建模为两条独立用例链路，但复用相同的严格校验与写回协作组件。

#### Scenario: 批量回复不复用填充任务快照作为来源
- **WHEN** 系统处理批量回复请求
- **THEN** 控制器和应用服务从用户上传的来源文档构建临时会话
- **AND** 不要求依赖智能填充任务快照作为来源模型

#### Scenario: 严格校验与写回逻辑共享
- **WHEN** 系统实现批量回复能力
- **THEN** 批量回复复用现有文档读取、表格提取、写回和打包下载基础设施
- **AND** 不复制另一套结构等价的底层实现

### Requirement: 批量回复工作流进入应用层
系统 MUST 将批量回复的上传会话、预检、执行和下载编排放入独立 Application 服务，而不是放在控制器或页面层直接拼接实现。

#### Scenario: 控制器只承担协议适配
- **WHEN** 客户端调用批量回复接口
- **THEN** 控制器只负责接收上传、调用应用服务和返回响应
- **AND** 具体文档解析、严格比对和写回编排由应用服务负责

### Requirement: 前端高复杂度视图必须分离编排壳与局部展示边界
系统 MUST 将高复杂度工作流页面和高信息密度弹窗实现为“顶层编排壳 + 聚焦子组件 + 本地派生逻辑”结构，而不是继续堆叠为单文件巨型视图。

#### Scenario: 数据导入页面收敛为编排壳
- **WHEN** 开发者查看数据导入页面实现
- **THEN** 页面级组件保留步骤导航、顶层状态装配和跨步骤动作
- **AND** 上传、表格选择、映射配置、目标选择、确认导入与差异确认弹窗拆为聚焦组件或本地 composable

#### Scenario: 匹配详情弹窗收敛为弹窗壳
- **WHEN** 开发者查看匹配详情弹窗实现
- **THEN** 弹窗级组件保留 `visible` / `item` 桥接与顶层组合
- **AND** 最佳匹配展示、差异对照、候选列表以及 diff / 比较派生逻辑拆到聚焦组件或本地 composable

### Requirement: Embedding 缓存后台任务边界
系统 SHALL 通过后台 HostedService 执行 Embedding 缓存预热，并将具体缓存生成逻辑委派给应用服务或专用缓存服务。

#### Scenario: 后台任务不阻塞主业务
- **WHEN** 系统启动或到达预热计划时间
- **THEN** 后台任务可以扫描并补齐缺失的 Embedding 缓存
- **AND** 预热失败不得导致应用启动失败
- **AND** 预热失败不得影响导入、智能填充或语义搜索接口继续按既有规则运行

#### Scenario: 请求链路保留兜底
- **GIVEN** 某条历史规格尚未被定时任务预热
- **WHEN** 用户发起智能填充或语义搜索
- **THEN** 系统仍可在请求链路中按需生成缺失缓存
- **AND** 生成结果写回持久化缓存供后续请求复用

### Requirement: Embedding 缓存预热管理入口
系统 SHALL 提供受权限控制的管理入口，用于查看当前预热配置、运行状态并手动触发一次预热。

#### Scenario: 查看配置与状态
- **WHEN** 管理员打开 Embedding 缓存预热页面
- **THEN** 系统展示当前启用状态、执行时间、间隔、批大小、单轮上限和最近一次执行结果

#### Scenario: 手动触发预热
- **WHEN** 管理员点击立即预热
- **THEN** 系统按当前配置触发一次后台预热
- **AND** 若已有预热正在运行，系统 SHALL 阻止重复触发并给出明确提示

#### Scenario: 调整运行期配置
- **WHEN** 管理员保存预热配置
- **THEN** 当前 API 进程 SHALL 使用新的配置执行后续调度
- **AND** 系统不直接改写部署配置文件

### Requirement: 运行保护策略集中配置
系统 MUST 在 API 启动阶段集中注册限流与健康检查策略，控制器只声明策略名称，不在动作方法内硬编码计数器或探活逻辑。

#### Scenario: 控制器声明限流策略
- **WHEN** 开发者查看登录、上传、AI/匹配重接口
- **THEN** 控制器通过框架限流特性或端点元数据声明策略
- **AND** 具体窗口、阈值和队列行为由启动配置统一管理

#### Scenario: 健康检查由框架执行
- **WHEN** `/health` 被调用
- **THEN** ASP.NET Core 健康检查框架执行依赖检查
- **AND** 控制器或最小 API 不手工拼装数据库探活逻辑

### Requirement: API 用例服务接口边界
系统 MUST 让控制器依赖简单 AppService 的接口契约，而不是直接依赖具体实现；接口化不得改变既有业务行为。

#### Scenario: 控制器依赖接口
- **WHEN** 开发者查看已接口化的 API AppService 控制器依赖
- **THEN** 控制器构造函数注入 `I*AppService`
- **AND** 依赖注入容器将接口映射到对应实现

### Requirement: 仓储行为具备回归测试
系统 MUST 为高价值仓储查询与通用 CRUD 行为提供自动化测试，覆盖排序、过滤、导航加载和缺失数据场景。

#### Scenario: 仓储查询回归
- **WHEN** 仓储查询逻辑被修改
- **THEN** 测试能够验证关键过滤、排序和关联加载行为未退化

### Requirement: 智能结构识别遵守应用层编排边界
系统 MUST 将智能结构识别的跨资源编排放入 Application 层，而不是由控制器直接编排 Core、Data 和文件解析细节。

#### Scenario: 控制器只做协议适配
- **WHEN** 客户端调用智能结构识别或确认 API
- **THEN** 控制器只负责请求接收、权限上下文传递和响应包装
- **AND** 控制器委派 Application 用例服务完成文档解析、识别、模板命中和学习沉淀

#### Scenario: Core 保持纯算法职责
- **WHEN** 系统执行表格结构识别算法
- **THEN** Core 层只处理表格数据、规则策略、LLM 结构裁决接口和确定性体检
- **AND** Core 层不引用 API、Application 或 Data 类型

#### Scenario: Data 保持纯持久化职责
- **WHEN** 系统保存模板或列映射学习词
- **THEN** Data 层只提供实体、仓储和迁移
- **AND** Data 层不实现 Core 业务接口或 Application 用例服务

