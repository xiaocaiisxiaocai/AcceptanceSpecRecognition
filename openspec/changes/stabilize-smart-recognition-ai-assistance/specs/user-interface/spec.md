## ADDED Requirements

### Requirement: 智能结构识别界面透明展示 AI 辅助结果
系统 SHALL 在数据导入和智能填充共享识别流程中展示后端返回的 AI 辅助执行状态，不得静默隐藏降级。

#### Scenario: 检测中状态在有界窗口内恢复
- **GIVEN** 用户启用了 AI 辅助且自动选择暂时为 `checking`
- **AND** 检测响应尚未返回明确服务 ID
- **WHEN** 页面准备发起智能结构识别
- **THEN** 页面在覆盖后端探测上限的可取消窗口内等待
- **AND** 恢复为 `available` 后携带最新服务 ID 发起请求

#### Scenario: 检测中状态已返回明确服务
- **GIVEN** 用户启用了 AI 辅助且自动选择暂时为 `checking`
- **AND** 检测响应已返回明确服务 ID
- **WHEN** 页面准备发起智能结构识别
- **THEN** 页面立即携带该服务 ID 发起请求
- **AND** 由受超时保护的业务调用确认真实可用性，不增加额外轮询等待

#### Scenario: AI 部分应用或回退
- **GIVEN** 识别响应的 `aiAssist.status` 为 `partial` 或 `fallback`
- **WHEN** 页面展示识别结果
- **THEN** 页面明确说明当前结果主要来自规则识别以及 AI 未完全应用的原因
- **AND** 用户仍可进入确认卡或手动配置

#### Scenario: AI 成功或无需调用
- **WHEN** `aiAssist.status` 为 `applied` 或 `notNeeded`
- **THEN** 页面使用与状态一致的简洁说明
- **AND** 不把 `notNeeded` 显示为 AI 调用失败

#### Scenario: 新文件或取消清理旧状态
- **WHEN** 用户取消识别、重置流程或选择新文件
- **THEN** 页面取消旧的状态检测和识别请求
- **AND** 不在新流程展示旧请求的 AI 降级提示
