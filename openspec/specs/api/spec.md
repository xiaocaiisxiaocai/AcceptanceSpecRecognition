# API Capability

## Purpose
提供基础数据的RESTful接口，覆盖客户、制程与验收规格等基础数据的查询、维护与前端/外部工具集成调用场景。
## Requirements
### Requirement: 基础数据RESTful API
系统 SHALL 通过ASP.NET Core Web API提供基础数据与系统配置访问接口。

#### Scenario: API数据查询
- **WHEN** 前端发送GET请求到/api/customers
- **THEN** 系统返回JSON格式的客户列表

#### Scenario: API数据创建
- **WHEN** 前端发送POST请求到/api/customers
- **THEN** 系统创建客户记录并返回创建结果

#### Scenario: API数据更新
- **WHEN** 前端发送PUT请求到/api/customers/{id}
- **THEN** 系统更新指定客户记录

#### Scenario: API数据删除
- **WHEN** 前端发送DELETE请求到/api/customers/{id}
- **THEN** 系统删除指定客户记录

#### Scenario: Prompt 模板配置接口
- **WHEN** 前端访问 Prompt 模板相关接口
- **THEN** 系统按系统模板场景返回模板数据
- **AND** 系统支持模板校验预览与按场景恢复默认内容
- **AND** 系统不再暴露任意新增、删除、设默认或非系统模板旁路读写等旧兼容接口

### Requirement: 匹配接口支持 LLM 实体判别配置
系统 SHALL 在智能匹配相关接口中接收 LLM 实体判别配置，并将其传入运行时匹配链路。

#### Scenario: 预览接口接收实体判别开关
- **GIVEN** 用户在匹配配置中开启 LLM 实体判别
- **WHEN** 前端调用匹配预览接口
- **THEN** 请求体包含实体判别开关与阈值配置
- **AND** 后端按该配置执行运行时实体判别

### Requirement: 匹配接口返回实体问题说明
系统 SHALL 在匹配预览与候选详情响应中返回实体判别产生的问题说明，而无需前端自行拼装。

#### Scenario: 返回实体冲突问题
- **GIVEN** 运行时实体判别结果为 `conflict`
- **WHEN** 后端返回最佳匹配与候选详情
- **THEN** 响应中包含结构化问题项
- **AND** 问题编码可标识实体冲突或实体未知
- **AND** 问题项包含源值、候选值和用户可读说明

### Requirement: 验收规格语义搜索 API
系统 SHALL 提供验收规格语义搜索 API，用于按输入文本批量返回语义相近的规格结果。

#### Scenario: 单条语义搜索
- **WHEN** 前端发送一条查询文本到验收规格语义搜索接口
- **THEN** 系统返回该查询的候选规格列表
- **AND** 每条结果包含规格主键与相似度分数

#### Scenario: 批量语义搜索
- **WHEN** 前端在一次请求中发送多条查询文本
- **THEN** 系统按输入顺序返回分组结果
- **AND** 每组结果独立包含查询文本、命中数和候选列表

#### Scenario: 语义搜索遵循数据范围
- **WHEN** 当前用户调用验收规格语义搜索接口
- **THEN** 系统仅返回当前用户有权访问的数据范围内的规格
- **AND** 同时应用请求中的客户、机型、制程筛选条件

#### Scenario: Embedding 服务不可用
- **WHEN** 前端调用验收规格语义搜索接口且 Embedding 服务不可用
- **THEN** 系统返回明确失败信息
- **AND** 不静默降级为普通关键词搜索

### Requirement: API 权限默认拒绝
系统 MUST 对所有控制器接口执行权限校验，采用 `api:resource:action` 权限码；未命中权限时返回 403。

#### Scenario: 用户缺少接口权限
- **WHEN** 已登录用户访问一个其权限集中不存在的控制器接口
- **THEN** 系统返回 403 且包含缺少的权限码信息

#### Scenario: 管理员访问接口
- **WHEN** 已登录用户持有可覆盖目标权限码的授权（如 `*:*:*` 或匹配通配权限）
- **THEN** 请求可继续进入控制器动作执行

### Requirement: 登录令牌承载组织与权限上下文
系统 MUST 在 AccessToken 中包含用户标识、公司标识与权限版本信息，以支持单公司边界和授权变更后会话管理。

#### Scenario: 登录成功下发令牌
- **WHEN** 用户使用正确用户名和密码登录
- **THEN** 返回的 AccessToken 声明包含 `user_id`、`company_id`、`permission_version`

### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
系统 SHALL 以服务端当前匹配结果和决策门禁为准，不再暴露或信任旧的 suggestion / compatibility 字段；同时执行权限与下载权限分离，执行成功后允许基于任务标识独立重试下载，并接受同步 AI 等价裁决的显式配置。

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** 客户端调用智能填充预览接口
- **THEN** 预览配置与结果仅暴露召回、歧义、实体判别、复核与等价裁决相关字段
- **AND** 不再暴露 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows`、`LlmSuggestionScoreThreshold` 或 `LlmSuggestion`

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 服务端在执行前按当前文件与配置重算或校验当前匹配决策
- **AND** 不接受旧 suggestion / compatibility 透传字段替代服务端决策

#### Scenario: 执行成功后允许独立重试下载
- **GIVEN** 智能填充执行已经成功并返回任务标识
- **WHEN** 客户端后续单独调用下载接口
- **THEN** 下载接口仍可仅基于任务标识返回结果文件
- **AND** 下载权限不足不应阻止此前的执行接口完成

#### Scenario: 配置同步 AI 等价裁决
- **WHEN** the client sends matching configuration with AI equivalence adjudication enabled
- **THEN** the server SHALL pass that flag into the matching runtime

#### Scenario: 默认不启用同步 AI 等价裁决
- **WHEN** the client omits the AI equivalence adjudication flag
- **THEN** the server SHALL treat synchronous AI equivalence adjudication as disabled

### Requirement: 智能填充 llm-stream SSE 契约
系统 SHALL 通过 `text/event-stream` 暴露智能填充复核进度，并在结束时发送显式终止事件。

#### Scenario: 复核生命周期事件
- **WHEN** 客户端调用 `POST /api/matching/llm-stream`
- **THEN** 响应 `Content-Type` 为 `text/event-stream`
- **AND** 对进入流式复核的行依次发送 `review.start`、一个或多个 `review.delta`，以及 `review.done` 或 `review.error`

#### Scenario: AI 等价裁决已要求人工确认时跳过旧复核流
- **GIVEN** 当前最佳候选的 AI 等价裁决结果已要求人工确认
- **WHEN** 客户端调用 `POST /api/matching/llm-stream`
- **THEN** 系统可以直接发送一个 `review.done`
- **AND** 该事件用于告知“保留 AI 等价裁决结果，不再进入旧复核流”
- **AND** 该行不要求先发送 `review.start` 或 `review.delta`

#### Scenario: 流式会话结束事件
- **WHEN** `llm-stream` 本次请求处理结束
- **THEN** 系统发送 `stream.complete`
- **AND** 事件数据至少包含 `totalItems`、`reviewTargets`、`reviewSuccess`、`reviewFailed`、`reviewTimeout`、`reviewRetries`、`totalFailures`、`circuitOpened` 与 `elapsedMs`

### Requirement: 运行时匹配知识不提供对外配置 API
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，不再提供分组作者视图、草稿生成或其他 matching-knowledge 配置接口。

#### Scenario: 客户端访问旧匹配知识接口
- **WHEN** 客户端访问 `/api/matching-knowledge`、`/api/matching-knowledge-drafts/generate`、`/api/matching-knowledge/clear`、`/api/matching-knowledge/restore-defaults`、`/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
- **THEN** 系统不再提供这些旧配置接口
- **AND** 运行时不再以数据库中的 matching-knowledge 配置作为来源

#### Scenario: 客户端访问旧分组作者视图
- **WHEN** 客户端访问旧实体组、单位组、字段组或冲突组作者接口
- **THEN** 系统不再返回或接受分组作者模型
- **AND** 匹配知识仅保留为运行时内部能力

### Requirement: 数据库存储的用户认证
系统 SHALL 从数据库用户表读取账号信息进行登录认证，并签发 JWT 令牌。

#### Scenario: 登录成功返回令牌
- **WHEN** 用户提供正确的用户名和密码，且账号处于启用状态
- **THEN** 系统返回 `accessToken`、`refreshToken` 与用户角色权限信息

#### Scenario: 用户名或密码错误
- **WHEN** 用户名不存在或密码校验失败
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 账号被停用
- **WHEN** 用户账号存在但 `IsActive = false`
- **THEN** 系统返回 `401 Unauthorized`

---

### Requirement: 刷新令牌校验
系统 SHALL 校验刷新令牌有效性，并在用户仍有效时重新签发令牌。

#### Scenario: 刷新令牌无效
- **WHEN** 客户端提交无效、过期或伪造的 `refreshToken`
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 刷新时用户不存在或已停用
- **WHEN** 刷新令牌有效但关联用户不存在或被停用
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 刷新成功
- **WHEN** 刷新令牌有效且用户处于启用状态
- **THEN** 系统返回新的 `accessToken` 与 `refreshToken`

---

### Requirement: 管理接口权限授权
系统 MUST 对管理类接口执行基于权限码的授权校验，而不是依赖固定角色名称作为唯一授权依据。

#### Scenario: 用户具备目标接口权限
- **WHEN** 已登录用户持有与目标接口匹配的权限码
- **THEN** 系统允许请求进入对应控制器动作

#### Scenario: 用户缺少目标接口权限
- **WHEN** 已登录用户访问一个其权限集中不存在的管理接口
- **THEN** 系统返回 `403 Forbidden`
- **AND** 响应包含缺少的权限码信息

