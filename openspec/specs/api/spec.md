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

### Requirement: 智能填充严格复用预检与执行 API
系统 SHALL 提供基于当前填充结果的一次性严格复用 API，用于多目标文件的预检和批量执行。

#### Scenario: 严格复用预检
- **GIVEN** 用户刚完成一份验规文件的智能填充
- **AND** 前端提交当前填充任务标识与多个目标文件
- **WHEN** 系统执行严格复用预检
- **THEN** 系统逐个文件校验文件类型、表格配置、数据区行数以及项目+规格顺序是否与来源完全一致
- **AND** 系统返回每个文件的“可应用 / 不可应用”状态与失败原因
- **AND** 系统不写入任何目标文件

#### Scenario: 严格复用执行
- **GIVEN** 目标文件已经通过严格复用预检
- **WHEN** 前端提交执行请求
- **THEN** 系统直接使用来源填充结果中已确认的验收与备注值写回目标文件
- **AND** 系统不重新匹配、不调用 AI、不要求用户重新逐行确认
- **AND** 系统返回批量执行结果与下载入口

#### Scenario: 结构不一致时拒绝复用
- **GIVEN** 某个目标文件与来源文件的表格配置、数据区行数或项目+规格顺序存在差异
- **WHEN** 系统执行严格复用预检或执行
- **THEN** 系统拒绝对该文件写回
- **AND** 系统返回具体差异原因

### Requirement: 严格复用会话不作为长期模板持久化管理
系统 SHALL 将严格复用限制为基于当前填充结果的临时会话能力，而不是长期模板能力。

#### Scenario: 仅允许基于当前填充结果发起复用
- **GIVEN** 用户位于一次智能填充完成后的操作上下文
- **WHEN** 用户发起严格复用
- **THEN** 系统只允许引用当前填充结果对应的临时会话数据
- **AND** 系统不要求用户从历史模板列表中选择

### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
系统 SHALL 以服务端当前匹配结果和决策门禁为准，不再暴露或信任旧的 suggestion / compatibility 字段。

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** 客户端调用智能填充预览接口
- **THEN** 预览配置与结果仅暴露召回、歧义、实体判别、复核与等价裁决相关字段
- **AND** 不再暴露 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows`、`LlmSuggestionScoreThreshold` 或 `LlmSuggestion`

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求仅允许提交当前文件定位、目标列、匹配范围、匹配配置和用户确认映射
- **AND** 服务端在执行前按当前文件与配置重算门禁
- **AND** 不要求也不接受 `SourceFileId`、`SourceTableIndex`、`SelectedSpecId`、`Acceptance`、`Remark` 或其他旧兼容透传字段
- **AND** 当请求携带这些旧字段时，接口在请求解析阶段直接返回 `400 Bad Request`，而不是静默忽略

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
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，而不是对外可编辑的配置 API。

#### Scenario: 客户端访问旧匹配知识接口
- **WHEN** 客户端访问 `/api/matching-knowledge`、`/api/matching-knowledge-drafts/generate`、`/api/matching-knowledge/clear`、`/api/matching-knowledge/restore-defaults`、`/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
- **THEN** 系统不再提供这些旧配置接口

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
- **WHEN** 客户端调用匹配预览、执行填充、下载结果或严格复用接口
- **THEN** 控制器委派独立 Application 用例服务执行
- **AND** 不再依赖单个全能匹配工作流服务

#### Scenario: RBAC 控制器委派
- **WHEN** 客户端调用系统用户、角色、组织或权限字典接口
- **THEN** 控制器通过对应 Application 用例服务或查询服务完成处理

