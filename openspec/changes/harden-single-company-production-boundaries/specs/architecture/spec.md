## ADDED Requirements

### Requirement: 部署拓扑只应用一次 API 路径前缀

系统 MUST 确保外部 `/api` 前缀在应用路由与代理映射组合后只出现一次。

#### Scenario: IIS 承载生产 API

- **WHEN** IIS 接收 `/api/auth/login`、`/api/auth/refresh` 或 `/api/auth/logout`
- **THEN** 请求命中对应 API 动作而不是形成 `/api/api/...`
- **AND** POST 请求不因静态文件处理器或错误应用层级返回 405

### Requirement: 高成本文件处理遵守统一资源预算

系统 MUST 在解析和物化 Excel 内容前执行文件、解压、维度、窗口和并发预算检查。

#### Scenario: 工作簿超过任一预算

- **WHEN** 上传文件大小、解压后大小、工作表维度或并发解析超过配置上限
- **THEN** 系统在大规模内存物化前拒绝请求
- **AND** 返回可操作的受限错误且清理临时文件
