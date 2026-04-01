# Change: 重构匹配知识配置为分组式维护模型

## Why
当前匹配知识配置页面直接暴露了运行时内部使用的“别名 -> 标准值”与“左词 -> 右词”结构，导致用户需要维护 `a -> a`、`功率 -> 功率`、`松下 -> 松下` 这类技术性自映射，理解成本高，也难以批量维护同义词与对立词。

## What Changes
- 将实体别名维护模型改为“实体组”，一行维护多个同一实体的叫法，首项作为标准实体。
- 将单位别名维护模型改为“单位组”，一行维护多个同一标准单位的写法，首项作为标准单位。
- 将字段别名维护模型改为“字段组”，一行维护多个同一标准字段的写法，首项作为标准字段。
- 将冲突词对维护模型改为“左右冲突组”，每行维护左右两个对立词组，组内同义、组间冲突。
- 匹配知识 API 读写分组化作者视图，并在保存时展开为运行时可直接消费的别名字典与冲突词对。
- 保持运行时匹配语义不变，继续使用展开后的标准化映射与冲突对进行解析和裁决。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code:
  - `web/src/views/config/matching-knowledge/index.vue`
  - `web/src/api/matching-knowledge.ts`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/MatchingKnowledgeController.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
