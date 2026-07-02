## ADDED Requirements

### Requirement: 智能结构识别 API
系统 SHALL 提供智能结构识别 API，用于对已上传 Word 或 Excel 文件输出全文档表格结构识别结果。

#### Scenario: 识别返回扁平表格结构
- **GIVEN** 用户已上传 Word 或 Excel 文件
- **AND** 用户已选择客户
- **WHEN** 前端调用 `POST /api/smart-config/recognize`
- **THEN** 响应包含数字类型 `fileId`
- **AND** 响应包含扁平 `tables` 数组
- **AND** 每个表包含 `tableIndex`、`tableName`、`headers`、表头行、数据范围、四列识别结果、字段来源、字段置信度和决策状态
- **AND** 响应不使用 Sheet/Tables 二级结构

#### Scenario: Excel 索引口径清晰
- **GIVEN** 识别结果来自 Excel 文件
- **WHEN** API 返回行列索引
- **THEN** 识别结果中的行列索引使用解析后表格的 0-based 相对索引
- **AND** 调用现有 Excel 导入接口前必须转换为 1-based 工作表绝对坐标

#### Scenario: 识别失败可降级
- **GIVEN** 识别过程中 LLM 超时、解析失败或服务异常
- **WHEN** 系统构造识别响应
- **THEN** 系统返回需要确认或失败状态
- **AND** 不阻断用户进入现有手动配置流程

### Requirement: 智能结构确认 API
系统 SHALL 提供智能结构确认 API，用于接收用户确认后的最终配置并触发模板与学习词沉淀。

#### Scenario: 确认后沉淀学习结果
- **GIVEN** 用户在确认卡或预览页确认识别结果
- **WHEN** 前端调用 `POST /api/smart-config/confirm`
- **THEN** 系统保存或更新客户级结构模板
- **AND** 系统为用户修正过的列写入客户域学习词
- **AND** 响应返回学习是否成功

#### Scenario: 学习失败不阻断当前流程
- **GIVEN** 当前业务导入或填充配置已经确认
- **WHEN** 模板或学习词沉淀失败
- **THEN** API 记录失败日志
- **AND** 不要求当前业务流程失败

### Requirement: 智能结构识别 API 权限受控
系统 MUST 对智能结构识别与确认 API 执行权限校验。

#### Scenario: 缺少权限被拒绝
- **WHEN** 已登录用户缺少智能结构识别或文档导入相关 API 权限
- **THEN** 系统返回 403
- **AND** 响应包含缺少的权限码
