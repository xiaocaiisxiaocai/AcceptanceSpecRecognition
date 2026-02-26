## MODIFIED Requirements

### Requirement: 智能填充界面
系统 SHALL 提供智能匹配与文档填充的Web界面。

#### Scenario: 目标文件上传
- **WHEN** 用户上传需要填充的Word文档
- **THEN** 系统解析文档并显示表格列表

#### Scenario: 匹配参数配置
- **WHEN** 用户进入匹配配置步骤
- **THEN** 系统提供 Embedding 服务/模型选择、LLM 服务/模型选择与阈值配置
- **AND** 不再提供传统相似度算法与权重配置

#### Scenario: 匹配预览
- **WHEN** 用户执行预览
- **THEN** 系统显示每行的最佳匹配、Embedding 得分与 LLM 复核评分/理由
- **AND** LLM 复核内容以流式方式实时展示
- **AND** 无匹配时显示明确原因
- **AND** 低置信度/无匹配时展示 LLM 生成建议（流式）

#### Scenario: 详情弹窗
- **WHEN** 用户查看匹配详情
- **THEN** 系统弹窗展示最佳匹配详情、Embedding 得分与 LLM 复核理由
- **AND** 不展示候选列表

#### Scenario: 执行填充
- **WHEN** 用户确认匹配结果并点击填充
- **THEN** 系统执行填充并提供下载入口

---

### Requirement: 配置管理界面
系统 SHALL 提供AI服务与文本处理的Web配置页面。

#### Scenario: AI服务配置
- **WHEN** 用户访问AI配置页面
- **THEN** 系统显示AI服务列表并支持新增、编辑、删除
- **AND** 每个服务可配置用途（LLM/Embedding）、服务类型（OpenAI/Azure/Ollama/LM Studio/自定义）与优先级

#### Scenario: 连接测试
- **WHEN** 用户点击测试连接按钮
- **THEN** 系统通过 SK 连接器测试AI服务并显示结果

#### Scenario: 文本处理配置
- **WHEN** 用户访问文本处理配置页面
- **THEN** 系统显示简繁转换、同义词、OK/NG等配置选项