### Requirement: 系统用户管理API
系统 SHALL 提供受权限保护的系统用户管理接口，并维持单角色、单组织用户契约。

#### Scenario: 查询系统用户列表
- **WHEN** 管理员或具备对应权限的用户请求系统用户列表接口
- **THEN** 系统返回分页用户数据
- **AND** 每个用户仅返回一个有效角色和一个有效组织归属

#### Scenario: 创建系统用户
- **WHEN** 管理员提交合法的新用户信息（用户名、密码、单个角色、单个组织）
- **THEN** 系统创建用户并返回用户详情

#### Scenario: 更新系统用户
- **WHEN** 管理员更新用户昵称、单个角色、单个组织或启用状态
- **THEN** 系统保存变更并返回更新后的用户信息

#### Scenario: 重置用户密码
- **WHEN** 管理员提交新密码
- **THEN** 系统更新用户密码哈希并使新密码可用于后续登录

#### Scenario: 禁止移除最后一个启用admin
- **WHEN** 管理员尝试删除或停用最后一个启用状态的 `admin` 用户
- **THEN** 系统拒绝请求并返回业务错误

### Requirement: 角色管理 API
系统 SHALL 提供角色管理接口，并对内置角色执行“可编辑、不可删除”的规则。

#### Scenario: 更新内置角色
- **WHEN** 前端发送 PUT 请求到 `/api/auth-roles/{id}` 更新内置角色
- **THEN** 系统保存角色名称、描述、状态、权限配置与数据范围修改

#### Scenario: 删除内置角色
- **WHEN** 前端发送 DELETE 请求到 `/api/auth-roles/{id}` 删除内置角色
- **THEN** 系统返回删除受限错误，且不删除该角色

### Requirement: Prompt 模板预览与重置 API
系统 SHALL 提供 Prompt 模板预览和按场景恢复系统默认内容的 API。

#### Scenario: 预览模板
- **WHEN** 前端发送 Prompt 模板预览请求
- **THEN** 系统返回模板校验结果、样例渲染内容与结构化输出校验结果
- **AND** 样例渲染内容会覆盖该系统模板场景运行时必需的占位符

#### Scenario: 按场景恢复默认模板
- **WHEN** 前端请求恢复某个系统模板场景的默认内容
- **THEN** 系统仅重置该场景模板内容
- **AND** 返回重置后的模板数据

### Requirement: 系统用户与权限接口采用单组织和菜单权限模型
系统 MUST 在系统用户管理和权限相关接口中使用单组织字段，并暴露菜单权限能力。

#### Scenario: 创建用户提交单组织
- **WHEN** 管理员调用系统用户创建接口并提交 `orgUnitId`
- **THEN** 系统为该用户建立唯一组织关系，并返回单个 `orgUnitId` / `orgUnitName`

#### Scenario: 更新用户提交旧组织数组
- **WHEN** 客户端仍以旧格式提交 `orgUnitIds`、`orgUnits` 或 `primaryOrgUnitId`
- **THEN** 系统拒绝请求并返回参数错误

#### Scenario: 路由权限包含菜单权限
- **WHEN** 用户登录后拉取路由或权限数据
- **THEN** 返回结果能够区分菜单权限与页面权限，并据此控制导航容器和页面节点

### Requirement: 系统用户与认证接口采用单角色模型
系统 MUST 在系统用户管理、登录和刷新令牌接口中使用单角色字段 `roleCode`，不再暴露角色数组。

#### Scenario: 创建用户提交单角色
- **WHEN** 管理员调用系统用户创建接口并提交 `roleCode`
- **THEN** 系统为该用户建立唯一角色关系，并返回单个 `roleCode`

#### Scenario: 更新用户提交多个角色
- **WHEN** 客户端仍以旧格式提交角色数组或等价多角色数据
- **THEN** 系统拒绝请求并返回参数错误

#### Scenario: 登录成功返回单角色
- **WHEN** 用户登录成功或刷新令牌成功
- **THEN** 返回数据包含单个 `roleCode`，且不包含 `roles` 数组

### Requirement: 单角色模型下保留管理员边界保护
系统 MUST 在单角色模型下继续阻止最后一个启用中的 `admin` 用户被降级、停用或删除。

#### Scenario: 尝试移除最后一个启用中的 admin
- **WHEN** 管理员更新、停用或删除系统中最后一个启用且角色为 `admin` 的用户
- **THEN** 系统拒绝请求并返回明确错误

### Requirement: 组织管理 API 收敛为单根组织契约
系统 SHALL 将组织管理接口收敛为单公司根组织维护接口，而不是多层级组织树编辑接口。

#### Scenario: 查询组织树与平铺列表
- **WHEN** 客户端访问组织树或组织平铺接口
- **THEN** 系统返回当前公司的唯一根组织节点
- **AND** 不暴露多层级组织树编辑契约

