## 1. Specification Gate
- [x] 1.1 校验本 change：`openspec validate add-smart-recognition-simplification --strict`。
- [x] 1.2 获得方案批准后再开始业务源码实现。

## 2. Backend Recognition
- [x] 2.1 新增 Core 文档结构识别模型、规则融合、确定性体检和 LLM 结构裁决。
- [x] 2.2 在 Application 层实现智能结构识别用例服务。
- [x] 2.3 新增 `SmartConfigController` 的 `recognize` 和 `confirm` 动作。
- [x] 2.4 接入 Prompt 模板场景、权限、DI 与架构边界测试。

## 3. Data Learning
- [x] 3.1 新增客户级 `DocumentTemplate` 存储与仓储。
- [x] 3.2 扩展 `ColumnMappingRule` 的来源和客户域。
- [x] 3.3 实现确认后模板 upsert、学习词写入和全局升级。

## 4. Frontend Flow
- [x] 4.1 新增智能识别 API 封装、composable、摘要横幅和确认卡。
- [x] 4.2 重排数据导入 5 步状态机为上传/目标、确认/预览、完成，并保留高级手动配置。
- [x] 4.3 重排智能填充上传与归属选择，识别后自动组装预览配置。

## 5. Verification
- [x] 5.1 后端运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`。
- [x] 5.2 前端运行 `cd web && pnpm typecheck && pnpm test && pnpm build`。
- [x] 5.3 使用真实 Word/Excel 样本验证直达、确认卡和高级模式兜底。
