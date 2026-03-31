# Change: 重构前端高复杂度视图边界

## Why
当前 `data-import/index.vue` 与 `ScoreDetailDialog.vue` 同时承载页面编排、派生状态、局部交互和大块展示模板，文件体量与职责密度都已经过高。继续在现状上迭代会放大回归风险，也会显著提高后续 review、测试和功能扩展成本。

## What Changes
- 将数据导入页重构为页面编排壳 + 步骤组件 + 本地 composable 的结构。
- 将匹配详情弹窗重构为弹窗壳 + 最佳匹配区块 + 差异区块 + 候选列表区块。
- 抽离纯类型、纯格式化函数和 diff/比较派生逻辑，减少模板组件中的算法和状态噪音。
- 为结构重构补充最小源码结构回归测试，约束“页面壳持有顶层状态、子组件负责局部展示”的目标边界。
- 保持现有交互流程、文案、视觉和接口契约不变。

## Impact
- Affected specs: `architecture`
- Affected code:
  - `web/src/views/data-import`
  - `web/src/views/smart-fill/components`
  - `web/src/views/smart-fill/composables`
  - `web/tests`（如新增前端结构回归测试）
