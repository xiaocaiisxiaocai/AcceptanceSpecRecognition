# AI Integration Capability

## ADDED Requirements

### Requirement: Semantic Kernel集成
系统必须（SHALL）使用Microsoft Semantic Kernel作为AI编排框架，支持在线和本地私有化部署。

#### Scenario: 初始化Semantic Kernel
- **GIVEN** 应用程序启动
- **WHEN** 系统初始化AI服务
- **THEN** 系统创建Semantic Kernel实例
- **AND** 根据数据库中的AiServiceConfig配置注册相应的AI连接器

#### Scenario: 切换AI提供商
- **GIVEN** 用户在配置中切换AI提供商（在线或本地）
- **WHEN** 系统重新初始化AI服务
- **THEN** 系统卸载当前连接器
- **AND** 加载新的连接器
- **AND** 验证连接有效性

---

### Requirement: 在线服务 - OpenAI连接器
系统必须（SHALL）支持OpenAI API作为在线LLM和Embedding提供商。

#### Scenario: 配置OpenAI连接
- **GIVEN** 用户选择OpenAI作为AI提供商（在线服务）
- **AND** 输入有效的API Key
- **WHEN** 系统测试连接
- **THEN** 系统调用OpenAI API验证Key有效性
- **AND** 显示"连接成功（在线服务）"

#### Scenario: OpenAI API Key无效
- **GIVEN** 用户输入无效的API Key
- **WHEN** 系统测试连接
- **THEN** 系统显示错误"API Key无效或已过期"

#### Scenario: 调用OpenAI Embedding
- **GIVEN** 系统配置为使用OpenAI Embedding
- **WHEN** 系统请求生成文本向量
- **THEN** 系统调用text-embedding-ada-002或text-embedding-3-small/large
- **AND** 返回向量结果

#### Scenario: 调用OpenAI LLM
- **GIVEN** 系统配置为使用OpenAI LLM
- **WHEN** 系统请求语义分析
- **THEN** 系统调用gpt-4或gpt-4o或用户指定的模型
- **AND** 返回分析结果

---

### Requirement: 在线服务 - Azure OpenAI连接器
系统必须（SHALL）支持Azure OpenAI服务作为在线LLM和Embedding提供商。

#### Scenario: 配置Azure OpenAI连接
- **GIVEN** 用户选择Azure OpenAI作为AI提供商（在线服务）
- **AND** 输入Endpoint URL、API Key和部署名称
- **WHEN** 系统测试连接
- **THEN** 系统调用Azure OpenAI API验证配置
- **AND** 显示"连接成功（Azure在线服务）"

#### Scenario: Azure OpenAI配置不完整
- **GIVEN** 用户未填写Endpoint URL
- **WHEN** 系统尝试测试连接
- **THEN** 系统显示错误"请填写完整的Azure OpenAI配置"

#### Scenario: 调用Azure OpenAI Embedding
- **GIVEN** 系统配置为使用Azure OpenAI Embedding
- **WHEN** 系统请求生成文本向量
- **THEN** 系统调用指定的部署模型
- **AND** 返回向量结果

---

### Requirement: 本地私有化服务 - Ollama连接器
系统必须（SHALL）支持Ollama作为本地私有化LLM和Embedding提供商。

#### Scenario: 配置Ollama连接
- **GIVEN** 用户选择Ollama作为AI提供商（本地私有化）
- **AND** 输入Ollama服务地址（默认http://localhost:11434）
- **WHEN** 系统测试连接
- **THEN** 系统调用Ollama API获取可用模型列表
- **AND** 显示"连接成功（本地私有化服务），可用模型：[模型列表]"

#### Scenario: Ollama服务未启动
- **GIVEN** 用户配置的Ollama服务未运行
- **WHEN** 系统测试连接
- **THEN** 系统显示错误"无法连接到Ollama服务，请确认服务已启动"
- **AND** 提供Ollama安装和启动指南链接

#### Scenario: 选择Ollama模型
- **GIVEN** 系统成功连接到Ollama
- **WHEN** 用户选择模型
- **THEN** 系统显示已安装的模型列表
- **AND** 用户可以分别选择LLM模型（如qwen2、llama3等）
- **AND** 用户可以选择Embedding模型（如nomic-embed-text、bge-m3等）

#### Scenario: 调用Ollama Embedding
- **GIVEN** 系统配置为使用Ollama Embedding
- **AND** 选择了支持Embedding的模型
- **WHEN** 系统请求生成文本向量
- **THEN** 系统调用Ollama /api/embeddings 端点
- **AND** 返回向量结果

#### Scenario: 调用Ollama LLM
- **GIVEN** 系统配置为使用Ollama LLM
- **AND** 选择了LLM模型
- **WHEN** 系统请求语义分析
- **THEN** 系统调用Ollama /api/generate 或 /api/chat 端点
- **AND** 返回分析结果

