## ADDED Requirements

### Requirement: AI 出站请求保持精确 Origin
系统 MUST 让配置保存使用统一的确定性 URI 规范化契约。系统 MUST 让连通性探测、模型列表、readiness、chat 和 embedding 等所有真实 AI 出站路径共用统一受限客户端，并把每个出站请求约束在配置端点的精确规范化 Origin。

#### Scenario: 保存合法 HTTP 或 HTTPS 端点
- **GIVEN** Endpoint 是结构合法的绝对 HTTP 或 HTTPS URI
- **AND** Endpoint 可以是内网、公网、域名、IPv4 或 IPv6
- **WHEN** 系统保存配置
- **THEN** 系统不因网络位置、地址族或提供商类型拒绝该 Endpoint
- **AND** 保存阶段不发送 AI 出站请求

#### Scenario: 真实 AI 出站路径共用受限客户端
- **WHEN** 系统执行连通性探测、模型列表、readiness、chat 或 embedding 请求
- **THEN** 该请求使用统一受限客户端
- **AND** 配置保存不属于真实 AI 出站路径

#### Scenario: 请求尝试离开配置 Origin
- **WHEN** 请求的 scheme、规范化 host 或有效端口与配置 Endpoint 不同
- **OR** 请求显式覆盖 Host
- **THEN** 系统在发送前拒绝该请求

#### Scenario: 传输固定使用直连和正常 TLS
- **WHEN** AI 客户端发送请求
- **THEN** 系统禁用代理和自动重定向
- **AND** HTTPS 使用系统 SNI 与证书验证
- **AND** 系统不自定义 DNS 解析或 Socket 目标改写

### Requirement: 高成本操作使用统一资源预算
系统 MUST 通过集中配置管理解析、比较和匹配等高成本操作的并发与工作量预算。

#### Scenario: 资源租约覆盖完整处理阶段
- **WHEN** 系统执行文件解析、比较和结果投影
- **THEN** 同一资源租约覆盖全部高内存阶段
- **AND** 不在解析结束后、结果仍在内存中时提前释放并发额度

#### Scenario: 预算可配置且有安全默认值
- **WHEN** 运维未覆盖资源预算配置
- **THEN** 系统使用规范定义的候选、比较、扫描和差异默认上限
- **AND** 启动日志或运维状态能够显示生效值

### Requirement: 构建依赖可重复解析
系统 MUST 使用锁文件、精确包版本和固定 CI Action 提交保证同一源码可重复解析依赖。

#### Scenario: 后端依赖恢复
- **WHEN** 构建系统恢复 NuGet 依赖
- **THEN** 项目使用精确版本和已提交锁文件
- **AND** 不通过通配版本静默选择新版本

#### Scenario: CI 使用第三方 Action
- **WHEN** 工作流引用第三方 GitHub Action
- **THEN** 引用固定完整提交 SHA
- **AND** 在同一行保留可读版本注释
