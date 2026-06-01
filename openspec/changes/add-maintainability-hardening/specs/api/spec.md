## ADDED Requirements
### Requirement: 控制器请求取消传递
系统 MUST 在直接执行异步查询且底层 API 支持取消的控制器方法中接收并传递 `CancellationToken`，以便客户端取消请求后尽快释放资源。

#### Scenario: 查询请求被取消
- **WHEN** 客户端取消一个仍在执行的查询请求
- **THEN** 控制器将取消信号传递给支持取消的异步查询调用

### Requirement: 关键请求 DTO 服务端验证
系统 MUST 为关键写入与预览请求 DTO 提供服务端验证，防止明显无效或超长输入进入业务处理。

#### Scenario: 请求字段缺失或超长
- **WHEN** 客户端提交缺少必填字段或超过长度限制的请求
- **THEN** API 返回 `400 Bad Request`
- **AND** 不执行后续业务写入或预览逻辑
