## MODIFIED Requirements

### Requirement: 客户与制程独立选择
系统 SHALL 允许用户分别选择客户与制程作为数据范围条件，并新增机型维度选择。

#### Scenario: 独立选择器
- **WHEN** 用户在导入/验收规格/智能填充页面选择范围
- **THEN** 页面提供独立的客户、制程、机型选择器

#### Scenario: 组合筛选
- **WHEN** 用户选择客户与制程/机型后
- **THEN** 系统按(CustomerId, ProcessId, MachineModelId)组合筛选数据

---

### Requirement: 基础数据管理界面
系统 SHALL 提供客户、制程、机型、验收规格的Web管理页面。

#### Scenario: 客户管理
- **WHEN** 用户访问客户管理页面
- **THEN** 系统显示客户列表并支持新增、编辑、删除

#### Scenario: 制程管理
- **WHEN** 用户访问制程管理页面
- **THEN** 系统显示制程列表并支持新增、编辑、删除

#### Scenario: 机型管理
- **WHEN** 用户访问机型管理页面
- **THEN** 系统显示机型列表并支持新增、编辑、删除

#### Scenario: 验收规格浏览
- **WHEN** 用户访问验收规格页面
- **THEN** 系统显示规格列表并支持按客户、制程、机型筛选

---

### Requirement: 数据导入界面
系统 SHALL 提供文档上传与导入的Web界面。

#### Scenario: 客户制程选择
- **WHEN** 用户完成列映射配置
- **THEN** 系统提供客户、制程、机型选择器
