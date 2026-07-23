# 覆盖率不回退策略

本策略适用于后端 Cobertura 与前端 Vitest V8 Cobertura/JSON Summary。它用于发现测试保护面的退化，不把任意绝对百分比当作质量结论。

## 比较基线

- 基线来源：`main` 最近一次成功 CI 的 `backend-coverage` 与 `frontend-coverage` artifact。
- 比较前提：工具版本、采集命令、include/exclude 范围和测试分类必须一致；这些条件变化时先建立新基线，并在 PR 中说明原因。
- 比较维度：后端与前端分别比较 lines、branches、functions（工具提供时）和 statements（工具提供时）；不得用一个维度的增长抵消另一个维度的下降。
- 精度：使用报告中的 covered/total 原始计数重新计算，保留两位小数；低于基线即视为回退，不设置额外“容忍带”。
- 新增或修改核心流程时，还必须核对变更行是否有对应测试；总体百分比未下降不能替代变更行审查。

## 执行阶段

1. CI 始终生成并保留原始 coverage artifact，失败时也上传可用报告。
2. 在连续三次成功的 `main` 报告确认采集稳定前，覆盖率比较为人工审查项，不设 required gate，避免把工具或采集范围波动误判为代码退化。
3. 稳定后将同维度不回退比较加入 required CI。基线 artifact 不可用、采集范围变化或报告不完整时必须 fail closed，不能静默跳过。

## 例外审批

例外必须在合并前记录：影响维度、基线值/当前值、原因、补测负责人、补测截止日期和审批人。永久例外不被接受；到期未补测时阻止后续相关变更合并。

当前例外：无。

## 本地复查

```powershell
dotnet test AcceptanceSpecSystem.sln -m:1 --collect:"XPlat Code Coverage" --results-directory artifacts/backend-coverage
pnpm --dir web test:coverage
```

2026-07-10 本地前端基线（Vitest 78 项、Node 238 项）：lines 66.54%，branches 51.88%，functions 59.68%，statements 64.74%。该数值只用于校验采集链路，不替代 `main` CI artifact 基线。
