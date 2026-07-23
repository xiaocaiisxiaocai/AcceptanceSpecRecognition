## Context

仓库已经有显式 Application 项目和架构规格，但当前实现存在三类“声明完成、边界未闭合”的问题：

1. **物理所有权不真实**：Application 通过 MSBuild Link 编译 Api DTO 与 Data provider 源文件，项目引用图无法代表源码依赖。
2. **协议层仍编排业务和持久化**：多个控制器与审计过滤器直接取得 `IUnitOfWork`，工作流仍集中在 `Api/Services`，Application 无法成为唯一用例入口。
3. **运行生命周期缺少独立治理**：临时 manifest 清理依赖请求流量；下载、解析、写回和匹配缺少统一资源预算；容器和 CI 不能充分模拟生产边界。

该变更是架构与运行治理提案，不在本阶段实施代码。后续实施必须按下面的阶段逐批取得验证证据。

## Goals / Non-Goals

### Goals

- 让项目引用、源码目录、编译归属和运行依赖保持一致。
- 让 Api 只负责 HTTP/SSE/下载协议适配，Application 负责用例编排和事务边界。
- 按业务模块迁移，而不是把现有巨型服务整体改名或机械搬家。
- 为临时数据、长耗时文档 I/O、匹配并发和大响应建立明确生命周期与资源预算。
- 让生产镜像具备最小权限和可复现基础依赖。
- 让真实 MySQL、浏览器关键路径和覆盖率趋势成为持续可见的质量信号。

### Non-Goals

- 不改变现有匹配算法、阈值、AI 裁决语义或候选排序规则。
- 不重设计页面，不更换前端框架或组件库。
- 不改变现有 API 路径、公开 DTO 或数据库业务 Schema；必须发生的破坏性变更另立提案。
- 不引入分布式锁、消息队列、Kubernetes 或远程对象存储。
- 不用 hosted cleanup 代替文件归属、事务补偿或离机备份。
- 不在此变更中删除真实业务样本、重写 Git 历史或归档其他 OpenSpec change。
- 不要求第一批就达到任意覆盖率百分比；先建立可信基线和不回退趋势。

## Decisions

### Decision 1: 先闭合物理编译边界，再迁移业务模块

第一阶段先为 DTO、应用端口和 provider adapter 确定唯一所有者，移除跨项目 `Compile Include/Link`。共享契约应放入 Application 自有目录或单独的中立契约项目；Data provider adapter 若包含 Core/Application 语义，应迁移到 Application，不允许复制同一源码到多个项目编译。

架构测试必须直接解析 `.csproj` 和源码依赖，阻止以下回归：

- Application 编译 Api/Data 目录下的源码；
- Api 绕过 Application 直接引用 Data/Core 项目；
- 控制器、filter、中间件直接注入 `IUnitOfWork`、Repository 或 `AppDbContext`。

### Decision 2: 以垂直用例批次迁移 `Api/Services`

迁移顺序按依赖风险从低到高推进：

1. 配置查询、审计写入与基础管理用例；
2. 文档上传、预览、导入、比较和下载；
3. BatchReply 会话、预览、执行与下载；
4. 匹配预览、LLM 流、执行填充、任务快照与执行历史；
5. RBAC、认证、权限种子和后台运维工作流。

每个批次采用“Application 端口 + Application 实现 + Api 适配器”的切片方式。控制器一次只依赖一个用例入口；跨用例共享能力拆为小型协作组件，不创建新的全能服务。迁移期间允许短期 façade 委派，但 façade 不得继续持有持久化编排，且必须有删除任务和架构守卫。

### Decision 3: 事务、审计与数据范围由 Application 明确拥有

Application 用例决定事务范围，并通过 Repository/UnitOfWork 或专用 query port 访问 Data。审计 filter 只收集协议上下文并调用审计端口；不能直接保存数据库。数据范围解析作为 Application 输入或用例依赖，不在控制器内组合查询。

### Decision 4: BatchReply 清理由独立 hosted service 驱动

新增单实例 hosted cleanup，按配置周期扫描 session 与 artifact manifest：

- 宿主启动后按周期运行，不依赖用户请求；
- 使用宿主 `CancellationToken`，停止时不启动新一轮扫描；
- 同一实例内防止扫描重入，并与活动 session mutation 使用一致的 keyed coordination；
- 删除前重新读取 manifest 时间与所有权信息，保持幂等；
- 单个文件失败不终止整轮，记录结构化计数和错误；
- 保留请求路径中的轻量到期校验，但移除以业务请求触发全目录扫描的职责。

不在本次引入跨实例分布式锁。若未来部署多个 API 副本，必须另行设计共享存储清理协调。

### Decision 5: 下载流式化，文档 I/O 使用统一资源预算

- 下载端点返回文件流或框架原生流式结果，不先把完整文件读入 `byte[]`；流在响应结束或取消时释放。
- parser/writer 接口接收并传播 `CancellationToken`，在表格/行批次边界检查取消。
- 通过配置化并发闸门限制同时进行的 Word/Excel 解析、写回和高成本匹配任务；等待闸门同样可取消。
- 明确单文件大小、解压大小、行列/单元格与并行任务预算；超限返回可理解错误，不以 OOM 或长时间占核作为流控手段。
- 预算值先基于现有 50MB 上传上限和压测数据确定，之后通过配置调整，不改变匹配判定语义。

### Decision 6: 生产镜像最小权限与基础镜像锁定

- build/runtime 基础镜像固定到明确版本并以 digest 锁定；升级由依赖更新任务显式完成。
- API 与 Web runtime 创建专用非 root 用户；仅对 `/data/files`、DataProtection keys、备份目录及必要 Nginx 运行目录授予最小写权限。
- 健康检查必须在非 root 身份下可运行。
- CI 构建镜像后验证运行用户、健康检查、只读目录与持久卷写入，不只验证 `docker build` 成功。

