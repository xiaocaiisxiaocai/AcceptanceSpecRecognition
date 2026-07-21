# Change: 加固后端安全边界与运行韧性

## Why

安全审查确认管理员边界、匹配预览进度、批量上传、日志和数据库备份在并发、取消或多用户场景下仍存在可绕过或资源泄漏风险。这些问题横跨 API、Application、Data 与文件生命周期，需要在保持现有 HTTP 路径和业务数据兼容的前提下统一修复。

## What Changes

- 在公司级跨实例操作锁内原子校验管理员角色有效期，禁止并发操作或未来/过期角色时间制造“零有效管理员”。
- 将匹配预览进度按公司、用户和请求标识隔离，并对请求标识和失败信息执行服务端校验与脱敏。
- 将批量回复上传改为有总量预算的顺序流式落盘，取消或失败时补偿本次新增临时文件。
- 禁止 LLM Prompt、规格正文和模型自由文本进入运行日志。
- 让数据库备份使用 `.partial` 临时文件，取消时终止进程树并清理半成品，成功后原子发布。
- 将请求取消信号贯穿 Application、Repository、EF 与事务调用；不再让客户端取消后的数据库操作无边界继续执行。
- 将新建和重置用户密码的最低长度统一为 12 位；现有账号登录保持兼容。

## Impact

- Affected specs: `api`, `architecture`, `file-storage`, `user-interface`
- Affected code: 系统用户用例、匹配预览进度、BatchReply 上传、通用仓储、LLM 日志、数据库备份、系统用户前端
- Compatibility: 保持现有 HTTP 路径和主要 JSON 字段；不新增数据库迁移；不强制已有短密码账号立即失效
- Related changes: `refactor-application-boundaries-and-operational-lifecycle`, `harden-browser-auth-token-lifecycle`
