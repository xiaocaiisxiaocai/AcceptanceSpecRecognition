# Change: 新增匹配知识 AI 草稿生成功能

## Why
当前匹配知识配置完全依赖人工维护，用户需要自己整理实体别名、单位规则、字段别名和冲突词对，录入成本高且容易漏掉高频术语。  
系统已经具备 LLM 能力、Prompt 模板管理和文档上传链路，但尚未把这些能力用于“生成可审核的匹配知识草稿”，导致知识沉淀效率偏低。

## What Changes
- 在匹配知识配置页为每个分类单独新增 `AI 生成候选` 入口，只生成当前分类的候选草稿。
- 支持三种输入来源：
  - 粘贴文本
  - 选择已上传文档
  - 在弹窗中临时上传新文档，并支持“仅本次使用 / 保存到已上传文档”。
- 新增后端 AI 草稿生成接口，返回结构化候选项、命中片段/理由和冲突状态。
- 前端新增“草稿审核弹窗”，支持勾选、删除、编辑后再导入到“自定义扩展”。
- 导入时执行合并去重：
  - 同 key 同 value 自动去重
  - 同 key 不同 value 标记为冲突待确认
  - 冲突词对左右互换视为同一条
- 第一版只支持人工审核后导入，不支持自动保存，不改动系统内置（只读）层。

## Assumptions
- 第一版沿用现有 AI 服务配置，由用户选择用于生成草稿的 LLM 服务。
- 单位规则生成仅覆盖“单位别名”这一可配置层；常见单位换算仍由系统内部固定规则处理。
- 第一版按单个分类生成，不支持一次生成多个分类。
- 第一版不做草稿历史持久化，关闭弹窗后草稿丢弃。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code:
  - `web/src/views/config/matching-knowledge/index.vue`
  - `web/src/api/matching-knowledge.ts`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/*`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/LlmMatchingAssistService.cs`
  - 匹配知识相关前后端测试
