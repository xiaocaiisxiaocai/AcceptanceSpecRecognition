## MODIFIED Requirements
### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
The system SHALL expose current smart-fill preview and execution contracts without trusting legacy suggestion-style decision fields, and SHALL accept an explicit matching configuration flag for synchronous AI equivalence adjudication.

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** the client requests smart-fill preview
- **THEN** the response SHALL use current match result decision fields rather than legacy suggestion fields

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** the client requests smart-fill execution
- **THEN** the server SHALL recompute or validate the current match decision instead of trusting legacy client decision fields

#### Scenario: 配置同步 AI 等价裁决
- **WHEN** the client sends matching configuration with AI equivalence adjudication enabled
- **THEN** the server SHALL pass that flag into the matching runtime

#### Scenario: 默认不启用同步 AI 等价裁决
- **WHEN** the client omits the AI equivalence adjudication flag
- **THEN** the server SHALL treat synchronous AI equivalence adjudication as disabled
