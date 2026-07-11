## Context
归档分支 `origin/feat/smart-auto-configuration` 已有规则识别骨架，但其交互目标仍是给旧向导自动填参数，未减少步骤。本变更复用可用的规则策略和表头检测思路，重做为上传后全文档逐表识别。

## Goals / Non-Goals
- Goals: 上传并选择客户/制程/机型后自动识别表格、表头、数据范围和项目/规格/验收/备注列。
- Goals: 高置信表直接进入导入预览或智能填充匹配，低置信表只显示确认卡。
- Goals: 用户确认后沉淀客户模板和客户域学习词。
- Non-Goals: 不新增 Embedding 结构识别层。
- Non-Goals: 不在本变更中重写现有 Word/Excel 导入接口必填契约。

## Decisions
- Decision: 识别响应使用扁平 `tables`，沿用现有 `TableInfo(Index + Name)`；不引入 Sheet/Tables 二级 DTO。
- Decision: 识别内部统一使用解析后 `TableData` 的 0-based 相对索引；Excel 调用现有导入接口前转换为 1-based 工作表绝对坐标。
- Decision: LLM 只做结构裁决，且 AutoApply 前必须通过确定性体检。
- Decision: 工作流编排放入 Application 用例服务，API 控制器只做 HTTP 适配。
- Decision: 数据导入保留现有导入接口，识别结果不满足必填列时进入确认或高级模式。

## Risks / Trade-offs
- 风险：Excel 相对/绝对索引转换错误会导致导入错列。缓解：API 与前端测试覆盖 `UsedRangeStartRow/Column` 非 1 的样例。
- 风险：前端导入页当前 5 步状态机按索引组织。缓解：将 `useDataImportPage`、Pinia store、目标加载和批量导入构造作为同一任务重排。
- 风险：客户域学习词只写不读。缓解：`effective?customerId=`、前端 API 与导入/填充调用点同步传客户。
- 风险：同一文档混合仅规格和项目+规格表。缓解：智能填充拆请求，或另行改造后端支持表级 `MatchingMode`。

## Migration Plan
1. 新增 `DocumentTemplate` 表。
2. 扩展 `ColumnMappingRule`，增加 `Source` 和 `CustomerId`。
3. 新增 Prompt 模板场景 `SmartConfigStructureRecognition`。
4. 保留现有导入与智能填充手动流程作为兜底。

## Open Questions
- 是否将 `recognize` 权限归入现有文档导入权限，还是作为 `api:smart-config:create` 单独授权。
