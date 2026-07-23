## ADDED Requirements
### Requirement: 关键接口限流
系统 MUST 对登录、文件上传、AI/匹配重接口提供可配置限流保护，避免单个客户端在短时间内耗尽认证、文件处理或 AI 计算资源。

#### Scenario: 登录请求超过限制
- **WHEN** 同一客户端在配置窗口内连续提交超过限制次数的登录请求
- **THEN** 系统返回 `429 Too Many Requests`
- **AND** 正常窗口恢复后允许继续登录

#### Scenario: 上传请求超过限制
- **WHEN** 同一已登录客户端在配置窗口内连续提交超过限制次数的上传请求
- **THEN** 系统返回 `429 Too Many Requests`

#### Scenario: AI 或匹配重接口超过限制
- **WHEN** 同一已登录客户端在配置窗口内连续调用超过限制次数的 AI/匹配重接口
- **THEN** 系统返回 `429 Too Many Requests`

### Requirement: 真实健康检查 API
系统 MUST 通过匿名 `/health` 端点返回 API 运行依赖状态，至少覆盖数据库连接与文件存储目录可写性。

#### Scenario: 依赖全部可用
- **WHEN** 数据库可连接且文件存储目录可写
- **THEN** `/health` 返回 `200 OK`
- **AND** 响应体包含整体健康状态

#### Scenario: 任一依赖不可用
- **WHEN** 数据库不可连接或文件存储目录不可写
- **THEN** `/health` 返回非成功健康状态