#### Scenario: 新增或删除组织节点
- **WHEN** 客户端调用新增或删除组织节点接口
- **THEN** 系统拒绝请求并返回单组织模式限制说明

#### Scenario: 更新组织节点
- **WHEN** 客户端调用更新组织节点接口
- **THEN** 系统只允许更新当前公司的根组织节点

### Requirement: 管理类控制器通过应用用例服务执行业务流程
系统 MUST 让导入、智能填充和 RBAC 相关控制器通过 Application 用例服务执行业务流程，而不是在控制器内直接编排文件、数据库和算法细节。

#### Scenario: 文档导入控制器委派
- **WHEN** 客户端调用文档上传、预览或导入接口
- **THEN** 控制器委派对应 Application 用例服务执行工作流
- **AND** 控制器不直接承担逐行导入、冲突处理与文件读写编排

#### Scenario: 匹配控制器委派
- **WHEN** 客户端调用匹配预览、执行填充或下载结果接口
- **THEN** 控制器委派独立 Application 用例服务执行
- **AND** 不再依赖单个全能匹配工作流服务

#### Scenario: RBAC 控制器委派
- **WHEN** 客户端调用系统用户、角色、组织或权限字典接口
- **THEN** 控制器通过对应 Application 用例服务或查询服务完成处理

### Requirement: 基于已回复文档的批量回复 API
系统 SHALL 提供独立的批量回复 API，允许用户上传一份人工已回复的来源文档和多个本地目标文档，并按严格复用规则完成预检与批量写回。

#### Scenario: 批量回复预检
- **GIVEN** 用户上传一份已回复的来源文档、多个目标文档以及表格配置
- **WHEN** 前端调用批量回复预检接口
- **THEN** 系统校验来源文件与目标文件格式一致
- **AND** 系统逐个文件校验表格配置、数据区行数以及项目+规格顺序是否与来源完全一致
- **AND** 系统返回每个目标文件的“可应用 / 不可应用”状态与失败原因
- **AND** 系统不写入任何目标文件

#### Scenario: 批量回复执行
- **GIVEN** 至少一个目标文件已经通过批量回复预检
- **WHEN** 前端提交批量回复执行请求
- **THEN** 系统只将来源文档中的验收值和备注值写回目标文件
- **AND** 系统不重新匹配、不调用 AI、不要求用户重新逐行确认
- **AND** 系统返回逐文件执行结果与下载入口

#### Scenario: 来源与目标格式不一致
- **GIVEN** 来源文档为 `docx` 而目标文档为 `xlsx`，或相反
- **WHEN** 前端调用批量回复预检或执行接口
- **THEN** 系统拒绝该目标文件
- **AND** 系统返回“文件类型不一致”错误

### Requirement: 批量回复会话采用临时上传上下文
系统 SHALL 将批量回复限制为基于一次临时上传会话的数据来源，而不是依赖智能填充任务快照或历史模板列表。

#### Scenario: 仅允许引用当前上传会话
- **GIVEN** 用户进入批量回复页面
- **WHEN** 用户发起预检或执行
- **THEN** 系统只允许使用当前上传来源文档建立的临时会话数据
- **AND** 系统不要求用户从智能填充历史任务中选择来源

#### Scenario: 执行前再次复检
- **GIVEN** 用户已经拿到批量回复预检结果
- **WHEN** 用户发起执行请求
- **THEN** 系统在写回前再次校验目标文件是否仍满足严格复用条件
- **AND** 对校验失败的文件拒绝写回并返回具体原因

### Requirement: 执行记录查询 API
系统 SHALL 提供智能填充与批量回复执行记录的列表与详情 API。

#### Scenario: 查询任务记录列表
- **WHEN** 用户请求执行记录列表
- **THEN** 系统按任务维度返回分页结果
- **AND** 每条记录包含任务类型、源文件信息、文件数、总行数、已匹配数、已采用数、未匹配数、跳过数、未采用数、人工选择数和创建时间等摘要字段
- **AND** 列表查询不要求客户端自行扫描详情 JSON 汇总

#### Scenario: 查询任务记录详情
- **WHEN** 用户请求某条执行记录详情
- **THEN** 系统返回该任务的 `文件 -> Sheet -> 行记录` 结构
- **AND** 每条行记录包含源项目、源规格、最终结果、置信度百分比、处理状态与人工选择标记
- **AND** 详情响应同时包含未匹配、跳过、未采用和已采用记录

#### Scenario: 按归属限制记录访问
- **WHEN** 用户查询不属于当前用户/公司的执行记录
- **THEN** 系统拒绝返回该记录
- **AND** 不泄露其他用户任务的摘要与详情

### Requirement: 执行完成后自动生成记录
系统 SHALL 在智能填充与批量回复执行完成后自动持久化可查询的执行记录。

