## 1. Specification
- [x] 1.1 校验 OpenSpec 提案：`openspec validate add-smart-recognition-adaptive-routing --strict`。
- [x] 1.2 获得实施批准后再修改业务源码。

## 2. Core / Application
- [x] 2.1 新增表格类型、推荐级别、识别问题、排序分模型。
- [x] 2.2 实现外置表格路由规则匹配，支持表名、表头、样例行和任意位置匹配，不在后端硬编码客户业务词。
- [x] 2.3 在智能结构识别链路中合并类型分、字段映射分、健康检查问题和历史案例分。
- [x] 2.4 调整 LLM 结构裁决预算分配，优先处理高价值灰区候选表。
- [x] 2.5 保持 AutoApply 门禁不放宽，Skip 仅作为建议。
- [x] 2.6 无外置路由规则时，不因报价、Layout、Utility 等业务词硬编码跳过表格。

## 3. Learning / Data
- [x] 3.1 扩展结构模板或新增轻量结构案例信号，记录表名、表类型、推荐结果、确认时间、使用次数和用户是否修正。
- [x] 3.2 实现历史案例相似度与权重计算：客户优先、表名/表头相似、近期和高频加权。
- [x] 3.3 用户确认后更新结构案例信号，不污染全局规则。
- [x] 3.4 新增智能结构路由规则表、仓储和迁移，支持人工、学习、AI 建议来源。
- [x] 3.5 用户确认后沉淀客户级学习路由规则，不自动提升为全局规则。

## 4. API
- [x] 4.1 扩展 `/api/smart-config/recognize` 响应字段：`tableKind`、`recommendation`、`rankingScore`、`issues`、`skipReason`。
- [x] 4.2 保持旧字段兼容，既有前端流程不因新增字段失效。
- [x] 4.3 补充 API 测试：混合 Excel 返回推荐、可选、跳过表。
- [x] 4.4 新增 `/api/smart-structure-routing-rules` 查询、新增、更新、删除和客户有效规则 API。
- [x] 4.5 补充 API 测试：无路由规则时不按硬编码业务词跳过，确认后写入客户级学习规则。

## 5. Frontend
- [x] 5.1 确认卡按推荐导入、需要确认、建议跳过分组展示。
- [x] 5.2 展示 NeedConfirm / Skip 具体原因。
- [x] 5.3 建议跳过表默认折叠，但允许用户展开并手动改为导入。
- [x] 5.4 data-import 与 smart-fill 共用展示逻辑。
- [x] 5.5 新增智能结构路由规则配置页，支持人员手写、启停、客户域和学习规则维护。

## 6. Verification
- [x] 6.1 运行智能结构识别相关 Core/API 测试。
- [x] 6.2 运行前端相关测试和类型检查。
- [x] 6.3 使用真实 PA06 Excel 与太阳式翻板暂存机 Word 跑完整上传 + 识别回归。
- [x] 6.4 运行 `dotnet test AcceptanceSpecSystem.sln --no-restore`。
