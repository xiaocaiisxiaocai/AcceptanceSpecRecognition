## ADDED Requirements
### Requirement: API 用例服务接口边界
系统 MUST 让控制器依赖简单 AppService 的接口契约，而不是直接依赖具体实现；接口化不得改变既有业务行为。

#### Scenario: 控制器依赖接口
- **WHEN** 开发者查看已接口化的 API AppService 控制器依赖
- **THEN** 控制器构造函数注入 `I*AppService`
- **AND** 依赖注入容器将接口映射到对应实现

### Requirement: 仓储行为具备回归测试
系统 MUST 为高价值仓储查询与通用 CRUD 行为提供自动化测试，覆盖排序、过滤、导航加载和缺失数据场景。

#### Scenario: 仓储查询回归
- **WHEN** 仓储查询逻辑被修改
- **THEN** 测试能够验证关键过滤、排序和关联加载行为未退化
