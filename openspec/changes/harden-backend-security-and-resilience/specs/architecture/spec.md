## ADDED Requirements

### Requirement: Application 与 Data 异步操作贯穿取消信号

系统 MUST 将请求或宿主取消信号贯穿 Application、Repository、EF 查询、事务与支持取消的外部操作。

#### Scenario: 客户端取消数据库请求

- **WHEN** 客户端取消仍在执行或等待的业务请求
- **THEN** Application 将取消信号传递给 Repository、EF 查询和事务调用
- **AND** 不继续启动新的数据库或外部服务子任务

#### Scenario: 补偿操作脱离请求取消

- **WHEN** 系统必须在请求取消后完成资源释放或回滚
- **THEN** 该补偿使用明确的独立生命周期 token
- **AND** 代码和测试说明其不会继续提交原业务成功结果

### Requirement: 运行日志不得保存 AI 与客户业务原文

系统 MUST 确保常规和 Debug 运行日志不包含完整 Prompt、项目、规格、验收正文、模型自由文本或其他客户业务原文。

#### Scenario: 记录 AI 调用诊断

- **WHEN** 系统记录 LLM 复核、裁决、重排或结构识别诊断
- **THEN** 日志只包含场景、长度、摘要、结构化结果、耗时和 traceId 等脱敏元数据
- **AND** 不提供通过普通日志级别开关输出完整 Prompt 的旁路

### Requirement: 外部进程取消与产物发布保持原子

系统 MUST 在取消或失败时终止由请求或后台任务启动的外部进程并清理未完成产物，成功产物只能在完整生成后原子发布。

#### Scenario: 数据库备份被取消

- **WHEN** 备份执行期间收到取消信号
- **THEN** 系统终止整个备份进程树并等待退出
- **AND** 删除本次未完成的 partial 文件
- **AND** 不生成看似成功的正式备份记录

#### Scenario: 数据库备份成功

- **WHEN** 备份进程成功退出且压缩流完整关闭
- **THEN** 系统在同一目录把 partial 文件原子发布为正式备份
- **AND** 保留策略只处理正式备份文件
