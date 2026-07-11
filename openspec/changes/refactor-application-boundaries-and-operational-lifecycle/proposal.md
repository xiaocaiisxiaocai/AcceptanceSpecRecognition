# Change: 收敛应用边界与运行生命周期

## Why

现有架构规格已经声明 `Api -> Application -> Core / Data`，但 `Application.csproj` 仍通过 `Compile Include/Link` 编译 Api DTO 与 Data 源文件，部分控制器和审计过滤器仍直接依赖 `IUnitOfWork`，大量工作流实现仍位于 `Api/Services`。这使项目引用图与真实源码所有权不一致，也让协议层、应用编排和持久化生命周期难以独立验证。

同时，批量回复过期清理仍依赖业务请求触发，大文件下载和文档解析/写回缺少统一的取消、流式传输与并发预算；生产镜像身份、基础镜像可复现性以及真实 MySQL、浏览器 E2E、覆盖率趋势门禁也尚未形成稳定契约。上述问题需要分阶段治理，不能继续以局部源文件搬运或单点补丁宣告架构完成。

## What Changes

- 移除 `AcceptanceSpecSystem.Application.csproj` 对 Api/Data 源文件的跨项目 `Compile Include/Link`，使每个源文件只由其所属项目编译。
- 将控制器、Action Filter 与其他协议适配组件对 `IUnitOfWork`、Repository、`AppDbContext` 的直接依赖替换为 Application 用例/查询/审计端口。
- 按文档、匹配与填充、BatchReply、RBAC、配置和运维模块分批迁移现存 `Api/Services` 工作流；每批保持 HTTP 路径和外部 DTO 兼容并设置独立验收门禁。
- 为 BatchReply 过期 session/artifact manifest 增加独立、可取消、可观测的 hosted cleanup，不再依赖新业务请求顺带触发。
- 将大文件下载改为流式响应，并为 Word/Excel parser、writer 与匹配/填充流水线建立取消传播、并发上限和内存预算。
- 将生产容器切换为非 root 运行，并用不可变 digest 或等价锁定方式固定基础镜像；保留可验证的健康检查与持久卷权限。
- 在 CI 中增加真实 MySQL 契约/迁移测试、关键浏览器 E2E、覆盖率产物与趋势基线；采用分阶段门禁，避免一次性用历史缺口阻断全部开发。

## Impact

- Affected specs: `architecture`, `api`, `file-storage`, `user-interface`, `matching-engine`
- Affected code: `AcceptanceSpecSystem.Application.csproj`、`Api/Controllers`、过滤器、`Api/Services` 工作流、文档 parser/writer、下载端点、BatchReply 生命周期服务、Dockerfile/Compose、CI 与测试工程
- External compatibility: 原有 API 路径、请求/响应 DTO、匹配决策语义和用户数据格式原则上保持兼容；若实施中发现必须破坏兼容，须另建 OpenSpec 变更
- Rollout: 按模块分批迁移，每批独立验证和回滚，不采用一次性全仓切换
