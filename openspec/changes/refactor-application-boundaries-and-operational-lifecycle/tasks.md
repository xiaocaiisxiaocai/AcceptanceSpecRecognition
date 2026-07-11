## 0. 基线与批准

- [x] 0.1 评审并批准本提案；在批准前不实施生产代码。
- [x] 0.2 记录现有项目依赖、跨项目 Compile Link、控制器/filter 持久化依赖和 `Api/Services` 模块清单。
- [x] 0.3 固化关键 API JSON 契约、数据库 migration、核心页面和资源使用基线。
- [x] 0.4 建立后端/前端覆盖率报告与 CI artifact，仅记录基线，暂不设置任意绝对阈值。

## 1. 物理 Application 边界

- [x] 1.1 决定 DTO/契约归属（Application 自有目录或中立 Contracts 项目）并记录依赖图。
- [x] 1.2 将 Data provider adapter 迁移到正确层次，删除 Application 对 Data 源文件的 Compile Link。
- [x] 1.3 迁移 Api DTO 链接源码并删除 Application 对 Api 源文件的全部 Compile Link。
- [x] 1.4 更新项目引用和 DI，使 `Api -> Application -> Core / Data` 与源码 using 一致。
- [x] 1.5 增加架构测试，禁止跨项目 Compile Link、Api 绕过 Application、协议层持久化依赖。
- [x] 1.6 更新 `openspec/project.md` 的架构约定，使其与正式 architecture spec 和实际项目依赖一致。

## 2. 协议层与应用用例迁移

- [x] 2.1 迁移配置查询、审计写入和基础管理用例；filter 仅调用 Application 审计端口。
- [x] 2.2 迁移文档上传、预览、导入、比较和下载用例，保持 HTTP/JSON 契约不变。
- [x] 2.3 迁移 BatchReply 会话、预览、执行和下载用例，保留现有会话与 manifest 兼容。
- [x] 2.4 迁移匹配预览、LLM 流、执行填充、任务快照与执行历史用例，不改变匹配决策。
- [x] 2.5 迁移认证、RBAC、权限种子与运维工作流。
- [x] 2.6 删除迁移期 façade 和对应旧 `Api/Services` 编排实现；确认控制器/filter 不直接依赖 `IUnitOfWork`、Repository 或 `AppDbContext`。

## 3. 生命周期、流式传输与资源预算

- [x] 3.1 实现可配置 BatchReply hosted cleanup，支持观察模式、宿主取消、防重入、结构化指标与单文件故障隔离。
- [x] 3.2 移除业务请求触发全目录过期扫描的职责，保留单会话到期校验。
- [x] 3.3 将大文件下载改为流式响应并验证连接取消时释放文件句柄。
- [x] 3.4 为 Word/Excel parser 与 writer 增加 CancellationToken，并在可控批次边界检查取消。
- [x] 3.5 为解析、写回和高成本匹配建立配置化并发闸门、输入预算及可观测等待指标。
- [x] 3.6 增加带安全宽限期的文件/元数据巡检，只清理能够证明无数据库引用的孤儿文件，并记录保留/清理指标。
- [x] 3.7 增加故障注入与压力测试，覆盖宿主停止、等待取消、提交结果不确定、超预算、部分文件失败和资源释放。

## 4. 容器与部署加固

- [x] 4.1 将 .NET、Node、Nginx 等基础镜像固定到明确版本和 digest，并记录升级流程。
- [x] 4.2 为 API/Web runtime 配置专用非 root 用户和最小目录权限。
- [x] 4.3 验证文件、DataProtection keys、数据库备份卷及 Nginx 运行目录在非 root 下可用。
- [x] 4.4 增加镜像运行用户、健康检查、只读目录和卷读写 smoke test。
- [x] 4.5 演练以不可变旧镜像标签回滚且不删除数据卷。

## 5. 生产等价验证与质量趋势

- [x] 5.1 CI 启动真实 MySQL 8，执行 migration、关键 repository/query、时区、排序和唯一约束测试。
- [x] 5.2 使用合成 fixture 建立浏览器 E2E：登录、导入、智能填充确认/下载、BatchReply 和权限拒绝。
- [x] 5.3 保存 E2E 失败截图/trace 与脱敏日志，并治理 flaky 后设为 CI 阻断门禁。
- [x] 5.4 发布后端和前端覆盖率 artifact，确定总体或变更行“不回退”策略并记录例外审批。
- [x] 5.5 运行全量后端、前端、真实 MySQL、浏览器 E2E、镜像 smoke 和 OpenSpec strict 验证。
- [x] 5.6 更新架构图、部署手册、故障处理与回滚手册；所有阶段证据齐全后才勾选完成。