#### Scenario: 智能填充执行后生成记录
- **WHEN** 用户完成一次智能填充执行
- **THEN** 系统保存一条任务级执行记录
- **AND** 记录中保留当前任务下各 Sheet 的逐行结果

#### Scenario: 批量回复执行后生成记录
- **WHEN** 用户完成一次批量回复执行
- **THEN** 系统保存一条任务级执行记录
- **AND** 记录详情中按文件拆分多个结果文件

### Requirement: 批量回复 API 支持来源与目标逐表独立映射
系统 SHALL 提供可同时表达来源逐表配置和目标逐表配置的批量回复 API，而不是把来源表配置直接复用到目标文件。

#### Scenario: 接收来源逐表配置
- **GIVEN** 用户已上传来源文件
- **WHEN** 前端提交来源表配置
- **THEN** 请求体中包含每张来源表的行配置和列映射
- **AND** 后端按该配置提取来源表中的项目、规格、验收和备注

#### Scenario: 接收目标逐表配置与来源表绑定
- **GIVEN** 用户已上传目标文件
- **WHEN** 前端提交某个目标文件的目标表配置
- **THEN** 请求体中包含每个目标表的行配置、列映射和 `sourceTableIndex`
- **AND** 后端按该 `sourceTableIndex` 选择来源表参与预览和执行

#### Scenario: Word 与 Excel 共用列映射语义
- **WHEN** 前端提交 Word 表格配置或 Excel 工作表配置
- **THEN** 后端都按统一的列映射字段解释项目列、规格列、验收列和备注列
- **AND** 不要求 Word 使用独立的固定列契约

### Requirement: 批量回复 API 支持逐表预览与按文件执行
系统 SHALL 允许客户端按目标表请求预览，并在执行时以单个目标文件为最小执行单元，而不是要求整批预检先通过。

#### Scenario: 逐表预览返回写回结果
- **GIVEN** 某个目标表已经绑定来源表并配置完成
- **WHEN** 前端调用该目标表的预览接口
- **THEN** 系统返回该目标表的逐行预览结果
- **AND** 返回行级状态、来源键、目标键、拟写回的验收值和备注值

#### Scenario: 乱序但键一致时允许预览与执行
- **GIVEN** 来源表与目标表的行顺序不同
- **AND** 两边按 `项目 + 规格` 归一化后的键集合一致
- **WHEN** 前端调用预览或执行接口
- **THEN** 系统允许继续处理该目标表
- **AND** 写回按目标行号落位

#### Scenario: 出现重复键时拒绝自动处理
- **GIVEN** 来源表或目标表中出现重复的 `项目 + 规格` 组合
- **WHEN** 前端调用预览或执行接口
- **THEN** 系统拒绝自动写回该目标表
- **AND** 返回要求用户手动处理的明确错误

#### Scenario: 仅执行配置完整的单个目标文件
- **GIVEN** 同一批次下多个目标文件处于不同配置进度
- **WHEN** 前端提交执行请求
- **THEN** 系统仅执行“所有参与表均配置完整且校验通过”的目标文件
- **AND** 不因其他目标文件未配置完成而整体拒绝请求

### Requirement: 批量回复单表预览支持重复键冲突决议
系统 SHALL 在批量回复单表预览接口中，把重复的“项目 + 规格”组合作为结构化冲突返回，并允许客户端提交逐组决议后重新生成当前表预览。

#### Scenario: 返回来源表重复键冲突
- **GIVEN** 当前目标表绑定的来源表中存在重复的“项目 + 规格”组合
- **WHEN** 前端调用批量回复单表预览接口且未提交该组决议
- **THEN** 系统返回 `canApply = false`
- **AND** 返回结构化冲突分组，包含冲突键、来源类型、涉及行号与候选写回值
- **AND** 不直接执行自动写回

#### Scenario: 返回目标表重复键冲突
- **GIVEN** 当前目标表数据区中存在重复的“项目 + 规格”组合
- **WHEN** 前端调用批量回复单表预览接口且未提交该组决议
- **THEN** 系统返回 `canApply = false`
- **AND** 返回结构化冲突分组，包含冲突键、目标类型、涉及行号与候选行
- **AND** 要求客户端先完成冲突处理

#### Scenario: 提交保留首条或末条决议后继续预览
- **GIVEN** 单表预览请求中已包含某个重复组的处理决议
- **WHEN** 用户选择“保留首条”或“保留末条”后重新提交预览
- **THEN** 系统按该决议消歧当前冲突组
- **AND** 若剩余校验通过，则返回可写回的逐行预览结果

#### Scenario: 提交跳过该组决议后继续预览
- **GIVEN** 某个重复组已选择“跳过该组”
- **WHEN** 前端重新调用单表预览接口
- **THEN** 系统不为该组生成写回结果
- **AND** 其余无冲突行仍返回预览结果
- **AND** 响应包含该组已被跳过的说明

