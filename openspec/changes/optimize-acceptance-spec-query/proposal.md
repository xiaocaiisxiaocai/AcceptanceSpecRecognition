# Change: 优化验收规格列表查询

## Why

当前验收规格约 8,878 条，普通分组分页仍然较快，但快速切换分组时前端缺少旧请求失效保护，且允许一次渲染 1,000 条多行记录。数据库现有组合筛选索引也未覆盖按导入时间倒序的分页排序，随着单组数据增长会持续产生额外排序开销。

## What Changes

- 为验收规格列表请求增加竞态保护，只有最新一次请求可以更新表格和加载状态。
- 分页选项调整为 `100 / 200 / 500`，保留用户要求的 500 条上限并移除 1,000 条选项。
- 为验收规格增加覆盖客户、制程、机型、导入时间和主键的组合排序索引。
- 通过 EF Core Migration 管理索引变更；应用本地迁移前先备份当前数据库。
- 保持现有关键词搜索语义、接口参数和返回结构不变，不引入全文检索。

## Impact

- Affected specs: `user-interface`, `data-storage`
- Affected code:
  - `web/src/views/base-data/specs/components/SpecTable.vue`
  - `web/tests/spec-global-search.test.ts`
  - `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
  - `src/AcceptanceSpecSystem.Data/Migrations/`
  - `tests/AcceptanceSpecSystem.Data.Tests/`
- Database: 新增非唯一组合索引，不修改业务数据。
- Compatibility: API 契约和查询结果语义保持兼容。
