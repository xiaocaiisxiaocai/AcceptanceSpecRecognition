## MODIFIED Requirements
### Requirement: 数据导入界面
系统 SHALL 提供文档上传与导入的Web界面。

#### Scenario: 文件上传
- **WHEN** 用户点击上传按钮并选择文档
- **THEN** 系统上传文件并显示文件信息

#### Scenario: 表格预览
- **WHEN** 文件上传成功
- **THEN** 系统显示文档中表格列表与预览内容

#### Scenario: Word 列映射规则自动预填
- **WHEN** 用户上传 Word 文件并选择一个或多个表格进入配置映射步骤
- **THEN** 系统基于列映射规则自动预填项目、规格、验收和备注列
- **AND** 用户仍可逐表手动调整

#### Scenario: Excel 不使用列映射规则
- **WHEN** 用户上传 Excel 文件进入配置映射步骤
- **THEN** 系统不套用列映射规则
- **AND** 用户按工作表手动配置列序号

#### Scenario: 列映射配置
- **WHEN** 用户选择目标表格
- **THEN** 系统允许配置项目、规格、验收、备注列

#### Scenario: 客户制程选择
- **WHEN** 用户完成列映射配置
- **THEN** 系统提供客户与制程选择器

#### Scenario: 导入前剔除数据
- **WHEN** 用户进入确认导入步骤
- **THEN** 系统显示本次待导入数据，并允许用户单个删除或批量删除不需要导入的行

#### Scenario: 导入冲突模态框确认
- **WHEN** 系统检测到数据库已有相同项目与规格但内容不一致的数据
- **THEN** 系统弹出模态框，并按左右两侧分别展示数据库已有数据与本次待导入数据供用户逐条确认是否覆盖

#### Scenario: 导入确认
- **WHEN** 用户完成冲突确认并点击导入按钮
- **THEN** 系统仅导入用户保留且确认覆盖的行，并提示结果

### Requirement: 智能填充界面
系统 SHALL 提供基于统一多阶段匹配引擎的智能匹配与文档填充界面。

#### Scenario: Word 表格自动预填列索引
- **WHEN** 用户上传 Word 文件进入“选择表格”步骤
- **THEN** 系统基于列映射规则自动预填项目列、规格列、验收列和备注列
- **AND** 用户仍可逐表调整

#### Scenario: Excel 表格不自动套用规则
- **WHEN** 用户上传 Excel 文件进入“选择表格”步骤
- **THEN** 系统不套用列映射规则
- **AND** 用户继续按工作表手动配置列索引

#### Scenario: 匹配参数配置
- **WHEN** 用户进入匹配配置步骤
- **THEN** 系统提供 Embedding 服务、LLM 服务、高置信阈值、候选过滤阈值、实体判别参数与复核并行参数
- **AND** AI 复核门禁固定开启，不再提供 `SingleStage / MultiStage` 或 `LLM 复核开关` 之类的策略切换
- **AND** 页面不再展示 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows` 等旧兼容项

#### Scenario: 匹配预览
- **WHEN** 用户执行预览
- **THEN** 系统显示每行的匹配结果、最终决策状态、高置信状态与人工确认状态
- **AND** 对无法自动采用的样本给出明确提示
- **AND** 主表与详情以服务端返回的 `decision`、`confidenceLevel`、证据与 AI 裁决结果为准，不再由前端旧兼容字段自行推断

#### Scenario: 详情弹窗
- **WHEN** 用户查看匹配详情
- **THEN** 系统弹窗展示候选匹配列表、关键证据、冲突状态、歧义状态与复核结果

#### Scenario: LLM复核
- **WHEN** 系统处理高歧义样本
- **THEN** 系统流式返回复核进度
- **AND** 页面根据 `review.start`、`review.delta`、`review.done`、`review.error` 更新逐行状态
- **AND** 以 `stream.complete` 作为本次流式会话结束信号
- **AND** 仅对通过门禁的结果允许自动采用

#### Scenario: 执行填充
- **WHEN** 用户确认匹配结果并点击填充
- **THEN** 系统执行填充；Word 提供结果文件下载，Excel 写回源文件后提供下载入口
- **AND** 对需要人工确认但未确认的样本阻止直接自动采用
- **AND** 页面只提交当前文件中的 `SpecId` 与人工确认状态，不再透传旧 suggestion / compatibility 字段

### Requirement: 配置管理界面
系统 SHALL 提供 AI 服务、Prompt 模板与列映射规则的 Web 配置页面。

#### Scenario: AI服务配置
- **WHEN** 用户访问AI配置页面
- **THEN** 系统显示AI服务列表并支持新增、编辑、删除

#### Scenario: 连接测试
- **WHEN** 用户点击测试连接按钮
- **THEN** 系统测试AI服务连接并显示结果

#### Scenario: Prompt 模板配置
- **WHEN** 用户访问 Prompt 模板页面
- **THEN** 系统显示系统模板场景列表并支持编辑、预览测试与恢复默认
- **AND** 页面不再以“设为默认模板”作为主操作

#### Scenario: 列映射规则配置
- **WHEN** 用户访问列映射规则页面
- **THEN** 系统显示列映射规则列表并支持新增、编辑、删除
