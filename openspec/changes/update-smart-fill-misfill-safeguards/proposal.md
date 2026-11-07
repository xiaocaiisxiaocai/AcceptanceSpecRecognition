# Change: 收紧智能填充自动通过门禁，修复高风险误匹配

## Why
匹配风险排查（2026-11-05）用真实匹配服务实测确认，多条自动通过路径会在无人复核的情况下把错误规格直接填入：
语义优先模式（智能填充前端默认开启）下 LLM 可覆盖数值/单位硬冲突；LLM 超时、熔断或额度耗尽回退后，高 Embedding 路径仍自动通过（fail-open）；
`10mΩ` 与 `10MΩ` 因单位识别不区分大小写被判为等价；“长度300”与“长度3000”这类裸数字差异被规格骨架抹平后确定性放行；
仅规格模式下近似规范化层命中多条候选时不判歧义；短项目名被长项目名“包含”即获得接近分。

后续复查（2026-11-07）又确认三项残余风险：原文精确层不检查硬冲突，大小写归一后的 `10mΩ`/`10MΩ` 仍被标为“精确一致”；同单位数值也套用 0.1% 换算容差，“外径25.000mm”与“25.020mm”被确定性放行；“保修期三十天”与“三天”这类中文数字差异不产生任何警告。

本变更为已实现修复补登记的提案（实现与回归测试已随提交 `4751db9` 及后续残余风险补修落地），用于让规格与实际行为保持一致。

## What Changes
- **BREAKING（行为）**：量纲型硬冲突（`numeric_unit_conflict`、`cross_temperature_scale`、`dimension_tuple_conflict`）在任何模式下都不允许 LLM 等价裁决覆盖；语义优先模式只保留对比较符、方向/极性、否定语义等“语义方向”类硬冲突的 LLM 放行。
- 高 Embedding 语义自动通过（`EmbeddingSemanticAutoApplyThreshold`）只接受 LLM 真实返回的 `uncertain`；LLM 调用失败、超时、熔断或额度耗尽回退时一律转人工。
- 数值单位识别大小写敏感（`mΩ`/`MΩ`、`mW`/`MW` 等），补充 `mΩ`、`μΩ` 电阻单位。
- 近似规范化层在比对带单位数值之外，单独比对无单位裸数字；仅规格模式下同骨架命中多条候选时判为歧义转人工。
- 项目名子串包含仅在较短者长度不低于较长者 70% 时给接近分（0.85），否则降为 0.5。
- 原文精确层扫出硬冲突时不再直接命中，交后续流程转人工。
- 同单位数值比较不再套用 0.1% 换算容差，仅跨单位换算时套用。
- 中文数字识别扩展到时间、周期、次数、倍数等常用单位，差异表达阻断无裁决自动通过。

不在本变更范围：智能填充前端是否继续默认开启语义优先、LLM 置信度校准、召回 Top-K 调整、跨单位工程容差按量纲配置（待业务确认后另行提案）。

## Impact
- Affected specs: `matching-engine`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.Decisions.cs`、`.IssueClassification.cs`、`.Llm.cs`、`.Results.cs`、`.Shortcuts.cs`、`.Types.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/Services/SpecCanonicalizer.cs`、`.Text.cs`、`.Knowledge.cs`、`Interfaces/ISpecCanonicalizer.cs`
  - 回归测试：`tests/AcceptanceSpecSystem.Core.Tests/MatchingRiskRegressionTests.cs`
- 用户可见影响：原先被自动填充的部分灰区行会转为人工确认（预期少量覆盖率下降，换取错填风险下降）。
