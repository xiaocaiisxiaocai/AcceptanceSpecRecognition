## 1. Backend Archive Storage
- [x] 1.1 为 `IFileStorageService` 增加保存/读取执行历史归档的方法，路径限定在 `uploads/execution-history/smart-fill/`。
- [x] 1.2 在 `FileStorageService` 与 `TestFileStorageService` 实现归档保存与读取，优先使用 gzip JSON。
- [x] 1.3 为归档路径增加根目录逃逸防护，复用现有 `GetAbsolutePath` 校验。

## 2. Execution History Persistence
- [x] 2.1 扩展 `ExecutionHistorySmartFillPlaybackDto` 或摘要 DTO，保存完整归档元数据。
- [x] 2.2 在 `ExecutionHistoryAppService.SaveAsync` 中对智能填充完整回放先写外置归档。
- [x] 2.3 将 `DetailJson` 缩减为轻量回放和归档引用，保留现有二级精简作为兜底。
- [x] 2.4 确保列表摘要 `hasPlaybackArchive` 在存在外置归档时为 `true`。

## 3. Read API
- [x] 3.1 在应用服务增加按行读取完整智能填充回放明细的方法。
- [x] 3.2 在 `ExecutionHistoryController` 增加只读接口，并复用执行记录归属校验。
- [x] 3.3 对归档缺失、旧记录、越权记录返回明确错误。

## 4. Frontend
- [x] 4.1 扩展 `web/src/api/execution-history.ts` 类型和 API 方法。
- [x] 4.2 执行记录回放组件打开行详情时按需加载完整明细。
- [x] 4.3 精简归档提示改为说明“列表为轻量视图，详情会按需加载完整匹配信息”。
- [x] 4.4 归档缺失或旧记录时显示降级提示。

## 5. Tests
- [x] 5.1 后端新增大记录测试：保存后 `DetailJson` 不超过阈值且不是 legacy。
- [x] 5.2 后端新增按行读取测试：任意行可返回完整 `bestMatch.topCandidates`、证据、AI 裁决和最终写回值。
- [x] 5.3 前端至少运行 `pnpm typecheck`。
- [x] 5.4 后端运行相关回归测试和 API 项目编译。
