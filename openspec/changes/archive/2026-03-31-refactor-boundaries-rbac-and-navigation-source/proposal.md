# Change: 重构系统边界、RBAC 能力面与导航元数据来源

## Why

当前项目已经出现明显的架构漂移：

- 文档宣称 `Api -> Core -> Data`，但实际代码中 `Data` 反向依赖 `Core`
- 导入、匹配、填充、下载等跨资源工作流集中在少数超大控制器和服务中，缺少显式应用层
- `Repository + UnitOfWork` 与 `AppDbContext` 直接访问并存，数据访问边界不一致
- RBAC 模型底层保留多组织、多角色、多节点范围能力，但 API / UI / 数据范围计算已经被裁成单组织、单角色，契约与模型长期不一致
- 前端路由元数据、页面权限码、后端权限种子存在多份真相，同时还残留未启用的 async-routes 模板兼容层

本次变更采用“强收敛”方案：新增显式 `Application` 层，统一依赖方向与工作流边界，同时把 RBAC 和导航元数据收敛到与当前产品真实能力一致的契约。

## What Changes

- 新增显式 `Application` 层，重构依赖方向为 `Api -> Application -> Core / Data`，并保证 `Core` 与 `Data` 互不依赖
- 将导入、智能填充、严格复用、下载、任务快照与 RBAC 管理等跨资源工作流拆分为按用例划分的 Application 服务，控制器只保留协议适配职责
- 清理 `Data -> Core` 反向依赖，把 Core-facing provider adapter、映射与默认策略选择移出 Data 层
- 统一数据访问边界：控制器、中间件和协议适配层不再直接编排 `AppDbContext`
- 将 RBAC 契约明确收敛为“单公司、单根组织、单角色用户”，移除多层级组织/多节点范围在 API 与 UI 上的无效能力面
- 建立页面、菜单、权限码的单一元数据来源，前后端共同消费；前端不再依赖运行时 async-routes 初始化
- 尽量保持现有 API 路径、请求/响应字段和前端页面入口不变，只强收敛内部结构与元数据来源

## Impact

- Affected specs:
  - `architecture`
  - `api`
  - `data-storage`
  - `user-interface`
- Affected code:
  - `AcceptanceSpecSystem.sln`
  - `src/AcceptanceSpecSystem.Api/*`
  - `src/AcceptanceSpecSystem.Core/*`
  - `src/AcceptanceSpecSystem.Data/*`
  - `web/src/router/*`
  - `web/src/store/*`
  - `web/src/api/routes.ts`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
  - `tests/AcceptanceSpecSystem.Data.Tests/*`
- Breaking changes:
  - OpenSpec 将正式声明系统组织契约为单根组织，而非多层级组织树
  - OpenSpec 将正式声明前端不再依赖运行时 async-routes 初始化
