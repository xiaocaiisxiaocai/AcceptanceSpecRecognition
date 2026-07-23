# Change: 合并所选 Sheet 的确认学习与开始导入

## Why

当前确认页按 Sheet 展示结构卡片，多 Sheet 文件仍要求用户逐页点击“确认并学习”，最后再点击“开始导入”。这些动作属于同一个文件级导入意图，重复操作增加遗漏风险，也让用户难以判断哪些 Sheet 已完成确认。

## What Changes

- 数据导入确认页仅展示一个文件级主操作“确认所选 Sheet、学习并开始导入”，不再在每个 Sheet 卡片内重复展示确认按钮。
- 单次操作按工作表顺序确认所有已勾选且需要确认的 Sheet；已经自动采用的 Sheet 不重复学习，未勾选的 Sheet 不学习、不导入。
- 用户在各 Sheet 中调整的范围和列映射必须作为草稿保留，文件级操作提交各 Sheet 的最终草稿，而不是回退到初始识别结果。
- 文件级操作必须等待全部目标 Sheet 确认与学习成功，再统一刷新导入配置、加载完整预览并正式导入。
- 任一 Sheet 配置不完整、确认失败或配置刷新失败时停止流程并定位失败 Sheet，不得继续正式导入。
- 批量执行期间提供当前进度并锁定重复操作；已成功学习的 Sheet 允许安全重试。
- 智能填充继续使用“确认并学习”，不触发数据导入。

## Impact

- Affected specs: user-interface
- Affected code:
    - web/src/views/shared/SmartStructureConfirmCard.vue
    - web/src/views/shared/SmartStructureConfirmTabs.vue
    - web/src/views/data-import/index.vue
    - web/src/views/data-import/components/DataImportConfirmPanel.vue
    - web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts
    - 前端回归测试
