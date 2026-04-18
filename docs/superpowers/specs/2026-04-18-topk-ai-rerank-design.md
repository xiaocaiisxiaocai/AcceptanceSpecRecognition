# TopK AI 重排设计

## 背景

当前智能填充主链路已经收敛为：

1. `项目 + 规格` 精确一致时直接命中
2. 否则执行 `Embedding TopK` 召回
3. 本地根据 `Embedding / 项目 / 规格文本 / 最小证据` 选出当前 Top1
4. 仅对当前 Top1 执行 `AI 等价裁决`

在去掉本地品牌、单位、反义词、数值规则后，系统更依赖 `Embedding + AI`。这会带来一个新问题：如果更合适的候选已经进入 TopK，但 `Embedding` 略低于另一个候选，当前链路会先把较差候选选成 Top1，再只对它做 AI 等价裁决，导致真正更合适的候选没有机会被 AI 选中。

典型场景：

- 源项：`宽度小于0.5cm`
- 候选 A：`宽度等于0.7cm`，Embedding 更高
- 候选 B：`宽度等于0.2cm`，Embedding 略低但语义更符合

## 目标

- 让 AI 有机会在已召回的 TopK 候选中改选最佳候选
- 保留现有“当前最佳候选 AI 等价裁决门禁”
- 不影响 `项目 + 规格` 精确一致直达
- AI 重排失败时稳定回退到当前本地 Top1，不阻断预览和填充

## 非目标

- 不改动 Embedding 召回方式
- 不恢复本地品牌、单位、反义词、数值硬编码规则
- 不把 LLM 变成全量候选主排序器

## 方案

### 1. 触发条件

仅在以下条件同时满足时触发 TopK AI 重排：

- 本行不是“项目 + 规格精确一致直达”
- `RecallTopK` 召回后实际候选数大于 1
- 已配置可用的 LLM 等价能力服务

### 2. 后端新链路

非精确命中行改为：

1. `Embedding TopK` 召回
2. 为召回候选生成本地证据和基础分
3. 调用新的 `TopK AI 重排`，让 AI 在候选集中选出最佳 `SpecId`
4. 若 AI 返回有效候选，则将其提升为当前最佳；否则回退本地 Top1
5. 对“最终被选中的当前最佳”继续执行现有 `AI 等价裁决`
6. 按现有 `decision` 规则生成结果

### 3. Prompt 契约

新增独立 Prompt 场景，例如 `MatchingTopKSelection`。

输入包含：

- 源项目、源规格
- 当前本地 Top1 的 `SpecId`
- TopK 候选列表
- 每个候选的 `SpecId / 项目 / 规格 / Embedding / Final / 证据摘要 / 冲突摘要`

输出严格 JSON：

```json
{
  "selectedSpecId": 2,
  "reason": "候选 2 与源项在边界条件上更一致",
  "confidence": 0.91
}
```

约束：

- `selectedSpecId` 必须来自候选列表
- 无法确认时允许返回当前本地 Top1
- 解析失败、越界 `SpecId`、调用失败时一律回退本地 Top1

### 4. 结果模型与前端展示

新增结果元数据：

- `selectionMode`
  - `exactShortcut`
  - `embeddingTop1`
  - `aiRerank`
- `selectionSummary`

用于区分：

- 100% 直达
- 本地 Top1 直接沿用
- AI 从 TopK 中改选

详情页展示上：

- 最佳匹配区增加“命中方式/选中方式”标签
- 候选列表对 AI 改选的 Top1 显示“AI 改选”
- 100% 精确直达不再伪装成普通 AI 结果

### 5. 回退策略

- AI 重排未配置：沿用本地 Top1
- AI 重排调用失败：沿用本地 Top1
- AI 重排返回非法 `SpecId`：沿用本地 Top1
- 后续 AI 等价裁决失败：保持当前已有的 `uncertain -> manualReview` 回退

## 测试

### Core

- TopK 中本地 Top1 不合理时，AI 可改选 Top2
- AI 重排失败时回退本地 Top1
- 精确一致直达时不触发 AI 重排
- AI 改选后，等价裁决应针对 AI 选中的候选执行

### API

- 预览接口返回 `selectionMode` / `selectionSummary`
- AI 改选场景下，`bestMatch.specId` 与本地 Top1 不同

### Frontend

- 详情页能区分 `exactShortcut` 与 `aiRerank`
- 候选卡片能显示 AI 改选摘要
