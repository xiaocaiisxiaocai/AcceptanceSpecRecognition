# Change: 加固运行时正确性与资源边界

## Why

当前系统在审计状态、并发缓存、前端异步响应、文件生命周期、高成本计算和 AI 出站访问上存在可复现风险。它们跨越 API、Application、Data 和前端边界，需要统一定义状态语义、资源预算和回滚顺序，避免局部修复形成新的不一致。

## What Changes

- 让控制器审计记录最终 HTTP 状态，并使用独立持久化作用域隔离业务失败状态。
- 统一业务错误码与真实 HTTP 状态，隐藏未知异常内部信息。
- 让 Embedding 缓存唯一键竞争安全复用胜出记录。
- 为执行历史、语义搜索、批量回复和 SmartFill 生命周期增加作用域与取消保护。
- 恢复智能填充执行历史完整逐行回放和服务端分页。
- 为真实文件删除增加持久化待删除状态和幂等清理。
- 为重复分析和文件比较增加明确资源预算、取消和流式处理。
- 让所有 AI 出站路径共用精确 Origin 约束的直连客户端，禁用代理、自动重定向和 Host 覆盖。
- 修复高危前端依赖、脆弱测试、密码规则漂移和构建依赖不可重复问题。
- 明确文件级 SmartFill 确认交互覆盖较早的逐 Sheet 确认要求。

## Compatibility

- API 错误响应将使用更准确的 404、409、422、429 或 500，依赖“一律 400”的客户端需要同步适配。
- 文件真实删除改为异步最终一致，删除请求成功不再表示物理文件已在同一事务内删除。
- 超出重复分析或文件比较预算的请求将明确失败，不返回部分结果。
- 数据库迁移仅新增删除状态及索引，已有文件和业务记录保持兼容。

## Impact

- Affected specs: `api`, `architecture`, `data-storage`, `file-storage`, `matching-engine`, `user-interface`
- Affected code: API filters/controllers/error middleware, Application audit/cache/file services, EF Core migrations and hosted cleanup, AI HTTP transports, file compare and duplicate analysis, execution history/semantic search/batch reply/SmartFill frontends, dependency and CI configuration
- Related active changes: `optimize-large-file-compare-preview`, `update-smart-fill-recognition-step-flow`, `combine-smart-fill-sheet-confirm`, `harden-single-company-production-boundaries`, `harden-browser-auth-token-lifecycle`
