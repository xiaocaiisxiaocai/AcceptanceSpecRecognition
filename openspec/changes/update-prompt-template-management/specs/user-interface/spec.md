## ADDED Requirements
### Requirement: Prompt 模板系统化管理界面
系统 SHALL 提供面向系统模板场景的 Prompt 模板管理界面，而不是以默认模板和任意命名为核心语义。

#### Scenario: 查看系统模板列表
- **WHEN** 用户访问 Prompt 模板页面
- **THEN** 页面显示系统模板的场景、系统键、显示名称、占位符说明和更新时间

#### Scenario: 编辑系统模板
- **WHEN** 用户编辑某个系统模板
- **THEN** 页面只允许编辑该场景对应的显示名称和模板内容
- **AND** 页面提示该模板被哪个业务场景使用

#### Scenario: 预览系统模板
- **WHEN** 用户在编辑页点击预览测试
- **THEN** 页面展示样例渲染结果和校验结果
- **AND** 在校验失败时阻止保存

#### Scenario: 恢复系统默认内容
- **WHEN** 用户点击恢复系统默认
- **THEN** 页面恢复该场景的默认模板内容
- **AND** 不影响其他场景模板

## MODIFIED Requirements
### Requirement: 配置管理界面
系统 SHALL 提供AI服务、Prompt 模板与文本处理的Web配置页面。

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

#### Scenario: 文本处理配置
- **WHEN** 用户访问文本处理配置页面
- **THEN** 系统显示简繁转换、同义词、OK/NG等配置选项