---

### Requirement: 本地私有化服务 - LM Studio连接器
系统必须（SHALL）支持LM Studio作为本地私有化LLM和Embedding提供商。

#### Scenario: 配置LM Studio连接
- **GIVEN** 用户选择LM Studio作为AI提供商（本地私有化）
- **AND** 输入LM Studio服务地址（默认http://localhost:1234）
- **WHEN** 系统测试连接
- **THEN** 系统调用LM Studio OpenAI兼容API
- **AND** 显示"连接成功（LM Studio本地服务）"

#### Scenario: LM Studio服务未启动
- **GIVEN** 用户配置的LM Studio服务未运行
- **WHEN** 系统测试连接
- **THEN** 系统显示错误"无法连接到LM Studio服务"
- **AND** 提示"请启动LM Studio并开启本地服务器"

#### Scenario: 调用LM Studio API
- **GIVEN** 系统配置为使用LM Studio
- **WHEN** 系统请求LLM或Embedding服务
- **THEN** 系统通过OpenAI兼容API调用LM Studio
- **AND** 自动适配LM Studio的响应格式

---

### Requirement: 本地私有化服务 - 自定义OpenAI兼容端点
系统必须（SHALL）支持任意OpenAI兼容API作为本地私有化服务。

#### Scenario: 配置自定义端点
- **GIVEN** 用户选择"自定义OpenAI兼容API"（本地私有化）
- **AND** 输入自定义服务地址和可选的API Key
- **WHEN** 系统测试连接
- **THEN** 系统调用自定义端点的/v1/models接口
- **AND** 显示"连接成功（自定义私有化服务）"

#### Scenario: 自定义端点配置
- **GIVEN** 用户配置自定义端点
- **WHEN** 用户填写配置
- **THEN** 用户可以设置：
  - 服务地址（必填）
  - API Key（可选）
  - Embedding模型名称
  - LLM模型名称
  - 请求超时时间

---

### Requirement: AI服务工厂
系统必须（SHALL）提供统一的AI服务抽象，支持动态切换在线和本地私有化提供商。

#### Scenario: 创建AI服务实例
- **GIVEN** 用户配置中指定了AI提供商类型
- **WHEN** 系统需要AI服务
- **THEN** AI服务工厂根据AiServiceType创建对应的服务实例
- **AND** 返回统一的IAiService接口
- **AND** 记录服务类型（在线/本地私有化）

#### Scenario: 运行时切换提供商
- **GIVEN** 用户在运行时更改AI提供商设置
- **WHEN** 系统保存新配置到数据库
- **THEN** AI服务工厂创建新的服务实例
- **AND** 后续调用使用新的提供商
- **AND** 更新界面显示当前服务类型

#### Scenario: 获取可用服务类型列表
- **GIVEN** 用户打开AI配置界面
- **WHEN** 系统加载配置选项
- **THEN** 显示分组的服务列表：
  - 在线服务：OpenAI、Azure OpenAI
  - 本地私有化：Ollama、LM Studio、自定义端点

---

### Requirement: AI调用错误处理
系统必须（SHALL）优雅处理AI服务调用失败的情况。

#### Scenario: 网络超时
- **GIVEN** AI服务调用超过30秒无响应
- **WHEN** 系统检测到超时
- **THEN** 系统取消当前请求
- **AND** 显示错误"AI服务响应超时，请检查网络或稍后重试"
- **AND** 提供重试按钮

#### Scenario: 在线服务API限流
- **GIVEN** 在线AI服务返回429 Too Many Requests
- **WHEN** 系统收到限流响应
- **THEN** 系统自动等待指定时间后重试
- **AND** 显示"AI服务繁忙，正在排队重试..."
- **AND** 最多重试3次

#### Scenario: 本地服务资源不足
- **GIVEN** 本地私有化服务返回内存不足错误
- **WHEN** 系统收到错误响应
- **THEN** 显示"本地AI服务资源不足，请关闭其他应用或选择更小的模型"
- **AND** 提供切换模型的快捷入口

#### Scenario: 模型不可用
- **GIVEN** 配置的模型在服务端不可用
- **WHEN** 系统调用失败
- **THEN** 系统显示错误"指定的模型不可用，请检查配置或选择其他模型"
- **AND** 对于Ollama，提供"拉取模型"按钮

---

### Requirement: 离线模式支持
系统必须（SHALL）在无网络环境下仍能提供基本匹配功能。

#### Scenario: 检测离线状态（在线服务）
- **GIVEN** 系统配置为使用在线AI服务
- **AND** 系统无法访问网络
- **WHEN** 用户尝试使用Embedding或LLM匹配
- **THEN** 系统提示"在线AI服务不可用，是否切换到本地服务或文本相似度匹配？"
- **AND** 提供三个选项：切换到本地服务、使用相似度匹配、取消

