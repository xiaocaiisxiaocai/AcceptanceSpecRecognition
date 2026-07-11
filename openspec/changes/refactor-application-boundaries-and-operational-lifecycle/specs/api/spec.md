## MODIFIED Requirements

### Requirement: 管理类控制器通过应用用例服务执行业务流程
系统 MUST 让导入、比较、智能填充、BatchReply、配置管理和 RBAC 相关控制器通过 Application 用例服务执行业务流程，而不是在控制器或 filter 内直接编排文件、数据库和算法细节。

#### Scenario: 文档导入控制器委派
- **WHEN** 客户端调用文档上传、预览、比较或导入接口
- **THEN** 控制器委派对应 Application 用例服务执行工作流
- **AND** 控制器不直接承担逐行导入、冲突处理、文件读写或持久化编排

#### Scenario: 匹配与批量回复控制器委派
- **WHEN** 客户端调用匹配预览、执行填充、下载结果、严格复用或 BatchReply 接口
- **THEN** 控制器委派独立 Application 用例服务执行
- **AND** 不再依赖单个全能匹配工作流服务
- **AND** 不直接依赖 `IUnitOfWork`、Repository 或 `AppDbContext`

#### Scenario: RBAC 与配置控制器委派
- **WHEN** 客户端调用系统用户、角色、组织、权限字典、AI 配置、Prompt 或规则接口
- **THEN** 控制器通过对应 Application 用例服务或查询服务完成处理
- **AND** 审计 filter 通过 Application 审计端口写入，不直接访问持久化组件

## ADDED Requirements

### Requirement: 大文件下载采用流式响应
系统 MUST 对文档、填充结果、批量回复产物和其他大文件下载使用流式响应，避免在发送响应前把完整文件加载到托管内存。

#### Scenario: 流式下载文件
- **WHEN** 客户端请求下载一个已授权的文件或执行产物
- **THEN** API 从受控文件流向响应管道传输内容
- **AND** 保持正确的文件名、Content-Type 与 Content-Disposition
- **AND** 不为完整文件额外分配等量 `byte[]`

#### Scenario: 客户端取消下载
- **WHEN** 客户端在文件传输完成前断开或取消请求
- **THEN** API 停止后续传输并释放文件句柄
- **AND** 取消不被转换为误导性的业务成功响应

### Requirement: 后台生命周期任务独立于业务流量
系统 MUST 让临时会话和下载产物的到期清理由宿主生命周期管理的后台任务驱动，而不是依赖新的用户请求顺带触发全目录扫描。

#### Scenario: 无业务请求时清理到期 BatchReply 数据
- **GIVEN** 系统在一段时间内没有收到 BatchReply 请求
- **AND** 存在超过保留期的 session 或 artifact manifest
- **WHEN** hosted cleanup 到达配置的扫描周期
- **THEN** 系统仍扫描并幂等清理到期临时数据
- **AND** 单个文件失败不会终止整轮扫描

#### Scenario: 宿主停止时取消清理
- **WHEN** API 宿主开始停止
- **THEN** cleanup 不启动新一轮扫描并传播宿主取消信号
- **AND** 记录本轮已处理、跳过和失败数量

### Requirement: 长耗时 API 传播取消与资源预算错误
系统 MUST 将请求取消传播到 Application 用例及支持取消的文档/匹配操作，并对并发或输入预算超限返回明确、可观测的响应。

#### Scenario: 长耗时请求被取消
- **WHEN** 客户端取消仍在等待资源或处理文档的请求
- **THEN** API 将取消信号传播到 Application 与底层可取消操作
- **AND** 不在后台继续启动新的解析、写回或 AI 子任务

#### Scenario: 请求超过资源预算
- **WHEN** 文件结构、行列规模或并发等待超过配置预算
- **THEN** API 返回明确的受限或超限错误
- **AND** 不以进程 OOM、无限等待或连接无响应作为流控结果
