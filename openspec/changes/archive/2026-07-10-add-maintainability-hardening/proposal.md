# Change: 增强可维护性与验证覆盖

## Why
当前项目仍存在局部类型约束不足、应用服务缺少接口边界、仓储测试覆盖不足、控制器取消传递与请求验证不完整等问题。这些问题不直接改变业务能力，但会提高后续迭代的回归风险。

## What Changes
- 继续补充前端 TypeScript 类型约束与可运行测试。
- 为更多简单 API AppService 暴露接口，并让控制器依赖接口契约。
- 补充高价值仓储单元测试，覆盖查询、排序、导航加载与通用 CRUD 行为。
- 为更多控制器查询路径传递 `CancellationToken`，并为关键请求 DTO 添加 DataAnnotations。
- 低风险优化 Docker/CI/健康检查配置，不移除 `mysqldump` 备份依赖。

## Impact
- Affected specs: architecture, api
- Affected code: `web/src`, `src/AcceptanceSpecSystem.Api`, `tests/AcceptanceSpecSystem.*.Tests`, Docker/CI 配置
