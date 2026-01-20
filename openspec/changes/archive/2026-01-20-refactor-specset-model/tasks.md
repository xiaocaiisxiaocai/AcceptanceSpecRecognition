## 1. Data model & migration
- [x] 1.1 更新实体：移除 `Process.CustomerId` 与 `Customer.Processes` 导航属性
- [x] 1.2 更新实体：为 `AcceptanceSpec` 增加 `CustomerId`（并建立导航属性 `Customer`）
- [x] 1.3 更新 EF 映射：调整外键/索引（`AcceptanceSpecs(CustomerId,ProcessId)`）
- [x] 1.4 新增 EF Migration：Schema 变更 + 数据迁移（用旧关系回填 `AcceptanceSpec.CustomerId`）

## 2. Repository / API
- [x] 2.1 调整 Process API：不再要求 customerId；列表不再按客户筛
- [x] 2.2 调整 Specs API：按 `(customerId, processId)` 组合查询；导入也写入 customerId
- [x] 2.3 调整 Matching/Import：匹配候选与导入范围按 `(customerId, processId)` 组合
- [x] 2.4 更新 Swagger 示例与 DTO（如需要）

## 3. Frontend (Vue)
- [x] 3.1 调整制程页面：去掉 customerId 依赖/筛选
- [x] 3.2 调整验收规格页面：客户选择 + 制程选择（不再级联），按组合筛
- [x] 3.3 调整导入页：客户与制程选择改为独立选择，导入提交同时传 customerId + processId
- [x] 3.4 调整智能填充页：选择范围改为 customerId + processId 组合

## 4. Tests & docs
- [x] 4.1 更新/补充 API 集成测试覆盖新模型
- [x] 4.2 更新前端构建验证
- [x] 4.3 更新 `docs/DEV.md`（说明新的选择方式/接口变化）