### Requirement: 执行记录详情接口返回智能填充完整回放归档
系统 SHALL 在智能填充执行记录详情中返回完整回放归档，以同时表达执行前预览结果和执行时最终选择结果。

#### Scenario: 智能填充详情返回双快照
- **GIVEN** 某条执行记录的任务类型为 `smart-fill`
- **AND** 该记录已按新版本归档保存完整回放信息
- **WHEN** 前端请求执行记录详情接口
- **THEN** 系统返回 `previewSnapshot` 与 `executionSnapshot`
- **AND** 每行同时包含匹配来源、人工确认、人工写入和最终写回值所需字段

#### Scenario: 批量回复详情保持简化
- **GIVEN** 某条执行记录的任务类型为 `batch-reply`
- **WHEN** 前端请求执行记录详情接口
- **THEN** 系统返回文件、表格和逐行写回结果
- **AND** 不要求返回智能填充候选、AI 复核或匹配回放字段

### Requirement: 执行记录列表接口返回任务选择与摘要字段
系统 SHALL 在执行记录列表中返回任务下拉和摘要卡所需的结构化字段，而不是要求前端扫描详情 JSON 自行统计。

#### Scenario: 智能填充任务返回分类汇总
- **GIVEN** 某条执行记录的任务类型为 `smart-fill`
- **WHEN** 前端请求执行记录列表
- **THEN** 系统返回该任务的完全匹配数、AI匹配数、人工确认数、人工写入数以及未采用或未匹配数
- **AND** 这些字段可直接用于任务下拉项和摘要卡展示

### Requirement: 执行记录详情接口不得为展示重跑匹配或 AI
系统 MUST 基于已归档的执行记录数据返回详情，而不得为了历史详情展示重新调用 AI 或重新执行匹配。

#### Scenario: 查看新版本智能填充详情
- **GIVEN** 执行记录已保存完整回放归档
- **WHEN** 前端请求详情接口
- **THEN** 系统直接读取已保存的归档数据
- **AND** 不重新触发匹配、AI 重排、AI 等价裁决或 LLM 复核

#### Scenario: 查看历史旧记录详情
- **GIVEN** 历史执行记录缺少完整回放归档
- **WHEN** 前端请求详情接口
- **THEN** 系统返回降级可用的旧结构详情和能力标记
- **AND** 不通过补算或后台重建来伪造完整回放数据

### Requirement: 数据库备份配置接口
系统 SHALL 提供数据库备份配置接口，用于读取当前配置、保存页面配置并手动触发一次备份。

#### Scenario: 读取备份配置
- **WHEN** 管理员访问数据库备份配置页面
- **THEN** API 返回当前备份配置、最近一次执行状态和可见备份文件列表

#### Scenario: 保存备份配置
- **WHEN** 管理员保存备份配置
- **THEN** API 持久化配置并返回保存后的当前配置

#### Scenario: 手动触发备份
- **WHEN** 管理员点击立即备份
- **THEN** API 执行一次数据库备份并更新最近一次执行状态

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

### Requirement: 智能填充执行历史完整回放读取 API
系统 SHALL 提供按执行记录读取智能填充完整回放明细的只读 API，并且只返回当前用户有权访问的记录。

#### Scenario: 按行读取完整匹配详情
- **GIVEN** 某条智能填充执行记录存在完整回放归档
- **WHEN** 前端按文件、Sheet 与行号请求该行详情
- **THEN** 系统返回该行执行时归档的源文本、最佳匹配、候选列表、证据、问题项、AI 裁决和最终写回值
- **AND** 系统不重新执行匹配或 AI 调用

#### Scenario: 拒绝读取无权记录
- **GIVEN** 当前用户不拥有目标执行记录
- **WHEN** 用户请求该记录的完整回放详情
- **THEN** 系统返回未找到或无权访问结果

#### Scenario: 旧记录缺少完整归档
- **GIVEN** 某条历史智能填充记录没有完整回放归档
- **WHEN** 前端请求该记录的完整行详情
- **THEN** 系统返回明确的归档缺失错误
- **AND** 不尝试通过重新匹配补算详情

### Requirement: 智能结构识别 API
系统 SHALL 提供智能结构识别 API，用于对已上传 Word 或 Excel 文件输出全文档表格结构识别结果。

#### Scenario: 识别返回扁平表格结构
- **GIVEN** 用户已上传 Word 或 Excel 文件
- **AND** 用户已选择客户
- **WHEN** 前端调用 `POST /api/smart-config/recognize`
- **THEN** 响应包含数字类型 `fileId`
- **AND** 响应包含扁平 `tables` 数组
- **AND** 每个表包含 `tableIndex`、`tableName`、`headers`、表头行、数据范围、四列识别结果、字段来源、字段置信度和决策状态
- **AND** 响应不使用 Sheet/Tables 二级结构

