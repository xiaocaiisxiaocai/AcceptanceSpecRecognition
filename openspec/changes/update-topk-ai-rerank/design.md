## Context

当前服务端链路是“Embedding TopK 召回 -> 本地选 Top1 -> Top1 等价裁决”。在去掉本地硬编码判别规则后，链路中的“谁先成为 Top1”这一点变得更关键。

## Goals / Non-Goals

- Goals:
  - 让 AI 在已召回的 TopK 中重新选出当前最佳
  - 保持精确一致直达和现有等价裁决门禁
  - 失败时稳定回退，不影响批量预览
- Non-Goals:
  - 不改 Embedding 召回算法
  - 不恢复本地品牌/单位/反义词/数值规则
  - 不引入全量候选 AI 排序

## Decisions

- Decision: 仅在 `TopK > 1` 且非精确一致直达时触发 AI 重排
  - Alternatives considered:
    - 所有行都触发：成本过高
    - 只对高歧义触发：会漏掉“Embedding 第一名明显不合理但分差不小”的场景

- Decision: 使用单独 Prompt 场景让 AI 从候选集合中选择 `selectedSpecId`
  - Alternatives considered:
    - 复用逐候选等价裁决：调用次数多，且缺少候选间横向比较

- Decision: AI 改选后仍执行现有等价裁决门禁
  - Alternatives considered:
    - AI 改选直接自动采用：风险过高

## Risks / Trade-offs

- 增加一次 LLM 调用，预览耗时会比当前略高
- Prompt 输出若不稳定，会走本地 Top1 回退
- 需要新增结果元数据，前后端都要同步

## Migration Plan

1. 先补 Core 红灯测试
2. 再实现 TopK AI 重排与模型
3. 最后补 API / UI 字段与展示

## Open Questions

- 暂无，当前按“只要 TopK>1 就触发 AI 重排”的批准方案实现
