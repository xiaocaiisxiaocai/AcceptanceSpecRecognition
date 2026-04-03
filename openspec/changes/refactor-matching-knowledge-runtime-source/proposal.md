# Change: 将匹配知识改为数据库唯一运行时来源

## Why
当前匹配知识采用“系统内置只读 + 自定义扩展可编辑 + 运行时合并”的模型。这个模型虽然能提供默认规则，但也带来了几个实际问题：

- 管理端看到的“当前生效配置”并不是单一事实来源，用户删除某些默认规则后，运行时仍可能因为内置层继续生效，导致“界面已删、运行时还在”的认知偏差。
- 部分系统内置规则并不适用于当前业务场景，管理员无法真正移除这些规则，只能在“自定义扩展”层做补充，长期会增加维护负担。
- AI 草稿生成、匹配运行时加载和配置页展示都要理解“内置 / 自定义 / 生效”三层概念，增加了实现复杂度和排查成本。
- 对开发者而言，匹配知识缺少一个清晰、可审计、可导出、可恢复的唯一真相源，不利于版本管理、环境迁移和问题复现。

业务上更需要的是：**运行时只使用数据库中的当前配置，默认知识仅作为初始化种子或恢复模板，而不是隐式参与每次匹配。**

## What Changes
- 将匹配知识的运行时来源收敛为 **数据库中的当前单例配置**，不再在运行时叠加 `appsettings` 内置层。
- 将当前 `MatchingKnowledge` 内置配置从“运行时只读基线”调整为“初始化种子 / 恢复默认模板”：
  - 首次启动且数据库为空时，可从默认种子导入一份初始配置。
  - 管理员可通过显式操作恢复默认种子。
  - 平时运行时不再自动叠加或回补默认项。
- 简化匹配知识配置 API 与前端页面语义：
  - `GET /api/matching-knowledge` 返回当前完整生效配置，而不是 `builtIn/custom/effective` 三层视图。
  - `PUT /api/matching-knowledge` 保存当前完整配置，而不是“仅保存自定义扩展”。
  - 将“重置默认”语义拆分为更清晰的显式操作，例如“清空当前配置”和“恢复默认种子”。
- 保持 AI 草稿生成能力，但草稿比对、去重和导入都基于数据库当前配置进行，不再依赖运行时内置层。
- 保持匹配主链路对“匹配知识驱动归一化与冲突校验”的依赖不变，但其知识来源改为数据库唯一事实源。
- 保留默认知识模板的可恢复能力，避免管理员误删后无法回滚。
- **BREAKING**：匹配知识 API 不再返回分层视图，运行时不再隐式叠加默认规则。

## Impact
- Affected specs:
  - `api`
  - `user-interface`
  - `data-storage`
- Potentially reviewed specs:
  - `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
  - `src/AcceptanceSpecSystem.Api/Services/ConfigurationMatchingKnowledgeProvider.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeBootstrapper.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Program.cs`
  - `src/AcceptanceSpecSystem.Api/appsettings.json`
  - `web/src/api/matching-knowledge.ts`
  - `web/src/views/config/matching-knowledge/index.vue`

## Expected Outcome
- 管理端看到的配置就是运行时真正使用的配置。
- 删除一条默认知识后，该知识不会在运行时隐式继续生效。
- 默认规则仍可作为初始化模板和恢复模板保留，但不再影响日常运行时判定。
- 匹配知识配置页、AI 草稿导入、匹配运行时加载三者共享同一份数据库配置语义，降低理解和排查成本。