#### Scenario: Excel 索引口径清晰
- **GIVEN** 识别结果来自 Excel 文件
- **WHEN** API 返回行列索引
- **THEN** 识别结果中的行列索引使用解析后表格的 0-based 相对索引
- **AND** 调用现有 Excel 导入接口前必须转换为 1-based 工作表绝对坐标

#### Scenario: 识别失败可降级
- **GIVEN** 识别过程中 LLM 超时、解析失败或服务异常
- **WHEN** 系统构造识别响应
- **THEN** 系统返回需要确认或失败状态
- **AND** 不阻断用户进入现有手动配置流程

### Requirement: 智能结构确认 API
系统 SHALL 提供智能结构确认 API，用于接收用户确认后的最终配置并触发模板与学习词沉淀。

#### Scenario: 确认后沉淀学习结果
- **GIVEN** 用户在确认卡或预览页确认识别结果
- **WHEN** 前端调用 `POST /api/smart-config/confirm`
- **THEN** 系统保存或更新客户级结构模板
- **AND** 系统为用户修正过的列写入客户域学习词
- **AND** 响应返回学习是否成功

#### Scenario: 学习失败不阻断当前流程
- **GIVEN** 当前业务导入或填充配置已经确认
- **WHEN** 模板或学习词沉淀失败
- **THEN** API 记录失败日志
- **AND** 不要求当前业务流程失败

### Requirement: 智能结构识别 API 权限受控
系统 MUST 对智能结构识别与确认 API 执行权限校验。

#### Scenario: 缺少权限被拒绝
- **WHEN** 已登录用户缺少智能结构识别或文档导入相关 API 权限
- **THEN** 系统返回 403
- **AND** 响应包含缺少的权限码

### Requirement: 智能结构识别按需表头裁决
系统 SHALL 在智能配置识别接口中，仅当规则表头识别不确定或结构健康检查降级时，按需调用 LLM 裁决表头结构。

#### Scenario: 规则明确时不调用 LLM
- **GIVEN** Word 或 Excel 表格的规则表头识别结果置信明确
- **WHEN** 用户调用 `POST /api/smart-config/recognize`
- **THEN** 系统使用规则识别结果继续列映射
- **AND** 不调用 LLM 表头裁决

#### Scenario: 不确定表头触发裁决
- **GIVEN** 规则表头候选分数接近、低置信或结构健康检查结果为 `NeedConfirm`
- **WHEN** 文档仍有 LLM 结构裁决预算
- **THEN** 系统向 LLM 提交表格预览、规则候选和参考模板
- **AND** LLM 仅返回表头结构字段与置信说明

#### Scenario: 合法裁决重新提取表格
- **GIVEN** LLM 返回的 `headerRowIndex`、`headerRowCount` 和 `dataStartRowIndex` 均在表格范围内
- **WHEN** 系统接受该裁决
- **THEN** 系统按该表头结构重新提取 Word 或 Excel 表格
- **AND** 重新执行列映射与结构健康检查

#### Scenario: 非法裁决回退规则结果
- **GIVEN** LLM 返回的表头结构越界、行数无效或数据起始行早于表头结束
- **WHEN** 系统校验裁决结果
- **THEN** 系统丢弃该裁决
- **AND** 保留规则识别结果并返回待确认状态

#### Scenario: 预算耗尽不调用裁决
- **GIVEN** 当前文档的 `MaxStructureAdjudicationCallsPerDocument` 预算为 0 或已耗尽
- **WHEN** 规则表头识别不确定
- **THEN** 系统不调用 LLM 表头裁决
- **AND** 返回规则识别与健康检查结果

### Requirement: 智能结构识别返回表格推荐信息
系统 SHALL 在智能结构识别 API 响应中为每张表返回表格类型、推荐级别、排序分和结构化原因。

#### Scenario: 返回推荐字段
- **GIVEN** 用户已上传 Word 或 Excel 文件
- **WHEN** 前端调用 `POST /api/smart-config/recognize`
- **THEN** 每个表格结果包含 `tableKind`
- **AND** 每个表格结果包含 `recommendation`
- **AND** 每个表格结果包含 `rankingScore`
- **AND** 每个表格结果包含结构化 `issues`

#### Scenario: 建议跳过仍保留表格结果
- **GIVEN** 某张表被识别为报价、Layout、Utility、备品清单或签核页
- **WHEN** API 返回识别结果
- **THEN** 该表仍出现在 `tables` 数组中
- **AND** `recommendation` 为 `Skip`
- **AND** 响应包含用户可读的跳过原因

### Requirement: 智能结构识别新增字段保持兼容
系统 MUST 保持智能结构识别 API 的既有字段兼容，新增推荐字段不得破坏旧流程。

#### Scenario: 旧字段仍可用于导入配置
- **WHEN** 前端只读取既有表头、行范围、字段列索引和决策字段
- **THEN** 新增推荐字段不会改变这些既有字段的含义
- **AND** 旧的手动配置兜底流程仍可使用

