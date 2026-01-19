# Data Storage Capability

## ADDED Requirements

### Requirement: EF Core数据访问层
系统必须（SHALL）使用Entity Framework Core作为ORM框架，采用Code First模式管理数据库。

#### Scenario: 数据库初始化
- **GIVEN** 应用程序首次启动
- **WHEN** 系统初始化数据访问层
- **THEN** 系统自动检测数据库是否存在
- **AND** 如果不存在，自动创建数据库和所有表结构
- **AND** 应用所有待执行的迁移

#### Scenario: 数据库迁移
- **GIVEN** 应用程序版本更新包含数据库Schema变更
- **WHEN** 系统启动时检测到待执行迁移
- **THEN** 系统自动应用迁移
- **AND** 保留现有数据
- **AND** 记录迁移日志

#### Scenario: 迁移失败处理
- **GIVEN** 数据库迁移过程中发生错误
- **WHEN** 系统检测到迁移失败
- **THEN** 系统回滚当前迁移
- **AND** 显示错误信息"数据库升级失败，请联系技术支持"
- **AND** 建议用户备份数据库文件

---

### Requirement: 客户管理
系统必须（SHALL）支持客户信息的增删改查操作。

#### Scenario: 创建新客户
- **GIVEN** 用户输入客户名称"客户A"
- **WHEN** 系统保存客户信息
- **THEN** 系统通过EF Core创建新客户实体
- **AND** 自动设置CreatedAt为当前时间
- **AND** 返回客户ID

#### Scenario: 客户名称重复
- **GIVEN** 数据库中已存在名为"客户A"的客户
- **WHEN** 用户尝试创建同名客户
- **THEN** EF Core抛出唯一约束异常
- **AND** 系统显示错误提示"客户名称已存在"

#### Scenario: 删除客户
- **GIVEN** 客户"客户A"下存在制程和验收数据
- **WHEN** 用户尝试删除该客户
- **THEN** 系统提示"该客户下存在X个制程和Y条验收数据，确认删除？"
- **AND** 用户确认后EF Core级联删除所有相关数据

---

### Requirement: 制程管理
系统必须（SHALL）支持按客户维度管理制程信息。

#### Scenario: 创建新制程
- **GIVEN** 用户选择了"客户A"并输入制程名称"制程X"
- **WHEN** 系统保存制程信息
- **THEN** 系统通过EF Core创建新制程实体
- **AND** 设置CustomerId外键关联
- **AND** 返回制程ID

#### Scenario: 同一客户下制程名称重复
- **GIVEN** 客户A下已存在名为"制程X"的制程
- **WHEN** 用户尝试在客户A下创建同名制程
- **THEN** EF Core抛出复合唯一约束异常
- **AND** 系统显示错误提示"该客户下制程名称已存在"

#### Scenario: 不同客户可以有同名制程
- **GIVEN** 客户A下存在"制程X"
- **WHEN** 用户在客户B下创建"制程X"
- **THEN** 系统成功创建制程
- **AND** 两个制程相互独立

---

### Requirement: 验收规格数据存储
系统必须（SHALL）支持存储验收规格数据，包含项目、规格、验收、备注四列。

#### Scenario: 导入验收数据
- **GIVEN** 用户选择了客户A的制程X，并提供了验收数据列表
- **WHEN** 系统执行导入
- **THEN** 系统通过EF Core批量创建AcceptanceSpec实体
- **AND** 每个实体关联到制程X的ProcessId
- **AND** 关联到上传的WordFile的WordFileId

#### Scenario: 批量导入性能
- **GIVEN** 用户导入包含1000行数据的表格
- **WHEN** 系统执行批量导入
- **THEN** 系统使用EF Core AddRange批量添加
- **AND** 在单个事务中提交
- **AND** 导入时间不超过5秒
- **AND** 显示导入进度

#### Scenario: 导入事务回滚
- **GIVEN** 批量导入过程中发生错误
- **WHEN** 系统检测到异常
- **THEN** EF Core自动回滚整个事务
- **AND** 数据库保持导入前状态
- **AND** 显示错误信息

---

### Requirement: 数据来源追溯
系统必须（SHALL）记录每条验收数据的来源信息。

#### Scenario: 查看数据来源
- **GIVEN** 用户查看某条验收数据的详情
- **WHEN** 系统显示详情
- **THEN** 系统通过EF Core Include加载关联的WordFile
- **AND** 显示该数据来源的Word文件名
- **AND** 显示导入时间
- **AND** 提供下载原始Word文件的链接

#### Scenario: 按来源筛选数据
- **GIVEN** 用户选择按来源文件筛选
- **WHEN** 用户选择特定的Word文件名
- **THEN** 系统通过EF Core Where查询关联WordFileId的所有数据
- **AND** 显示该文件导入的所有验收数据

---

