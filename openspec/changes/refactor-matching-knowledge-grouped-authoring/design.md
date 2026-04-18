## Context
这份 design 原本描述的是“matching-knowledge 分组式作者视图”方案，但当前分支已经移除了 matching-knowledge 对外配置 API、草稿生成入口和前端页面，因此该方案不再有落地位置。

## Goals / Non-Goals
- Goals:
  - 明确分组式作者视图方案已停用
  - 避免后续维护者误以为仍需恢复 matching-knowledge 页面和接口
- Non-Goals:
  - 不重新设计新的作者模型
  - 不恢复任何 matching-knowledge 读写 API

## Decisions

### Decision: 分组式作者视图方案取消实施
- 不再实现实体组、单位组、字段组、左右冲突组等作者模型
- 不再实现对应的 DTO、Controller、页面或草稿导入逻辑
- 现行规格以“matching-knowledge 旧接口与旧页面已移除”为准

## Verification
- 现行代码中不存在 matching-knowledge 页面、接口和 DTO
- pending change 文案不再与现行移除方案冲突
