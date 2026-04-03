# Change: 新增匹配知识 AI 草稿生成功能

## Why
当前草稿生成链路依赖粘贴文本或上传文档正文抽取，这与用户真实操作目标不一致。用户更希望直接复用系统中已经沉淀的历史验规，并按客户、制程、机型、关键词和导入时间范围筛选后统一生成候选。继续沿用文档来源不仅增加交互成本，还会因为原始文件缺失导致生成失败。

## What Changes
- 在匹配知识配置页为每个分类单独新增 `AI 生成候选` 入口，只生成当前分类的候选草稿。
- 草稿弹窗只保留一种输入来源：`历史验规`。
- 用户可按客户、制程、机型、关键词、导入时间范围筛选历史验规，并查看当前命中结果预览与总数。
- 当前筛选结果默认全部参与生成，只提供 `全选 / 取消全选` 切换，不支持逐条勾选验规。
- 草稿生成接口改为接收历史验规筛选条件；移除粘贴文本、已上传文档、临时上传文档及相关后端处理分支。
- AI 输入改为由历史验规字段拼接而成，不再依赖上传文档解析或原始文件读取。
- 当命中的历史验规数量或拼接文本超过系统安全上限时，接口显式提示用户收窄筛选条件，而不是静默截断。
- 草稿仍先进入审核弹窗，用户可编辑、删除并导入到“自定义扩展”；系统内置（只读）层保持不变。

## Assumptions
- 第一版沿用现有 AI 服务配置，由用户选择用于生成草稿的 LLM 服务。
- 第一版按单个分类生成，不支持一次生成多个分类。
- 预览列表允许分页浏览，但实际生成范围始终以当前筛选条件命中的全部历史验规为准。
- 单位规则生成仅覆盖“单位别名”这一可配置层；常见单位换算仍由系统内部固定规则处理。
- 第一版不做草稿历史持久化，关闭弹窗后草稿丢弃。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code:
  - `web/src/views/config/matching-knowledge/index.vue`
  - `web/src/views/config/matching-knowledge/components/MatchingKnowledgeDraftDialog.vue`
  - `web/src/api/matching-knowledge.ts`
  - `web/src/api/spec.ts`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecQueryOptions.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecRepository.cs`
  - 匹配知识与验规筛选相关测试