### Requirement: 重复数据处理
系统必须（SHALL）在导入时检测并处理重复数据。

#### Scenario: 检测重复文件
- **GIVEN** 用户尝试导入已导入过的Word文件
- **WHEN** 系统通过EF Core查询FileHash
- **THEN** 检测到文件哈希值重复
- **AND** 显示对话框"该文件已导入，请选择操作"
- **AND** 提供三个选项：覆盖、追加、取消

#### Scenario: 覆盖导入
- **GIVEN** 用户选择"覆盖"操作
- **WHEN** 系统执行覆盖导入
- **THEN** 系统在事务中先删除该文件之前导入的所有数据
- **AND** 导入新数据
- **AND** 记录操作到历史表
- **AND** 提交事务

#### Scenario: 追加导入
- **GIVEN** 用户选择"追加"操作
- **WHEN** 系统执行追加导入
- **THEN** 系统保留原有数据
- **AND** 将新数据追加到数据库
- **AND** 记录操作到历史表

---

### Requirement: 操作历史记录
系统必须（SHALL）记录所有数据操作的历史，支持撤销。

#### Scenario: 记录导入操作
- **GIVEN** 用户完成数据导入
- **WHEN** 导入成功
- **THEN** 系统创建OperationHistory实体
- **AND** 设置OperationType为Import
- **AND** Details存储导入的文件名和数据条数（JSON格式）
- **AND** UndoData存储导入的记录ID列表（JSON格式）

#### Scenario: 记录填充操作
- **GIVEN** 用户完成数据填充
- **WHEN** 填充成功
- **THEN** 系统创建OperationHistory实体
- **AND** 设置OperationType为Fill
- **AND** Details存储目标文件名和填充的数据
- **AND** UndoData存储原始数据用于撤销

#### Scenario: 撤销导入操作
- **GIVEN** 用户查看历史记录中的一次导入操作
- **WHEN** 用户点击"撤销"
- **THEN** 系统解析UndoData获取记录ID列表
- **AND** 通过EF Core批量删除这些记录
- **AND** 更新操作历史的CanUndo为false

#### Scenario: 撤销填充操作
- **GIVEN** 用户查看历史记录中的一次填充操作
- **WHEN** 用户点击"撤销"
- **THEN** 系统解析UndoData恢复Word文档中被填充位置的原始内容
- **AND** 更新操作历史的CanUndo为false

---

### Requirement: 向量缓存存储
系统必须（SHALL）缓存Embedding向量以提高匹配性能。

#### Scenario: 缓存新向量
- **GIVEN** 系统为某条验收数据生成了Embedding向量
- **WHEN** 向量生成完成
- **THEN** 系统创建EmbeddingCache实体
- **AND** 设置SpecId关联到原始数据
- **AND** 设置ModelName为使用的模型名称
- **AND** Vector存储为byte[]（序列化的float数组）

#### Scenario: 读取缓存向量
- **GIVEN** 用户请求对某条数据进行Embedding匹配
- **WHEN** 系统通过EF Core查询缓存
- **THEN** 按SpecId和ModelName查找缓存
- **AND** 如果存在，直接返回缓存向量
- **AND** 如果不存在，生成新向量并缓存

#### Scenario: 模型变更时重新生成
- **GIVEN** 用户切换了Embedding模型
- **WHEN** 系统执行匹配
- **THEN** 系统检测到ModelName不匹配
- **AND** 删除旧缓存，生成新向量
- **AND** 创建新的缓存记录

---

### Requirement: AI服务配置存储
系统必须（SHALL）将AI服务配置持久化到数据库。

#### Scenario: 保存AI服务配置
- **GIVEN** 用户配置了新的AI服务（在线或本地私有化）
- **WHEN** 用户点击保存
- **THEN** 系统创建或更新AiServiceConfig实体
- **AND** API Key等敏感信息加密存储
- **AND** 设置UpdatedAt为当前时间

#### Scenario: 加载默认AI配置
- **GIVEN** 应用程序启动
- **WHEN** 系统初始化AI服务
- **THEN** 系统通过EF Core查询IsDefault=true的配置
- **AND** 使用该配置初始化AI服务
- **AND** 如果无默认配置，提示用户配置

#### Scenario: 切换AI服务配置
- **GIVEN** 用户有多个AI服务配置
- **WHEN** 用户选择切换配置
- **THEN** 系统更新原默认配置的IsDefault为false
- **AND** 设置新配置的IsDefault为true
- **AND** 重新初始化AI服务

---

### Requirement: 数据查询优化
系统必须（SHALL）优化数据查询性能。

#### Scenario: 分页查询
- **GIVEN** 数据库中有大量验收数据
- **WHEN** 用户查看数据列表
- **THEN** 系统使用EF Core Skip/Take实现分页
- **AND** 每页默认显示50条
- **AND** 显示总数和页码

