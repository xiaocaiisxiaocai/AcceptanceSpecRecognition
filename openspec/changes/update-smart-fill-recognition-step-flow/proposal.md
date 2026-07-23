# Change: 智能填充识别完成后进入独立确认步骤

## Why
当前智能填充把上传归属、AI 辅助设置、识别摘要和逐 Sheet 确认卡全部堆叠在第一个步骤中。识别完成后页面仍停留在“上传/归属”，关键的“确认并学习”操作可能落在首屏之外，而底部只显示被禁用的“请先确认列配置”，容易让用户误以为识别卡死。

## What Changes
- 将智能填充默认流程从三步调整为四步：`上传/归属 → 识别确认 → 匹配配置 → 预览确认`。
- 第一页只负责上传文件、选择归属、配置 AI 辅助并发起识别；识别成功后自动进入“识别确认”。
- “识别确认”页集中展示识别摘要、Sheet 选择、区域问题、范围调整和确认学习操作，不再重复展示上传与归属表单。
- 待确认 Sheet 的主操作和具体阻断原因保持在当前 Sheet 的可见操作区；全部选中 Sheet 确认完成后才能进入匹配配置。
- 识别失败时保留在上传页并提供明确重试；从确认页返回时保留文件、归属和识别结果，只有文件或归属变化才使旧结果失效。
- 保留现有手动高级配置兜底、匹配预览和执行链路，不修改智能识别、确认学习或匹配 API。

## Impact
- Affected specs: `user-interface`
- Affected code: `web/src/views/smart-fill/index.vue`, `web/src/views/smart-fill/smartFill.smartRecognition.ts`, 智能结构确认共享组件及相关测试
- Related changes: `add-multi-region-smart-recognition`, `fix-upload-recognition-service-selection`
- API / data impact: 无 API、数据库或历史数据变更
