# Change: 智能填充编辑值回填验收规格

## Why
智能填充预览已允许用户手动修订本次导出的验收标准与备注，但这些修订当前只作用于本次导出，无法沉淀回验收规格主数据。用户需要把人工修正后的有效内容批量回填，减少后续重复修订。

## What Changes
- 在智能填充执行前，对用户手动修改过的行弹出回填确认框。
- 支持全选或部分选择编辑行回填到验收规格。
- 已匹配行回填为更新现有验收规格的验收标准与备注。
- 未匹配但已手工填写的行回填为新增验收规格，使用源项目、源规格和当前匹配范围归属。
- 保留“不回填，仅执行填充”路径，避免强制修改主数据。

## Impact
- Affected specs: `user-interface`, `api`
- Affected code: `web/src/views/smart-fill/*`, `web/src/api/matching.ts`, `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`, `src/AcceptanceSpecSystem.Api/Controllers/*`, `src/AcceptanceSpecSystem.Api/Services/*`, `src/AcceptanceSpecSystem.Data/Entities/AcceptanceSpec.cs`
- Affected tests: `tests/AcceptanceSpecSystem.Api.Tests/*`, `web/tests/*`
