## Context
- 当前智能填充主链已是 Embedding 召回 + 证据重排 + AI 等价裁决/复核。
- 旧列映射规则只剩历史兼容用途，不再符合当前产品方向。
- 用户已明确确认方案 1，并允许删除旧列映射和相关数据库结构。

## Goals
- 删除旧列映射规则能力的所有活代码和对应数据结构。
- 让 smart-fill 在进入预览/执行前就给出明确、可操作的前置提示。
- 保持现有 AI 主链不变，只修界面契约和删旧链路。

## Non-Goals
- 不在本次变更中重写底层 deterministic 证据链。
- 不新增新的匹配策略或新的 Prompt 场景。

## Decisions
- Decision: 旧 `ColumnMappingRules` 迁移文件保留历史记录，但通过新增迁移删除现存旧表，并更新 snapshot。
- Decision: data-import 不再依赖任何规则自动预填，字段映射完全由本地默认值和用户手工调整驱动。
- Decision: smart-fill 继续保留后端 `400` 错误语义，但前端新增服务状态和范围空态前置引导。
- Decision: 执行填充与下载权限分开判定，执行成功后始终缓存 `taskId`，允许用户稍后重新下载。

## Risks / Trade-offs
- 风险：删除旧仓储/DTO/接口后，可能引起测试替身和 `UnitOfWork` 代理编译报错。
  - Mitigation：先补失败测试，再统一移除引用并跑全量构建。
- 风险：数据库已存在旧表时，需要保证新增迁移能安全删除。
  - Mitigation：使用新增移除迁移，不回写或篡改历史迁移文件。

## Migration Plan
1. 新增移除 `ColumnMappingRules` 表的 EF 迁移并更新 snapshot。
2. 删除后端控制器、DTO、实体、仓储、注册点与测试引用。
3. 删除前端 API、配置页和 data-import 自动预填逻辑。
4. 补 smart-fill 空态、下载恢复与文案修复后完成验证。
