# Change: 智能结构识别自适应表格路由

## Why
真实客户文档中，同一客户在不同时期、不同人员维护的 Word/Excel 表格结构差异很大；Excel 工作簿还常混入报价、Layout、Utility、备品、签核、SECS、工安环保等非主验收表。当前智能结构识别已经能识别表头和字段，但仍偏向“逐表识别”，缺少文档级判断：哪些表值得导入、哪些表应跳过、哪些表需要确认，以及为什么。

## What Changes
- 在智能结构识别结果中增加表格类型、推荐级别、排序分、跳过建议和结构问题原因。
- 引入文档级候选表排序：优先推荐最像验收规格表的主表或专项验收表，降低报价、Layout、Utility、备品清单等辅助表的干扰。
- 将用户确认结果沉淀为“结构案例”信号，按客户、表名、表头相似度、最近确认时间和使用次数为后续识别加权。
- LLM 结构裁决预算优先分配给最有价值的灰区候选表，而不是简单按表格遍历顺序消耗。
- 前端确认卡展示推荐导入、可选确认、建议跳过分组，并明确 NeedConfirm 或 Skip 的原因。

## Non-Goals
- 不追求所有表格自动采用。
- 不把所有表格都交给 LLM。
- 不在第一版引入 Embedding 作为结构案例检索的强依赖。
- 不改变现有手动配置兜底流程。

## Impact
- Affected specs: `api`, `matching-engine`, `data-storage`, `user-interface`
- Affected code:
  - `SmartConfigurationAppService`
  - `SmartConfigurationRecognizeModels`
  - `SmartConfigurationRecognizedTableFactory`
  - `DocumentTemplateAppService`
  - `DocumentTemplate` / 可能新增结构案例字段或轻量实体
  - `SmartConfigController`
  - `web/src/api/smart-config`
  - `SmartStructureConfirmCard.vue`
  - data-import / smart-fill 智能识别流程
