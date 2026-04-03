## Context

当前仓库最核心的问题不是单点 bug，而是边界长期漂移：

- 代码依赖方向与文档不一致，导致分层失去公信力
- 业务工作流缺少显式应用层，用例编排挤进 API 层
- 数据访问抽象被部分绕过，仓储与 `DbContext` 双轨并存
- RBAC 模型和产品契约分裂，调用方看到的是“单组织、单角色”，内部却仍保留“大而全”的能力面
- 前后端页面与权限元数据各自维护，导航初始化还背着模板残留

本次变更不是新增业务功能，而是对系统的“真实架构”做一次强收敛，使代码结构、运行契约和 OpenSpec 重新一致。

## Goals / Non-Goals

- Goals:
  - 新增显式 Application 层，建立稳定依赖方向
  - 将导入、匹配、填充、RBAC 等跨资源工作流拆成小而清晰的用例服务
  - 消除 `Data -> Core` 反向依赖，恢复 Data 纯持久化职责
  - 统一数据访问边界，停止控制器直接编排 `AppDbContext`
  - 将 RBAC 能力面明确收敛到单公司、单根组织、单角色用户
  - 建立前后端共享的导航/权限元数据单一来源，移除 async-routes 运行时依赖
  - 在尽量不改外部 API 路径和页面入口的前提下完成重构
- Non-Goals:
  - 不在本次变更中重写匹配算法本身
  - 不在本次变更中大规模替换现有前端 UI 组件或页面布局
  - 不以“删除所有历史表”为目标，优先收敛运行契约和代码边界
  - 不主动引入新的远程配置中心或复杂代码生成体系

## Decisions

### Decision: 新增显式 Application 层

新增 `AcceptanceSpecSystem.Application` 项目，用于承载：

- 文档导入用例
- 匹配预览用例
- 填充执行与下载用例
- 严格复用用例
- 系统用户、角色、组织、权限字典用例
- 导航元数据读取与权限种子输入适配

目标依赖方向固定为：

- `Api -> Application`
- `Application -> Core`
- `Application -> Data`
- `Core` 不依赖 `Data`
- `Data` 不依赖 `Core`

这样 API 层只负责 HTTP 协议适配，Core 保持算法和文档处理能力，Data 保持 EF Core 与仓储职责，Application 专门承接跨模块业务编排。

### Decision: 用例服务按工作流拆分，而不是继续扩展巨型服务

不再保留“一个大服务兜住全部工作流”的方式。

按用例拆分 Application 服务，至少包括：

- `DocumentImportAppService`
- `MatchingPreviewAppService`
- `MatchingExecutionAppService`
- `MatchingTaskAppService`
- `StrictReuseAppService`
- `SystemUserAppService`
- `AuthRoleAppService`
- `OrgUnitAppService`
- `AuthPermissionQueryService`

控制器每个动作只做：

1. 参数接收与协议校验  
2. 调用单个用例服务  
3. 将结果映射为响应 DTO

### Decision: Data 层恢复纯持久化职责

Data 层只保留以下内容：

- EF Core 实体
- `DbContext`
- Repository / Query object
- Migration
- 持久化相关值转换与索引配置

以下内容移出 Data：

- 对 Core 接口的实现适配器
- Core 枚举或模型映射
- 任何“默认匹配策略”“AI 能力选择”之类的业务语义

持久化模型若需要表达默认策略、用途或状态，应使用 Data 自己的枚举或基础值类型，再由 Application 层负责映射。

### Decision: 统一数据访问入口到 Application 内部

控制器、中间件、过滤器和前端协议适配层不再直接注入 `AppDbContext`。

Application 层内部可根据需要选择：

- 通过 Repository / UnitOfWork 访问聚合写入
- 通过专用查询服务处理复杂只读查询

但这种选择只允许发生在 Application 内部，不再向 API 层泄漏。

### Decision: RBAC 契约显式收敛为单公司、单根组织、单角色用户

本次重构不再延续“底层支持任意多组织树和多角色，接口层再硬裁”的方式。

正式契约定义为：

- 每个公司只有一个业务根组织节点
- 组织接口只允许读取和编辑根组织
- 每个用户只有一个有效角色
- 每个用户只有一个有效组织归属
- 角色数据范围不再对外提供多节点自定义能力

底层关系表可在迁移过渡期保留，但 API、UI、DTO、校验、种子初始化和数据范围计算都必须围绕上述单一契约实现。

### Decision: 导航、菜单与页面权限码改为单一元数据来源

新增共享导航元数据清单，作为以下内容的单一真相：

- 菜单权限码
- 页面权限码
- 菜单标题与层级
- 页面路径到权限码的映射

前端静态路由模块从该清单装配权限与标题信息；后端权限种子初始化也从同一清单读取页面/菜单权限定义。

保留 `/get-async-routes` 兼容接口，但前端启动过程不再依赖它；该接口只作为兼容性空实现保留，避免对外路径发生不必要破坏。

## Risks / Trade-offs

- 新增 Application 项目会扩大首轮改动面
  - Mitigation: 先保持 API 路径和 DTO 兼容，只重构内部依赖与委派关系
- 拆分工作流服务时容易把逻辑机械搬家，没真正降复杂度
  - Mitigation: 以“一个服务一个用例”为原则，先拆职责，再决定共享组件
- RBAC 收敛可能触及现有测试、种子和前端表单
  - Mitigation: 先把 OpenSpec 契约写清，再同步更新测试基线
- 共享导航元数据如果设计得过于复杂，会引入新的维护负担
  - Mitigation: 只共享菜单/页面/权限元数据，不共享组件实现细节

## Migration Plan

1. 新增 `Application` 项目并接入解决方案
2. 将 Core-facing adapter 和映射从 Data 移到 Application
3. 先拆导入与匹配工作流，再拆 RBAC 用例
4. 替换 API 层对 `AppDbContext` 的直接依赖
5. 收敛 RBAC DTO、控制器和数据范围逻辑到单根组织/单角色契约
6. 引入共享导航元数据清单，替换前后端重复定义
7. 移除前端运行时 async-routes 依赖，保留兼容接口
8. 更新集成测试、仓储测试和前端构建验证

## Open Questions

- 共享导航元数据清单最终采用 JSON 还是 C# / TS 双消费的中立格式
- 角色数据范围在收敛后是否保留 `All` 与 `Self` 两类契约，还是继续保留对根组织节点的显式表达