### Requirement: 智能结构路由规则配置 API
系统 SHALL 提供智能结构路由规则配置 API，用于人工维护、客户级隔离和确认学习结果审阅。

#### Scenario: 管理路由规则
- **WHEN** 前端调用智能结构路由规则 API
- **THEN** 系统支持查询、新增、更新、删除路由规则
- **AND** 规则字段包含名称、表格类型、推荐结果、匹配范围、匹配方式、匹配词、权重、优先级、启停状态、来源和客户域

#### Scenario: 查询客户有效规则
- **GIVEN** 系统存在全局规则和客户级规则
- **WHEN** 前端或识别服务按客户查询有效规则
- **THEN** API 返回启用的全局规则和该客户规则
- **AND** 其他客户的客户级规则不得出现在结果中

### Requirement: 列映射规则管理 API
系统 SHALL 提供列映射规则管理 API，用于维护 Word 表头和智能结构识别所需的全局、客户级及不同来源规则。

#### Scenario: 读取生效规则
- **WHEN** 客户端访问 `/api/column-mapping-rules/effective`
- **THEN** 系统返回当前上下文可见的启用规则
- **AND** 结果按目标字段、客户优先级与规则优先级排序

#### Scenario: 管理规则 CRUD
- **WHEN** 已授权客户端访问 `/api/column-mapping-rules`
- **THEN** 系统支持列映射规则的查询、新增、更新和删除
- **AND** 对非法正则表达式或空匹配词返回明确错误

### Requirement: 列映射规则恢复默认词 API
系统 SHALL 提供 `POST /api/column-mapping-rules/restore-defaults` 接口，用于按词补齐缺失的内置（Builtin、全局）表头字段默认词。接口 SHALL 支持可选 `targetField` 参数以仅恢复指定字段；SHALL NOT 增删改手动、学习或客户级规则。接口 SHALL 受管理接口权限授权约束。

#### Scenario: 恢复全部字段默认词
- **WHEN** 已授权用户调用 restore-defaults 且不带 `targetField`
- **THEN** 所有内置全局词已缺失的字段被重新补齐，返回成功

#### Scenario: 仅恢复指定字段
- **WHEN** 已授权用户调用 restore-defaults 并指定 `targetField`
- **THEN** 仅该字段缺失的内置默认词被补齐，其余字段不受影响

#### Scenario: 不影响用户自定义规则
- **WHEN** restore-defaults 执行
- **THEN** 手动、学习与客户级规则保持不变

### Requirement: 智能结构识别返回列语义召回建议
系统 SHALL 在智能配置识别接口中，以可选字段返回列语义召回建议，用于辅助用户确认未命中规则的表头。

#### Scenario: 返回未映射表头的语义建议
- **GIVEN** 表格规则列映射缺少关键字段
- **AND** 存在未映射的短表头文本
- **WHEN** 用户调用 `POST /api/smart-config/recognize`
- **THEN** 响应中包含该表头的候选目标字段、置信度、理由和来源
- **AND** 既有列索引字段保持规则识别结果，不因建议字段而静默改变含义

#### Scenario: 规则完整命中时不返回额外建议
- **GIVEN** 表格通过确定性规则或历史模板已经识别出关键列
- **WHEN** 用户调用 `POST /api/smart-config/recognize`
- **THEN** 系统可以不返回列语义召回建议
- **AND** 不因 AI 召回改变原有自动采用判断

#### Scenario: AI 建议需要用户确认
- **GIVEN** 某个关键字段只由列语义召回建议补齐
- **WHEN** 系统返回智能结构识别结果
- **THEN** 该表格推荐状态为需要确认
- **AND** 用户确认后才可保存模板和沉淀学习规则

### Requirement: 仅规格导入项目回填
系统 SHALL 在导入接口中支持明确仅规格表，并在缺少项目列且满足门禁时使用规格值补齐项目值。

#### Scenario: 明确仅规格表导入
- **GIVEN** 导入配置标记当前表为仅规格
- **AND** 规格列存在且数据健康
- **AND** 项目列为空
- **WHEN** 用户执行导入
- **THEN** 系统将每行 `Project` 写为该行 `Specification`
- **AND** 系统将 `Specification` 保持为该行规格文本
- **AND** 不要求用户额外提供项目列

#### Scenario: 疑似漏识别项目列时拒绝自动回填
- **GIVEN** 项目列为空
- **AND** 表头或样本中存在疑似项目列
- **WHEN** 用户执行导入或请求导入预览
- **THEN** 系统不得自动使用规格补项目
- **AND** 系统返回需要人工确认或参数错误，提示用户选择项目列或确认仅规格

#### Scenario: 用户确认仅规格后导入
- **GIVEN** 系统无法自动确认仅规格
- **AND** 用户在确认界面明确选择仅规格导入
- **WHEN** 用户执行导入
- **THEN** 系统允许使用规格值补齐项目值
- **AND** 响应或预览中包含项目由规格补齐的提示
