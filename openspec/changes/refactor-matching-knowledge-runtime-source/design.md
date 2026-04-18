## Context
当前代码、测试和现行主规格已经不再提供 `matching-knowledge` 对外配置 API，也不再保留配置页与草稿生成入口。此前这份 design 仍在描述“数据库单例配置 + GET/PUT /api/matching-knowledge + 恢复默认/清空当前配置”的方案，和当前分支事实不一致。

## Goals / Non-Goals

### Goals
- 让 pending change 与现行实现保持一致，不再暗示存在可维护的 matching-knowledge API。
- 明确旧接口、旧页面和旧草稿能力已经下线。
- 保留“匹配知识属于运行时内部能力”的事实描述。

### Non-Goals
- 不重新设计新的 matching-knowledge 作者模型。
- 不恢复数据库单例配置、恢复默认或清空当前配置接口。
- 不改变当前匹配引擎内部对匹配知识的消费方式。

## Decisions

### Decision: matching-knowledge 不再提供对外配置 API
- 不再提供 `GET /api/matching-knowledge`、`PUT /api/matching-knowledge`。
- 不再提供 `POST /api/matching-knowledge/drafts/generate`、`/clear`、`/restore-defaults` 等派生接口。
- 不再提供 matching-knowledge 前端配置页和草稿生成交互。

### Decision: 匹配知识仅保留为运行时内部能力
- 运行时仍可保留最小必要的匹配知识模型与转换逻辑。
- 这些知识不再作为现行外部契约暴露，也不再承诺作者视图或持久化管理入口。

## Verification
- 旧接口访问返回 `404`
- 旧前端页面与 API 文件已移除
- OpenSpec pending change 文案与现行实现一致
