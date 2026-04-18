# 智能填充预览页内编辑导出覆盖值设计

## 背景
- 当前智能填充预览页只能选择“采用哪条匹配结果”，不能在执行前直接修订 `验收标准` 与 `备注`。
- 客户如果只想对少量匹配结果做落地微调，只能先导出文档，再在导出文件里二次修改，流程割裂。
- 现有执行链路只上传 `specId`、人工确认状态与复核放行令牌，后端写回时始终使用规格库中的原始 `Acceptance/Remark`。

## 目标
- 允许用户在智能填充预览页直接编辑单行的 `验收标准` 与 `备注`。
- 编辑结果只影响本次智能填充导出，不回写验收规格主数据。
- 用户点击 `保存并采用` 后，该行自动视为“已选择填充”。
- 预览表要能显式区分“原匹配值”和“本次已编辑值”。

## 非目标
- 不支持在“无匹配”或“拒绝填充”行上手工录入全新验收内容。
- 不把编辑能力混入现有“匹配详情”弹窗，避免职责混杂。
- 不新增数据库表，也不改动验收规格维护页。

## 方案
### 前端
- 在 `MatchPreviewTable.vue` 的操作列新增 `编辑` 按钮，仅对存在 `bestMatch` 的行显示。
- 新增独立编辑弹窗组件，展示四行字段：
  - `项目`：只读
  - `规格`：只读
  - `验收标准`：可编辑
  - `备注`：可编辑
- 弹窗主按钮使用 `保存并采用`：
  - 保存当前覆盖值
  - 自动把该行加入已选择填充集合
- 预览表中的 `验收标准`、`备注` 列优先显示覆盖值，并展示轻量 `已编辑` 标记。
- 用户点击 `不填充` 时，仅取消本行采用状态，不清空覆盖值。
- 重新执行一次预览时，整批覆盖草稿清空，避免沿用旧匹配上下文。

### 执行链路
- `BatchPreviewTabs` 与 `MatchPreviewTable` 暴露的选择结果增加可选覆盖字段：
  - `overrideAcceptance`
  - `overrideRemark`
- `web/src/views/smart-fill/index.vue` 在 `handleExecute` 中把覆盖值带入 `mappings`。
- `web/src/api/matching.ts` 与 `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs` 同步扩展执行契约。

### 后端
- 批量执行请求中的单行映射支持接收本次导出的覆盖值。
- `MatchingWorkflowService` 构造写回结果时优先使用覆盖值；未提供覆盖值时回退到匹配规格原值。
- `MatchingResultWriteBackService` 保持写单元格逻辑不变，只消费已决议好的写回内容。
- 执行历史详情优先展示本次导出实际写入的覆盖值，避免历史记录和导出内容不一致。

## 风险与取舍
- 风险：用户可能误以为编辑会同步更新规格库。
  - 处理：文案明确说明“仅本次导出使用”。
- 风险：取消采用后保留覆盖草稿，用户可能忘记该行未被选中。
  - 处理：仍以“已选择填充”状态为准，表格保持选择状态显式展示。
- 取舍：不对“无匹配”开放手工填写。
  - 原因：这会把“智能填充修订”扩成“人工录入”，超出本次最小范围。

## 影响文件
- `web/src/views/smart-fill/components/MatchPreviewTable.vue`
- `web/src/views/smart-fill/components/BatchPreviewTabs.vue`
- `web/src/views/smart-fill/index.vue`
- `web/src/api/matching.ts`
- `web/tests/smart-fill-ai-equivalence.test.ts`
- `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
- `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`
- `tests/AcceptanceSpecSystem.Api.Tests/LlmMatchingAssistFillTests.cs`
