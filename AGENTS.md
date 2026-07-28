<!-- OPENSPEC:START -->
# OpenSpec Instructions

These instructions are for AI assistants working in this project.

Always open `@/openspec/AGENTS.md` when the request:
- Mentions planning or proposals (words like proposal, spec, change, plan)
- Introduces new capabilities, breaking changes, architecture shifts, or big performance/security work
- Sounds ambiguous and you need the authoritative spec before coding

Use `@/openspec/AGENTS.md` to learn:
- How to create and apply change proposals
- Spec format and conventions
- Project structure and guidelines

Keep this managed block so 'openspec update' can refresh the instructions.

<!-- OPENSPEC:END -->

## 分支合并保护

- `feat/smart-recognition-simplification` 合并、推送或以任何方式提交到远端 `main` 前，必须先向用户二次确认并取得明确同意。

## Git 提交规范

- 提交、推送、创建或切换分支等版本控制操作统一使用 `git` 命令完成，不使用 GitHub App、网页界面或其他提交工具代替。
- 提交前先用 `git status`、`git diff` 等命令确认实际改动范围，避免混入无关文件。

## 回归测试范围

- 小型修改默认只运行与改动直接相关的定向测试、类型检查或代码规范检查，不运行完整回归测试。
- 只有大型修改、跨模块或高风险修改，或者用户明确要求完整回归测试时，才运行全量测试和完整构建验证。
- 测试范围应与修改风险相匹配；不得仅为流程完整而重复运行已经通过且未受影响的测试。

## Docker 部署与生产更新

- 当用户提及“部署”、“Docker 部署”、“生产更新”、“更新服务器”或表达同等意图时，必须先完整读取 `docs/DEPLOY-WINDOWS-DOCKER.md`。
- 回复时先向用户提供该文档路径，并以文档中的首次部署或生产更新流程为操作基线；用户要求简洁教程时，可以只摘取当前阶段，但不得省略备份、迁移门禁、健康验证和数据抽查。
- 生产更新教程必须从 Git 拉取、分支和完整 SHA 核对开始，不得默认从构建、迁移或启动步骤中途开始。
- 默认只提供部署教程。未经用户对准确生产目标和本次生产动作的独立明确授权，不执行远端、生产、容器或数据库写操作。
- 若现场的目录、Compose 文件、环境文件、服务名、端口或持久化卷与文档不同，先执行只读核对，不猜测或直接套用命令。
- 不得在回复、日志、截图或提交中回显真实密码、JWT 密钥、生产环境变量或客户数据；发现凭据暴露时应提示在部署稳定后进行受控轮换。
- 任何生产更新均不得执行 `docker compose down -v`、删除 MySQL 容器或数据卷、覆盖线上 `.env.docker`、手工修改 `__EFMigrationsHistory`，也不得在迁移失败后强行启动新版本。
