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

### Requirement: 匹配知识 AI 草稿生成 API
系统 SHALL 提供匹配知识 AI 草稿生成接口，支持按单个分类基于历史验规筛选结果生成可审核候选项。

#### Scenario: 按单个分类生成草稿
- **GIVEN** 请求中指定分类为 `entityAliases`
- **WHEN** 前端调用匹配知识 AI 草稿生成接口
- **THEN** 系统只返回实体别名候选草稿
- **AND** 不返回单位规则、字段别名或冲突词对候选

#### Scenario: 仅接受历史验规筛选条件
- **GIVEN** 请求中包含客户、制程、机型、关键词或导入时间范围中的任意组合
- **WHEN** 系统执行草稿生成
- **THEN** 系统仅基于当前用户可访问且符合筛选条件的历史验规生成候选结果
- **AND** 不读取粘贴文本或上传文档作为输入来源

#### Scenario: 当前筛选条件没有命中历史验规
- **GIVEN** 当前筛选条件下没有可访问的历史验规
- **WHEN** 前端调用匹配知识 AI 草稿生成接口
- **THEN** 系统返回明确错误并提示用户调整筛选条件

#### Scenario: 当前筛选结果超过系统安全上限
- **GIVEN** 当前筛选结果数量或拼接文本超过系统安全上限
- **WHEN** 系统执行草稿生成
- **THEN** 系统返回明确错误并提示用户收窄筛选条件
- **AND** 不允许静默截断后继续生成

#### Scenario: 返回结构化候选与状态
- **WHEN** 系统返回草稿生成结果
- **THEN** 每条候选包含值、标准值或配对值、命中片段、生成理由和状态
- **AND** 状态至少覆盖“可导入”“重复忽略”“冲突待确认”

#### Scenario: 不直接持久化草稿
- **WHEN** 前端调用匹配知识 AI 草稿生成接口
- **THEN** 系统仅返回草稿结果
- **AND** 不直接修改数据库中的匹配知识配置

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

### Requirement: 统一匹配知识配置 API
系统 SHALL 提供统一的匹配知识配置 API，用于读取、保存和重置当前生效的结构化匹配知识。

#### Scenario: 读取当前匹配知识配置
- **WHEN** 前端发送 `GET /api/matching-knowledge`
- **THEN** 系统返回当前生效的实体别名、单位别名、单位换算、字段别名和冲突词对配置

#### Scenario: 保存匹配知识配置
- **GIVEN** 用户已编辑匹配知识配置
- **WHEN** 前端发送 `PUT /api/matching-knowledge`
- **THEN** 系统校验并持久化整套配置
- **AND** 后续匹配请求读取更新后的配置

#### Scenario: 重置为系统默认配置
- **WHEN** 前端发送 `POST /api/matching-knowledge/reset`
- **THEN** 系统将当前匹配知识恢复为系统默认配置
- **AND** 返回重置后的完整配置

#### Scenario: 旧配置接口移除
- **WHEN** 客户端访问 `/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
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

### Requirement: 管理接口角色授权
系统 MUST 对管理类接口执行 `admin` 角色授权。

#### Scenario: 普通角色访问管理接口
- **WHEN** `common` 角色用户访问管理接口
- **THEN** 系统返回 `403 Forbidden`

#### Scenario: 未登录访问管理接口
- **WHEN** 未携带有效登录身份访问管理接口
- **THEN** 系统返回 `401 Unauthorized`

---

### Requirement: 系统用户管理API
系统 SHALL 提供受 `admin` 角色保护的系统用户管理接口。

#### Scenario: 查询系统用户列表
- **WHEN** 管理员请求系统用户列表接口
- **THEN** 系统返回分页用户数据，包含账号启用状态与角色权限信息

#### Scenario: 创建系统用户
- **WHEN** 管理员提交合法的新用户信息（用户名、密码、角色）
- **THEN** 系统创建用户并返回用户详情

#### Scenario: 更新系统用户
- **WHEN** 管理员更新用户昵称、角色、权限或启用状态
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

