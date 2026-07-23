## Context
当前系统已经有 `EmbeddingCaches` 表，并在智能填充预览、匹配执行和验收规格 AI 搜索中按需生成缓存。问题有两个：

- 首次匹配历史数据时，用户请求需要等待候选向量生成。
- 缓存键只有 `SpecId + ModelName`，但匹配使用 `项目 + 规格`，AI 搜索使用 `项目 + 规格 + 验收 + 备注`，存在跨用途复用错误向量的风险。

## Goals / Non-Goals
Goals:
- 夜间或固定间隔自动补齐历史规格向量缓存。
- 缓存按用途和文本指纹隔离，避免错误复用。
- 保留请求链路懒生成兜底。
- 后台任务失败不影响主业务。
- 提供配置管理入口，允许管理员查看、调整并手动触发预热。

Non-Goals:
- 不引入独立向量数据库。
- 不改变匹配决策规则和阈值。
- 不让导入流程同步等待向量生成。

## Decisions
- 新增 `EmbeddingCacheUsage` 字段，区分 `matching`、`semantic-search`、`import-duplicate-detection`。
- 新增 `TextHash` 字段，保存本次向量对应文本的稳定哈希；缓存命中必须同时满足 `SpecId + ModelName + Usage + TextHash`。
- 将唯一索引从 `SpecId + ModelName` 调整为 `SpecId + ModelName + Usage`，同一用途同一模型只保留当前有效文本的一条缓存。
- 新增 `EmbeddingCacheWarmupService : BackgroundService`，复用现有 `AuditLogCleanupService` / `EmbeddingCacheCleanupService` 的 HostedService 模式。
- 定时任务按配置的本地时间运行，默认关闭启动即跑，默认每天低峰执行。
- 后台任务只负责全局预热，不套用户数据范围；权限仍由业务查询接口控制。
- 管理页面修改运行期配置，不直接写 `appsettings.json`；部署配置仍由环境和运维控制。

## Risks / Trade-offs
- 数据库迁移需要兼容旧缓存。迁移后旧缓存缺少用途和文本指纹，应视为不可命中，由懒生成或定时任务重建。
- 多实例部署可能重复预热同一批数据。当前内网单实例优先，先通过小批量与唯一索引降低影响；多实例锁作为后续增强。
- 语义搜索缓存文本包含验收和备注，更新这两个字段也必须失效对应缓存。

## Migration Plan
1. 为 `EmbeddingCaches` 增加 `Usage`、`TextHash` 字段。
2. 迁移旧缓存为默认 `matching` 用途，并设置空或历史标记指纹。
3. 调整仓储查询接口，按用途和文本指纹读取缓存。
4. 后台任务逐步重建缺失或指纹不匹配的缓存。

## Open Questions
- 生产环境默认是否启用定时预热，建议先默认开启但限制单轮数量。
- 夜间时间默认使用 `02:00`，是否需要按部署环境调整。
