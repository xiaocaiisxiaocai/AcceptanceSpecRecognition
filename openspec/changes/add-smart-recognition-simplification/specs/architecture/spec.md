## ADDED Requirements

### Requirement: 智能结构识别遵守应用层编排边界
系统 MUST 将智能结构识别的跨资源编排放入 Application 层，而不是由控制器直接编排 Core、Data 和文件解析细节。

#### Scenario: 控制器只做协议适配
- **WHEN** 客户端调用智能结构识别或确认 API
- **THEN** 控制器只负责请求接收、权限上下文传递和响应包装
- **AND** 控制器委派 Application 用例服务完成文档解析、识别、模板命中和学习沉淀

#### Scenario: Core 保持纯算法职责
- **WHEN** 系统执行表格结构识别算法
- **THEN** Core 层只处理表格数据、规则策略、LLM 结构裁决接口和确定性体检
- **AND** Core 层不引用 API、Application 或 Data 类型

#### Scenario: Data 保持纯持久化职责
- **WHEN** 系统保存模板或列映射学习词
- **THEN** Data 层只提供实体、仓储和迁移
- **AND** Data 层不实现 Core 业务接口或 Application 用例服务