#### Scenario: 本地服务离线可用
- **GIVEN** 系统配置为使用本地私有化服务（Ollama/LM Studio）
- **AND** 本地服务正常运行
- **WHEN** 网络断开
- **THEN** AI功能继续正常工作
- **AND** 状态栏显示"本地AI服务（离线可用）"

#### Scenario: 使用缓存向量离线匹配
- **GIVEN** 系统处于离线状态
- **AND** 数据库中存在已缓存的向量
- **WHEN** 用户执行Embedding匹配
- **THEN** 系统使用缓存的向量进行匹配
- **AND** 提示"使用缓存向量匹配（离线模式）"
- **AND** 新数据无法生成向量，标记为"待同步"

---

### Requirement: AI服务状态监控
系统必须（SHALL）监控AI服务的状态并在界面上显示。

#### Scenario: 显示服务状态
- **GIVEN** 应用程序运行中
- **WHEN** 系统检查AI服务状态
- **THEN** 状态栏显示当前AI服务信息：
  - 服务类型（在线/本地私有化）
  - 提供商名称
  - 连接状态（已连接/断开/错误）
  - 当前模型名称

#### Scenario: 服务状态变更通知
- **GIVEN** AI服务状态发生变化
- **WHEN** 系统检测到状态变更
- **THEN** 更新状态栏显示
- **AND** 如果从可用变为不可用，显示警告通知
- **AND** 记录状态变更日志

#### Scenario: 定期健康检查
- **GIVEN** AI服务已配置
- **WHEN** 系统每60秒执行健康检查
- **THEN** 系统调用服务的测试接口
- **AND** 更新服务状态
- **AND** 连续3次失败后显示警告

---

### Requirement: LLM和Embedding分离连接测试
系统必须（SHALL）支持分别测试LLM服务和Embedding服务的连接状态。

#### Scenario: 测试LLM连接
- **GIVEN** 用户配置了AI服务
- **WHEN** 用户点击"测试LLM连接"按钮
- **THEN** 系统发送简单的测试Prompt到LLM服务
- **AND** 成功时显示"LLM连接成功"和响应时间
- **AND** 失败时显示具体错误信息

#### Scenario: 测试Embedding连接
- **GIVEN** 用户配置了AI服务
- **WHEN** 用户点击"测试Embedding连接"按钮
- **THEN** 系统发送测试文本生成向量
- **AND** 成功时显示"Embedding连接成功"、向量维度和响应时间
- **AND** 失败时显示具体错误信息

#### Scenario: 同时测试两种服务
- **GIVEN** 用户配置了AI服务
- **WHEN** 用户点击"全部测试"按钮
- **THEN** 系统同时测试LLM和Embedding服务
- **AND** 分别显示两种服务的测试结果
- **AND** 汇总显示整体状态

---

### Requirement: Prompt模板管理
系统必须（SHALL）支持用户自定义和管理LLM匹配使用的Prompt模板。

#### Scenario: 查看默认Prompt模板
- **GIVEN** 用户进入Prompt管理界面
- **WHEN** 界面加载完成
- **THEN** 显示系统默认的Prompt模板
- **AND** 模板包含占位符说明（如{{query}}、{{candidate}}）

#### Scenario: 编辑Prompt模板
- **GIVEN** 用户在Prompt管理界面
- **WHEN** 用户修改Prompt内容
- **THEN** 系统提供多行文本编辑器
- **AND** 支持语法高亮显示占位符
- **AND** 实时显示字符数统计

#### Scenario: 保存自定义Prompt
- **GIVEN** 用户修改了Prompt模板
- **WHEN** 用户点击"保存"
- **THEN** 系统验证Prompt格式（必须包含必要占位符）
- **AND** 保存到数据库
- **AND** 显示"保存成功"

#### Scenario: 重置为默认Prompt
- **GIVEN** 用户修改了Prompt但想恢复默认
- **WHEN** 用户点击"重置为默认"
- **THEN** 系统提示确认
- **AND** 用户确认后恢复为系统默认Prompt
- **AND** 显示"已重置为默认模板"

#### Scenario: Prompt模板预览
- **GIVEN** 用户编辑了Prompt模板
- **WHEN** 用户点击"预览"
- **THEN** 系统用示例数据填充占位符
- **AND** 显示最终发送给LLM的完整Prompt
- **AND** 用户可以确认效果

#### Scenario: Prompt占位符说明
- **GIVEN** 用户在Prompt编辑界面
- **WHEN** 用户查看帮助信息
- **THEN** 显示可用的占位符列表：
  - {{query_project}} - 查询的项目名称
  - {{query_spec}} - 查询的规格
  - {{candidate_project}} - 候选的项目名称
  - {{candidate_spec}} - 候选的规格
  - {{candidate_acceptance}} - 候选的验收内容
  - {{candidate_remark}} - 候选的备注
  - {{threshold}} - 匹配阈值
