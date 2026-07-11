# 生产等价验证记录（2026-07-11）

本记录对应 OpenSpec `refactor-application-boundaries-and-operational-lifecycle` 的容器、真实 MySQL、浏览器 E2E 与回滚阶段。所有验证均使用一次性 `codex-*` 容器、卷、端口和凭据；未停止或修改本机已有 `acceptance-api`、`acceptance-web`、`acceptance-mysql`，未执行 `docker compose down -v`。

## 1. 验证环境

- Docker Engine 29.2.1（Linux containers）
- Docker Compose 5.1.0
- Buildx 0.32.1
- MySQL 8.0 固定 digest：`sha256:7dcddc01f13bab2f15cde676d44d01f61fc9f99fe7785e86196dfc07d358ae2b`
- Chromium E2E 使用 Playwright 1.61.0、Vite 7.3.6，CI 单 worker 模式

## 2. 镜像与非 root smoke

标准仓库上下文构建成功；补齐 `.dockerignore` 后 build context 从约 595 MB 降到不足 0.5 MB。

| 镜像 | 本地验证镜像 ID | 运行用户 | 结果 |
|---|---|---|---|
| API | `sha256:f15d2f85e0dc0b56ce0efa8cabdd15024db526e950119a90b61725c2cd0d8758` | UID/GID 1654 | 只读根文件系统下可写三个持久卷，并在隔离 MySQL 上完成迁移后进入 `healthy` |
| Web | `sha256:03a106a6b0600f7a37e70d63bb3a9850c98a63e9802b519994ed821ef7986e68` | UID/GID 101 | 只读根文件系统及受控 tmpfs 下可通过 8080 提供页面 |

最终 smoke 在隔离 Docker network 中以 `api` 网络别名连接 Web/Nginx 与 API，真实经 Nginx `/login` 代理验证：Refresh Cookie 包含 `HttpOnly; SameSite=Strict`，不含 `Secure`/`Domain`，JSON 不含 RefreshToken。API/Web 均以只读根文件系统和非 root 用户运行。

## 3. 真实 MySQL 合约

完整 Data 测试在固定 digest MySQL 8.0.46 上执行：87 通过，4 个只适用于另一测试模式的事务测试按条件跳过，0 失败。

验证范围：

- 完整 migration chain 及最新迁移 ID；
- 数据库和全部文本列为 `utf8mb4_unicode_ci`；
- UTC 时区；
- Emoji 往返；
- 大小写不敏感唯一约束；
- ColumnMappingRule repository 排序。

实测发现历史初始迁移会让 MySQL 8 采用 `utf8mb4_0900_ai_ci`。新增 `20260711010000_EnforceDatabaseCollation`，同时修复全新迁移链和既有数据库默认/历史表列排序规则。

该迁移会转换既有文本表，生产大库可能发生表重建、metadata lock 等待或唯一值排序语义冲突。上线前必须在数据副本执行容量与冲突预检、完成可恢复备份，并安排维护窗口；本次空库/合成数据验证不等同于大数据量在线迁移保证。

## 4. 浏览器 E2E

最终全套在 Production 显式内网 HTTP 配置、真实 MySQL 8 和 Vite HTTP 入口下 10/10 通过：

- 登录、HttpOnly Cookie 会话恢复；
- 普通用户页面隐藏与 API 403；
- Refresh Cookie 轮换、旧 token 重放撤销；
- CSRF/恶意 Origin 拒绝且不消耗 token；
- 两标签主动登出和服务端失效同步；
- 10 个并发 401 仅触发一次 refresh 并统一重放；
- 合成 DOCX 数据导入；
- 智能填充预览、执行、下载；
- BatchReply 上传、预览、执行、下载。

首次运行暴露 3 个不稳定定位器，均改为可访问角色/稳定组件状态；随后 BatchReply 定向 1/1 和全套 10/10 通过。失败截图、trace、HTML 报告与 API 脱敏日志由 CI artifact 保留。浏览器 job 不再使用 `continue-on-error`，因此工作流内为阻断门禁；仓库托管平台的分支保护 required-check 仍需由仓库管理员在外部设置。

## 5. 回滚演练

- 使用精确旧 API 镜像 ID `sha256:f028814e0b633da3820dfea8b0303f0e25247c47e37a785d48ce2aee04e65419` 和旧 Web 镜像 ID `sha256:99c60e89545dfdff6f0e09d0654ea632d446ed3b7580004e4bd4a6837de773ce` 回滚。
- 新 API → 旧 API → 新 API 切换后，文件、DataProtection keys、备份卷标记均保留，新非 root API 仍可继续写入。
- MySQL 容器删除并以相同固定镜像和同一命名卷重建后，测试表数据保留。
- 旧 Web 镜像能够重新启动并提供页面。

生产回滚仍应遵循 `.deploy/README.md`：只切换不可变镜像标签并执行 `compose up -d`；不得执行 `down -v`。旧 root 镜像若在回滚窗口写入文件，重新前滚非 root 镜像前必须按 `CONTAINER-DEPENDENCY-UPDATES.md` 执行卷权限检查。

## 6. 外部环境边界

本记录完成架构提案的生产等价验证，但不替代真实内网发布审批。当前确认的认证边界为无 SSO、内网同站 HTTP：用户只从一个固定主机名或 IP 进入 Web，由同站入口代理 API；不再把真实 SSO provider 或跨站拓扑列为待验证范围。

HTTP 模式必须显式开启，并继续强制 host-only、`SameSite=Strict`、精确 Origin、CSRF、RefreshToken 轮换和服务端撤销。上述控制不能提供传输加密：能够监听或篡改内网链路的人员或设备仍可能获取登录口令、AccessToken 或 Cookie。部署方应通过受信任网段/VLAN、主机防火墙和固定入口限制访问，不得把 Web/API 暴露到互联网或不可信无线网络；威胁边界变化时应迁移到内部 HTTPS。

## 7. 内网 HTTP 收口验证

- 后端：Core 341、Data 79（12 个条件跳过）、API 685（18 个外部/真实样本条件跳过），0 失败；认证生命周期专项 31/31。
- 前端：Vitest 74、Node 238，类型检查、ESLint、Prettier、Stylelint 和生产构建通过。
- Production HTTP 浏览器 E2E：10/10；真实 MySQL、固定同站 Origin、显式 `AllowInsecureHttp=true`。
- Docker：最终 API/Web 镜像构建通过；真实 Nginx → API 登录 Cookie 与 JSON 契约通过。
- 依赖审计：pnpm 与 NuGet 均未发现已知漏洞。
- OpenSpec strict、CI YAML/JSON 解析和 `git diff --check` 通过。
