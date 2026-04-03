## Context
当前前端已有复杂工作流页面和高信息密度弹窗，但实现边界偏粗。`data-import/index.vue` 集中了步骤导航、上传、映射、目标选择、确认导入、差异确认和导入执行等多种职责；`ScoreDetailDialog.vue` 则同时持有弹窗状态、格式化逻辑、diff 计算和大块模板。需求不是改交互，而是在不改变现有行为和视觉的前提下，把这些边界重新切开。

## Goals / Non-Goals
- Goals:
  - 降低 `data-import/index.vue` 与 `ScoreDetailDialog.vue` 的单文件复杂度
  - 将页面编排、局部展示和纯派生逻辑拆到明确边界
  - 让后续新增字段、调整局部交互或补测试时不必反复阅读整页巨型文件
  - 保持现有用户流程、视觉结构、文案和 API 行为不变
- Non-Goals:
  - 不引入新的 store、状态机或跨页面共享状态模型
  - 不调整现有交互顺序、信息层次或样式类名
  - 不改接口契约、权限语义或业务校验规则

## Decisions
- Decision: 对高复杂度视图采用“编排壳 + 受控子组件 + 本地 composable”模式
  - `data-import/index.vue` 保留步骤导航、顶层状态装配和跨步骤动作
  - 各步骤面板与差异确认弹窗拆为受控组件，只消费 props 并通过 emits 触发动作
  - 导入映射、待导入预览与导入执行聚合逻辑拆为本地 composable
- Decision: `ScoreDetailDialog.vue` 退化为弹窗壳
  - 弹窗壳只保留 `visible/item` 桥接与顶层组合
  - 最佳匹配、差异对照、候选卡片列表拆为稳定区块组件
  - diff 派生、候选比较和格式化工具统一沉到本地 composable / helper
- Decision: 迁移顺序采用低风险批次推进
  - 批次 A：先抽纯类型和纯函数
  - 批次 B：拆 `ScoreDetailDialog`
  - 批次 C：拆 `DataImportStepConfirm` 与 `DataImportDifferenceDialog`
  - 批次 D：拆其余步骤面板并收敛页面壳

## Alternatives Considered
- 只抽 composable，不拆展示组件
  - 优点是改动更小
  - 缺点是大模板仍然堆在单文件里，阅读成本下降有限
- 直接引入 store 或状态机
  - 优点是状态模型更显式
  - 缺点是本轮目标只是降复杂度且零行为变更，这个方案引入了不必要的迁移风险

## Risks / Trade-offs
- props / emits 可能在页面拆分后变长
  - 通过 composable 聚合派生状态，避免逐层透传临时值
- 模板拆分可能导致细微 DOM 顺序变化
  - 明确要求保持模板片段顺序、class 名和文案不变
- 差异确认和候选对比逻辑拆分后可能出现状态不同步
  - 相关状态继续保持单一来源，只把展示分发给子组件

## Migration Plan
1. 先抽离 `dataImport.types.ts`、`dataImport.helpers.ts`、`scoreDetail.formatters.ts`
2. 再拆 `ScoreDetailDialog` 的三个展示区块与 `useScoreDetailDiff`
3. 然后拆数据导入确认区与差异确认弹窗
4. 最后拆上传、表格选择、映射配置和目标选择步骤面板
5. 每个批次后执行 `pnpm typecheck`、`pnpm build` 和最小结构回归测试

## Open Questions
- 前端结构回归测试最终放在 `web/tests` 还是沿用现有源码断言测试方式，需要结合仓库现有前端测试基础再落定。
