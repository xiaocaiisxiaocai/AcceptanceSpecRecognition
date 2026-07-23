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
