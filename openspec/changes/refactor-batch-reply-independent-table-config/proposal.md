# Change: 重构批量回复为步骤化文件工作区

## Why
当前批量回复虽然已经拆成“来源配置 / 目标配置 / 执行结果”三个根 Tab，但页面内部仍然主要依赖表格卡片展开配置，并额外保留了独立的“当前表回写预览”区域。这和用户确认的目标交互仍有偏差：用户要的是“每一步一个 Tab，每个 Excel/Word 一个 Tab，文件 Tab 下再分 Sheet/表格 Tab”，且不再保留单独的预检查区域。

现有实现的问题不在于接口不可用，而在于交互层级仍然不清晰。用户在目标文件中很难一眼分辨“我现在正在配置哪一个文件的哪一张表”，同时独立预检查区也重新引入了额外的上下文切换成本。

## What Changes
- 将批量回复重构为真正的步骤式工作区：`来源文件 -> 目标文件 -> 执行结果`。
- 在来源文件步骤中引入“文件 Tab -> Sheet/表格 Tab”结构，每个 Sheet/表格内直接配置行设置与列映射。
- 在目标文件步骤中引入“目标文件 Tab -> Sheet/表格 Tab”结构，每个目标表可显式选择来源表并直接配置行设置与列映射。
- 移除页面内独立的预检查/预览结果区域，把预览入口与反馈收拢到当前 Sheet/表格上下文内。
- 保留现有逐表预览接口与执行契约，执行前仅按当前配置做最小必要校验，不再要求用户经过独立预检查步骤。
- 保持写回范围仅限验收列与备注列；匹配仍以 `项目 + 规格` 为键，允许乱序，不允许重复键自动处理。

## Impact
- Affected specs: `user-interface`
- Affected code:
  - `web/src/api/matching.ts`
  - `web/src/views/batch-reply/index.vue`
  - `web/src/views/smart-fill/components/BatchTableConfig.vue`
  - `web/src/views/data-import/components/TablePreview.vue`
  - `web/tests/batch-reply-*.test.ts`
  - `tests/AcceptanceSpecSystem.Api.Tests/*BatchReply*`
  - `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