### Decision 7: 质量门禁分为“信号建立”和“阻止回退”

- 真实 MySQL job 执行 migration、关键 repository/query 与时区/排序/唯一约束测试；SQLite 仍保留为快速反馈，不再冒充生产等价验证。
- 浏览器 E2E 覆盖登录、文档上传/识别/导入、智能填充确认/下载、BatchReply 和权限拒绝等关键路径；用合成 fixture，不依赖真实业务样本。
- 后端与前端生成可机读覆盖率报告并保留 CI artifact。首批记录基线；后续对变更代码或总体趋势设置“不回退”门禁，再单独评审是否提高阈值。
- 失败测试必须保存必要日志、截图或 trace，同时对敏感配置脱敏。

## Phased Delivery

### Phase 0: 基线与架构守卫

- 固化当前 API 契约、关键工作流和性能基线。
- 新增 csproj/source 边界守卫，列出所有跨项目 Link 与协议层持久化依赖。
- 建立覆盖率报告但暂不设历史无法满足的绝对阈值。

### Phase 1: 物理 Application 边界

- 迁移 DTO/端口/provider adapter 到唯一归属。
- 删除全部 Api/Data 跨项目 Compile Link。
- 使 Api 项目引用和源码 using 满足正式依赖方向。

### Phase 2: 垂直模块迁移

- 按配置/审计、文档、BatchReply、匹配、RBAC 顺序迁移。
- 每个模块完成控制器/filter 去持久化依赖、Application 用例测试和 API 兼容回归后再进入下一模块。

### Phase 3: 运行生命周期与资源预算

- 上线 BatchReply hosted cleanup。
- 流式化下载；为 parser/writer/matching 增加取消传播、并发闸门与预算测试。
- 通过故障注入验证取消、部分失败、补偿和宿主停止。

### Phase 4: 容器加固

- 锁定基础镜像、切换非 root、修正卷权限和健康检查。
- 对镜像执行身份与持久卷 smoke test。

### Phase 5: 生产等价与趋势门禁

- 增加真实 MySQL job 与浏览器 E2E job。
- 发布覆盖率 artifact，记录基线并启用不回退策略。
- 完成全链路回归、运行手册和回滚演练。

## Migration Plan

1. 为外部 API、数据库迁移和关键页面建立基线测试，记录当前覆盖率与资源使用。
2. 创建 Application 自有契约目录/端口，先复制语义并用委派保持行为一致；新旧实现不能同时写入。
3. 逐个移除 Link，迁移一个源码所有权就删除一个旧编译入口，持续运行架构测试。
4. 按模块将控制器/filter 切换到新用例；每批使用兼容 façade 保留原 HTTP 契约，验收后删除 façade。
5. 部署 hosted cleanup 时先启用“扫描并记录、不删除”观察窗口，再开启删除；保留开关以便紧急停用清理，不回退会话读写格式。
6. 下载流式化和 I/O 并发预算分别灰度；保留旧端点契约，回滚只切换内部实现。
7. 构建非 root 新镜像并以新标签部署；卷权限 smoke 通过后切流。失败时回滚旧镜像标签，不修改数据卷。
8. CI 新 job 先以可见但非阻断方式稳定运行，消除 flaky 后再按阶段设为 required；覆盖率先阻止回退，再评审目标值。

## Rollback Plan

- **边界迁移**：以模块为单位回退到上一个 façade 实现；不得恢复跨项目 Link 作为长期方案，若紧急恢复必须立即登记后续删除任务。
- **hosted cleanup**：关闭 cleanup 开关即可停止新扫描；已删除的过期临时文件不承诺恢复，因此启用删除前必须完成观察窗口。
- **资源预算/流式下载**：回退内部实现或提高预算配置，保持 API 路径和 Content-Disposition 不变。
- **容器**：回滚到上一不可变镜像标签；禁止用重建卷作为回滚手段。
- **CI 门禁**：仅在确认基础设施故障时临时降为非阻断，并保留失败信号和恢复期限。

## Risks / Trade-offs

- **迁移面过大导致长期双轨**：按垂直模块设置“旧 façade 删除”完成条件，未删除不得标记该阶段完成。
- **DTO 所有权迁移引起序列化漂移**：保留 JSON 契约快照和 API 集成测试，显式验证属性名、空值和枚举值。
- **清理任务与活跃会话竞争**：使用一致的 keyed coordination、删除前二次校验和幂等删除；多实例场景明确不在本次承诺。
- **取消支持受第三方同步库限制**：在表格/行批次边界检查取消并用并发预算限制最坏占用，不伪称能中断库内部不可取消的单次调用。
- **非 root 镜像出现卷权限问题**：CI 以实际挂载目录执行写入/读取/备份 smoke，部署前验证现有卷 UID/GID 策略。
- **真实 MySQL/E2E 增加 CI 时间和 flaky**：快速单测与生产等价 job 分层，固定合成 fixture，保存 trace，并在稳定后再设 required。
- **覆盖率数字驱动无价值测试**：只把趋势作为最低护栏，代码评审仍关注关键分支、故障注入和行为覆盖。

## Open Questions

- Application 对外 DTO 是直接归属 Application，还是新增轻量 Contracts 项目；实施 Phase 1 前需以依赖图和序列化兼容成本作出决策。
- hosted cleanup 的默认观察窗口和扫描周期应基于线上 manifest 数量、磁盘容量及现有保留时长确定。
- Word/Excel 解析与写回的初始并发预算需通过目标部署机器压测确定，不能直接照搬开发机核数。
- 覆盖率“不回退”按总体、变更行还是关键模块计算，需在 Phase 0 基线报告后确认。