#### Scenario: 延迟加载导航属性
- **GIVEN** 用户查看验收数据列表
- **WHEN** 系统加载数据
- **THEN** 默认不加载WordFile等导航属性
- **AND** 仅在需要时通过Include显式加载
- **AND** 避免N+1查询问题

#### Scenario: 索引优化查询
- **GIVEN** 用户按客户和制程筛选数据
- **WHEN** 系统执行查询
- **THEN** 利用EF Core配置的索引加速查询
- **AND** 查询响应时间不超过100ms

---

### Requirement: 同义词存储
系统必须（SHALL）支持同义词数据的持久化存储。

#### Scenario: 存储同义词组
- **GIVEN** 用户添加同义词组"不锈钢,不鏽鋼,SUS,Stainless"
- **WHEN** 系统保存同义词
- **THEN** 系统创建SynonymGroup实体
- **AND** 为每个词创建SynonymWord实体并关联到组
- **AND** 组内第一个词标记为标准词（IsStandard=true）

#### Scenario: 查询同义词
- **GIVEN** 系统需要查找"SUS"的同义词
- **WHEN** 系统执行查询
- **THEN** 系统查找包含"SUS"的同义词组
- **AND** 返回组内所有词语
- **AND** 标识标准词

#### Scenario: 更新同义词组
- **GIVEN** 已存在同义词组
- **WHEN** 用户修改组内词语
- **THEN** 系统更新SynonymWord记录
- **AND** 记录更新时间

#### Scenario: 删除同义词组
- **GIVEN** 用户删除同义词组
- **WHEN** 系统执行删除
- **THEN** 系统删除SynonymGroup及所有关联的SynonymWord
- **AND** 使用级联删除

---

### Requirement: 关键字存储
系统必须（SHALL）支持高亮关键字的持久化存储。

#### Scenario: 存储关键字
- **GIVEN** 用户添加关键字"危险"
- **WHEN** 系统保存关键字
- **THEN** 系统创建Keyword实体
- **AND** 记录创建时间

#### Scenario: 批量添加关键字
- **GIVEN** 用户导入关键字列表文件
- **WHEN** 系统批量保存
- **THEN** 系统使用AddRange批量添加
- **AND** 自动去除重复关键字
- **AND** 在单个事务中完成

#### Scenario: 查询所有关键字
- **GIVEN** 系统需要获取所有关键字用于高亮
- **WHEN** 系统执行查询
- **THEN** 系统返回所有Keyword记录
- **AND** 结果可缓存以提高性能

#### Scenario: 删除关键字
- **GIVEN** 用户删除关键字
- **WHEN** 系统执行删除
- **THEN** 系统删除对应Keyword记录

---

### Requirement: 文本处理配置存储
系统必须（SHALL）支持文本处理配置的持久化存储。

#### Scenario: 存储简繁转换配置
- **GIVEN** 用户配置简繁转换
- **WHEN** 系统保存配置
- **THEN** 系统创建或更新TextProcessingConfig实体
- **AND** 保存转换模式（如HansToTW）
- **AND** 保存启用状态

#### Scenario: 存储关键字高亮配置
- **GIVEN** 用户配置关键字高亮
- **WHEN** 系统保存配置
- **THEN** 系统保存启用状态
- **AND** 保存高亮颜色（RGB值）

#### Scenario: 存储OK/NG格式配置
- **GIVEN** 用户配置OK/NG格式转换
- **WHEN** 系统保存配置
- **THEN** 系统保存启用状态
- **AND** 保存OK标准格式文本
- **AND** 保存NG标准格式文本

#### Scenario: 加载文本处理配置
- **GIVEN** 应用程序启动
- **WHEN** 系统初始化文本处理服务
- **THEN** 系统从数据库加载TextProcessingConfig
- **AND** 应用配置到文本处理管道

---

### Requirement: Prompt模板存储
系统必须（SHALL）支持LLM匹配Prompt模板的持久化存储。

#### Scenario: 存储自定义Prompt模板
- **GIVEN** 用户修改了Prompt模板
- **WHEN** 用户保存模板
- **THEN** 系统创建或更新PromptTemplate实体
- **AND** 保存模板内容
- **AND** 记录更新时间

#### Scenario: 加载Prompt模板
- **GIVEN** 应用程序启动或用户进入Prompt管理界面
- **WHEN** 系统加载Prompt模板
- **THEN** 系统从数据库查询PromptTemplate
- **AND** 如果存在自定义模板，返回自定义内容
- **AND** 如果不存在，返回系统默认模板

#### Scenario: 重置Prompt模板
- **GIVEN** 用户选择重置为默认模板
- **WHEN** 系统执行重置
- **THEN** 系统删除或清空PromptTemplate记录
- **AND** 后续加载将返回系统默认模板
