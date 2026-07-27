# Task 12 实施报告：CRUD 取消传播与有界批量删除

## 结论

Task 12 已按 `task-12-brief.md` 完成实现、定向回归和严格构建验证。

- 实现提交：`60919ff4d4c4d8f7fc717a88ac82467cbebe7823`
- 分支：`fix/runtime-correctness-hardening-impl`
- 基线：`146e69c5b21d0b28ad737e5d71175cabc47e8452`
- 未 push、未 merge、未切换 `main`，未新增迁移，未改变数据库关系。
- OpenSpec `3.8` 已更新为完成；未提前勾选需要全变更/真实环境证据的其他任务。

## 实施结果

### 1. 取消传播

- Customers、Processes、MachineModels、Specs 四个 controller 把动作
  `CancellationToken` 传入 `ResolveSpecScopeAsync`，再传入数据范围服务。
- Customer / Process / MachineModel 的 Create、Update、GetById、Delete、
  BatchDelete 和实际子列表路径把同一 token 传入仓储、EF 查询、保存和事务。
- 不存在 ID 的预取消单删在首个 `GetByIdAsync` 停止，不再先返回 `false`。
- 批删规范化逐项检查取消；结果映射逐项检查取消。
- 异常路径使用安全回滚，回滚次生失败不会覆盖原始取消或数据库异常。
- 未机械修改未被这些实际路径使用的遗留专用仓储方法。

### 2. 共享批删规范化

新增 Application 层唯一入口 `BatchDeleteInputNormalizer`：

- `MaxBatchDeleteItems = 500`。
- 忽略 `<= 0`，按首次出现顺序去重。
- 只读取到第 501 个唯一正 ID；第 501 个立即抛稳定 422。
- 过滤后为空分别返回“请选择要删除的客户/制程/机型/规格”400。
- 500 个唯一正 ID允许执行，501 个在事务、查询和写入之前拒绝。
- 未增加原始 JSON 请求体大小限制。

### 3. 三类主数据原子批删

Customer / Process / MachineModel 每批最多 500，不分块：

1. 一个事务；
2. 一次 tracked 父实体 `IN` 查询；
3. 一次 AcceptanceSpec 引用 `GroupBy` 聚合；
4. 按规范化输入顺序形成不存在、被引用和 eligible 结果；
5. eligible 一次 `RemoveRange`、一次 `SaveChangesAsync`、一次 commit。

不存在和被引用项继续作为逐项业务失败与成功项共存于 HTTP 200。保存阶段发生已知
FK/并发冲突时整个 eligible 集合回滚并返回 409；未知 `DbUpdateException` 回滚后
原样上抛，不再伪装成冲突。

### 4. AcceptanceSpec 批删

- 使用相同 500 上限和输入规范化。
- 保留一条 `ExecuteDeleteAsync`，返回数据库实际删除数量。
- ID 条件前置强制 `spec.WordFile.CompanyId == scope.CompanyId`，`IsAll` 也不能跨公司。
- 再应用本人/组织等既有范围；实际删除 0 继续返回 403。
- 已知删除冲突映射 409；未知数据库异常进入统一 500。
- EmbeddingCache 仍由数据库 cascade 删除；WordFile 不删除、不进入物理删除流程。

### 5. 稳定异常分类、HTTP 与审计

- `DbUpdateConcurrencyException` 识别为删除冲突。
- MySQL 1451/1217 识别为父项删除 FK 冲突。
- MySQL 1062 只有精确目标索引 `IX_Customers_Name` 才映射客户名称 409。
- SQLite 测试只使用稳定 primary/extended error code：FK 787、unique 2067。
- 未知 `DbUpdateException` 不包装；API 统一返回脱敏 500。
- 三个主数据批删 controller 捕获明确 `ApplicationServiceException`，声明 409/422，
  使 422 成为普通 4xx 结果，审计 `status=422`、`level=Warning`。
- 四类 501 API 均验证 HTTP 422、body code 422、稳定中文提示和非空 traceId。

### 6. 既有关系保持

- AcceptanceSpec -> Customer / Process / MachineModel 的 Restrict 行为仍阻断主数据删除。
- AcceptanceSpec -> EmbeddingCache 继续数据库 cascade。
- Customer -> DocumentTemplate -> Region 继续数据库 cascade。
- WordFile 在规格批删后保留。
- 未触碰 ColumnMappingRule / SmartStructureRoutingRule 的逻辑 CustomerId。
- 没有 migration 或模型关系改动。

## TDD RED / GREEN 证据

### A. CRUD 与 controller scope 取消

命令：

```text
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter FullyQualifiedName~CrudCancellationAndBatchDeleteTests --no-restore --nologo
```

- RED：旧实现 12/12 失败。三个创建未取消且写入；三个更新先保存再在统计查询取消；
  三个不存在单删返回 `false`；两个子列表返回 `null`；四类 controller scope 丢 token，
  继续进入应用服务并出现 `NullReferenceException`。
- GREEN：取消分组 12/12 通过。

### B. 500/501、非正数、重复和空输入

命令：

```text
dotnet test ...Api.Tests.csproj -c Debug --filter "FullyQualifiedName~四类批删" --no-restore --nologo
```

- RED：旧实现 16 项中 11 项按预期失败；501 未拒绝并开始删除，全非正数未返回 400，
  三类主数据把非正数和重复 ID 写入失败结果。旧实现已有的 500 边界和规格重复实际删除数
  共 5 项保持通过。
- GREEN：16/16 通过；501 用“首项为真实可删实体”证明拒绝发生在任何数据库工作之前，
  实体仍保留。

