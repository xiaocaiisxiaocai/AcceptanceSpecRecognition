# 项目踩坑记录

## Api 集成测试：管理员必须显式指定业务归属部门（2026-11-06）

- 现象：大量 Api 集成测试在上传文档、新增规格、llm-stream 时返回 400（“请选择本次导入或智能填充的业务归属部门”“当前操作缺少源文件的业务归属”），导入查重/回填测试“查不到既有规格”。
- 根因：`b11666b` 起启动种子会创建默认业务部门 `ELECTRICAL_CONTROL`；存在业务部门时 `BusinessOrgScopeService` 要求管理员显式传 `businessOrgUnitId`，llm-stream 需带 `fileId` 按源文件归属定范围；直接写库时 `OwnerOrgUnitId = 1`（公司根）的规格会被部门隔离过滤。
- 规避：统一用 `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/TestBusinessOrg.cs`：上传 `await multipart.AddDefaultBusinessOrgUnitIdAsync(client)`，建规格传 `businessOrgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(client)`，写库造数用 `GetDefaultBusinessOrgUnitIdAsync(dbContext)`；llm-stream 用 `BatchPreviewTestHelper.PostWithFileIdAsync` 拿 `fileId`。不要放宽生产侧部门校验。
- 另：测试里的 `IUnitOfWork` 包装类新增仓储后要补转发（如 `AcceptanceSpecContentVersions`），否则走默认接口成员抛 `NotSupportedException` → 500。

## 文档行号：快照投影是相对数据起始行（2026-11-06）

- 现象：Word 智能填充预览的 `rowIndex` 比执行端少 1，执行报“表格1第1行不属于已确认的数据区域”。
- 根因：`6363756` 改为复用解析快照后，`TableDataProjection.Project` 输出的数据行号从 0 开始（相对数据起始行）；原 Word 解析器给的是表格物理行号。`08d0aa7` 只在执行与批量回复目标两处事后补偏移，预览没补。
- 规避：写回坐标统一在 `DocumentTableAccessService.ExtractMatchSourceItemsAsync` / `ExtractReplySourceItemsAsync` 换算（`row.Index + mapping.DataStartRowIndex`，Word 与 Excel 同一规则），调用方不要再自行加偏移。回归测试 `WordRowIndexRegressionTests`。导入链路（`ExtractTableDataAsync` 的 `row.Index`）仍是相对行号，前后端自洽，改动前先核对前端排除行。

## 新增 EF 迁移必须登记安全分类（2026-11-06）

- 现象：`MigrationMetadataTests` 失败；生产升级已有库时 API 进入“仅开放健康诊断”的迁移封锁。
- 根因：`DatabaseInitializer` 把未出现在 `SafeMigrationIds`/`DestructiveMigrationIds` 的迁移视为非安全。
- 规避：每次新增迁移同时在 `DatabaseInitializer` 中显式分类（只加可空列/新表 → Safe）。

## 规格相关实体带全局查询过滤器（2026-11-06）

- 现象：Data 层测试插入 `EmbeddingCache` 后查不到。
- 根因：`0e60c17` 起 `EmbeddingCache` 等实体按所属规格 `CleanupStatus == Active` 过滤；InMemory 不校验外键，只插缓存不插规格会被过滤。
- 规避：测试造数时先插对应的正常状态 `AcceptanceSpec`。
