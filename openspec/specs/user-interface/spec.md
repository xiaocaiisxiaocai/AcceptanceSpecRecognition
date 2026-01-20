# User Interface Capability

## Purpose
提供基于Web的管理界面，覆盖数据导入、智能填充、基础数据维护、配置管理与操作历史浏览等核心操作流程。

## Requirements

### Requirement: Web管理界面
系统 SHALL 提供可在浏览器访问的Web管理界面。

#### Scenario: 浏览器访问
- **WHEN** 用户通过浏览器访问系统URL
- **THEN** 系统显示登录页面或主界面

---

### Requirement: 客户与制程独立选择
系统 SHALL 允许用户分别选择客户与制程作为数据范围条件。

#### Scenario: 独立选择器
- **WHEN** 用户在导入/验收规格/智能填充页面选择范围
- **THEN** 页面提供独立的客户选择器与制程选择器

#### Scenario: 组合筛选
- **WHEN** 用户选择客户与制程后
- **THEN** 系统按(CustomerId, ProcessId)组合筛选数据

---

### Requirement: 数据导入界面
系统 SHALL 提供文档上传与导入的Web界面。

#### Scenario: 文件上传
- **WHEN** 用户点击上传按钮并选择文档
- **THEN** 系统上传文件并显示文件信息

#### Scenario: 表格预览
- **WHEN** 文件上传成功
- **THEN** 系统显示文档中表格列表与预览内容

#### Scenario: 列映射配置
- **WHEN** 用户选择目标表格
- **THEN** 系统允许配置项目、规格、验收、备注列

#### Scenario: 客户制程选择
- **WHEN** 用户完成列映射配置
- **THEN** 系统提供客户与制程选择器

#### Scenario: 导入确认
- **WHEN** 用户点击导入按钮
- **THEN** 系统执行导入并提示结果

---

### Requirement: 智能填充界面
系统 SHALL 提供智能匹配与文档填充的Web界面。

#### Scenario: 目标文件上传
- **WHEN** 用户上传需要填充的Word文档
- **THEN** 系统解析文档并显示表格列表

#### Scenario: 匹配参数配置
- **WHEN** 用户进入匹配配置步骤
- **THEN** 系统提供阈值与相似度算法开关及权重配置

#### Scenario: 匹配预览
- **WHEN** 用户执行预览
- **THEN** 系统显示每行的匹配结果、得分与置信度状态

#### Scenario: 详情弹窗
- **WHEN** 用户查看匹配详情
- **THEN** 系统弹窗展示候选匹配列表与综合得分

#### Scenario: 执行填充
- **WHEN** 用户确认匹配结果并点击填充
- **THEN** 系统执行填充并提供下载入口

---

### Requirement: 基础数据管理界面
系统 SHALL 提供客户、制程、验收规格的Web管理页面。

#### Scenario: 客户管理
- **WHEN** 用户访问客户管理页面
- **THEN** 系统显示客户列表并支持新增、编辑、删除

#### Scenario: 制程管理
- **WHEN** 用户访问制程管理页面
- **THEN** 系统显示制程列表并支持新增、编辑、删除

#### Scenario: 验收规格浏览
- **WHEN** 用户访问验收规格页面
- **THEN** 系统显示规格列表并支持按客户、制程筛选

---

### Requirement: 配置管理界面
系统 SHALL 提供AI服务与文本处理的Web配置页面。

#### Scenario: AI服务配置
- **WHEN** 用户访问AI配置页面
- **THEN** 系统显示AI服务列表并支持新增、编辑、删除

#### Scenario: 连接测试
- **WHEN** 用户点击测试连接按钮
- **THEN** 系统测试AI服务连接并显示结果

#### Scenario: 文本处理配置
- **WHEN** 用户访问文本处理配置页面
- **THEN** 系统显示简繁转换、同义词、OK/NG等配置选项

---

### Requirement: 操作历史列表界面
系统 SHALL 提供操作历史列表的Web页面。

#### Scenario: 历史列表
- **WHEN** 用户访问操作历史页面
- **THEN** 系统显示操作历史列表，包含时间、类型、详情
