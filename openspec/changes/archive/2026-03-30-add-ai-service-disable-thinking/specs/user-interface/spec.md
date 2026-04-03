## MODIFIED Requirements
### Requirement: 配置管理界面
系统 SHALL 提供AI服务与文本处理的Web配置页面。

#### Scenario: AI服务配置
- **WHEN** 用户访问AI配置页面
- **THEN** 系统显示AI服务列表并支持新增、编辑、删除

#### Scenario: 配置关闭思考模式
- **WHEN** 用户编辑 Ollama 的 LLM 服务配置
- **THEN** 系统允许设置“关闭思考模式”开关并保存

#### Scenario: 连接测试
- **WHEN** 用户点击测试连接按钮
- **THEN** 系统测试AI服务连接并显示结果

#### Scenario: 文本处理配置
- **WHEN** 用户访问文本处理配置页面
- **THEN** 系统显示简繁转换、同义词、OK/NG等配置选项
