# Change: 优化智能填充 AI 复核性能

## Why
智能填充预览当前默认在同步匹配阶段对大量候选执行 AI 等价裁决，慢模型或批量文档场景下会明显拖慢预览反馈。

## What Changes
- 同步匹配阶段的 AI 等价裁决默认关闭。
- 匹配配置增加显式开关，用户需要精度优先时可手动开启。
- 关闭同步 AI 等价裁决时，预览仍保留 Embedding 召回、本地证据判断、人工确认和后续 LLM 复核能力。

## Impact
- Affected specs: `matching-engine`, `api`, `user-interface`
- Affected code: 匹配配置 DTO、匹配配置转换、智能填充配置页、匹配配置类型