### C. mixed 结果与一次保存/提交

命令：

```text
dotnet test ...Api.Tests.csproj -c Debug --filter "FullyQualifiedName~三类主数据混合批删" --no-restore --nologo
```

- RED：3/3 失败；旧实现 mixed 顺序正确，但两个 eligible 实测调用保存 2 次而不是 1 次。
- GREEN：3/3 通过；failures 保持“不存在 -> 被引用”输入顺序，success 保持 eligible
  输入顺序，Begin/Save/Commit 各 1、Rollback 0。

### D. provider 分类与异常回滚

命令：

```text
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Debug --filter FullyQualifiedName~DatabaseConstraintClassifierTests --nologo
dotnet test ...Api.Tests.csproj -c Debug --filter "FullyQualifiedName~三类主数据批删未知数据库错误|FullyQualifiedName~三类主数据批删取消|FullyQualifiedName~客户名称目标唯一冲突" --no-restore --nologo
```

- RED：分类器缺少删除冲突 API，新增测试首先因目标行为不存在而编译失败；服务异常组
  8/8 失败：未知错误被包装 409、回滚失败覆盖原取消、客户目标唯一冲突原样外抛。
- GREEN：分类器 7/7；服务异常组 8/8。

### E. Spec 公司隔离和关系

命令：

```text
dotnet test ...Api.Tests.csproj -c Debug --filter "FullyQualifiedName~规格批删IsAll|FullyQualifiedName~删除规格应级联" --no-restore --nologo
```

- RED：双公司 `IsAll` 旧实现实际删除 2 条，期望 1 条；EmbeddingCache cascade /
  WordFile 保留的既有关系测试在旧实现即通过。
- GREEN：双公司隔离和规格关系 2/2；Customer -> DocumentTemplate -> Region
  级联回归另行 1/1 通过。

### F. API 422、审计和未知 500

命令：

```text
dotnet test ...Api.Tests.csproj -c Debug --filter "FullyQualifiedName~四类批删API|FullyQualifiedName~三类主数据批删应声明" --no-restore --nologo
dotnet test ...Api.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~未知数据库异常应由统一边界" --nologo
```

- RED：7 项中旧实现 6 项失败；三主数据 422 的审计是 Error，且动作只声明 200。
  Specs 已有普通异常转换，1 项保持通过。
- GREEN：422/API/审计/OpenAPI 7/7；未知数据库异常脱敏 500 单项 1/1。
- 未知 500 的核心行为已在 D 组旧实现上看到正确 RED：旧服务把同一未知
  `DbUpdateException` 包装成 409；API 组验证最终统一边界和脱敏响应。

## 最终验证

### Task 12 定向

- `CrudCancellationAndBatchDeleteTests`（默认门禁关闭）：50 pass / 1 skip。
- `MySql真实环境应允许500项主数据单事务批删`（门禁开启）：1/1。
- `DatabaseConstraintClassifierTests + EmbeddingCacheRepositoryTests`：21/21。

### 受影响 API 回归

命令：

```text
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~CrudFlowTests|FullyQualifiedName~SpecDataScopeTests|FullyQualifiedName~CancellationPropagationTests|FullyQualifiedName~AuditLogsTests|FullyQualifiedName~MasterDataPaginationContractTests" --nologo
```

结果：27/27。

### 受影响 Data 回归

命令：

```text
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~CustomerRepositoryTests|FullyQualifiedName~ProcessRepositoryTests|FullyQualifiedName~MachineModelRepositoryTests|FullyQualifiedName~DocumentTemplateRepositoryTests|FullyQualifiedName~AcceptanceSpecRepositoryQueryTests" --nologo
```

结果：19/19。

### 构建、规范与仓库卫生

```text
dotnet build AcceptanceSpecSystem.sln -c Debug --no-restore --nologo -m:1 -p:UseSharedCompilation=false
openspec validate harden-runtime-correctness-and-resource-boundaries --strict
openspec validate --all --strict --no-interactive
git diff --check
```

- solution build：成功，0 warning / 0 error。
- OpenSpec change：valid。
- OpenSpec 全量 strict：23/23。
- `git diff --check`：通过。

## 环境门禁与已知关注

1. 真实 MySQL 500-ID 批删合约测试：

   ```text
   dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~MySql真实环境应允许500项主数据单事务批删" --nologo
   ```

   本地 `mysqld` 的门禁结果为 1 pass / 0 fail / 0 skip。测试只创建唯一命名的
   `acceptance_spec_test_*` 隔离 schema，迁移后插入并通过真实应用服务一次批删
   500 个 Customer，验证结果顺序、零逐项失败和数据库零残留；`await using`
   退出时执行 `DROP DATABASE IF EXISTS`，隔离 schema 已清理。连接配置只临时注入
   测试进程并在 `finally` 清除，报告、ledger 和提交均不记录凭据或连接串。
   本证据不延伸声称真实 FK race 已验证。

2. 一次组合测试复跑时，全机只剩约 340 MiB 可用内存，VSTest 进程以
   `System.OutOfMemoryException` 中止；这不是测试断言失败。只终止了可自动重建的
   Roslyn `VBCSCompiler` 进程（未停止 API、数据库或其他项目服务），可用内存恢复到
   约 1.1 GiB。随后使用单并发 build 与 `--no-build` 重跑，Task12 全类 50/50 和所有
   上述门禁均通过。

3. 本任务按 brief 只运行定向和受影响回归，没有声称执行 .NET 全量测试、真实
   FK race、push、merge、部署或生产验证。
