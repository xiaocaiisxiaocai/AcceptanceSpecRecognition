# Change: 重构批量回复为来源与目标逐表独立配置

## Why
当前批量回复默认把来源文件的表格索引、行配置和列配置直接套用到所有目标文件，仍然隐含“来源表与目标表结构基本一致”的前提。实际业务中，不同 `Sheet/表格` 往往需要各自单独指定行配置、项目列、规格列、验收列和备注列，甚至同一个目标表还需要显式选择对应的来源表。

用户同时明确希望页面改成类似智能填充的配置体验：来源文件和目标文件都能逐表独立配置，页面支持预览，不再要求先做一次整批“预检查”才能进入执行。

## What Changes
- 重构批量回复交互为多 Tab 配置流，分别管理来源配置、目标配置和执行结果。
- 新增来源文件逐表独立配置能力，支持 Word / Excel 的行配置与列映射。
- 新增目标文件逐文件、逐表独立配置能力，每个目标表可显式选择来源表并单独配置行/列映射。
- 将批量回复从“整批预检门禁”改为“按表预览 + 按文件完整性执行”，执行前仅校验当前目标文件的参与表是否配置完整且可写回。
- 保持写回范围仅限验收列与备注列；匹配仍以 `项目 + 规格` 为键，允许乱序，不允许重复键自动处理。

## Impact
- Affected specs: `api`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/Controllers/BatchReplyController.cs`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/BatchReplySessionService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`
  - `web/src/api/matching.ts`
  - `web/src/views/batch-reply/index.vue`
  - `web/src/views/smart-fill/components/BatchTableConfig.vue`
  - `tests/AcceptanceSpecSystem.Api.Tests/*BatchReply*`
  - `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`
