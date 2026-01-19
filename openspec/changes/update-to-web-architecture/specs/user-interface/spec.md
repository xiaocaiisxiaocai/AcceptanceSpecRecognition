## MODIFIED Requirements

### Requirement: 用户界面架构
系统 SHALL 提供基于Vue3 + pure-admin的现代化Web管理界面，支持主流浏览器访问。

#### Scenario: 浏览器访问
- **WHEN** 用户通过Chrome/Firefox/Edge浏览器访问系统URL
- **THEN** 系统显示登录页面或主界面

#### Scenario: 响应式布局
- **WHEN** 用户在不同尺寸的屏幕上访问系统
- **THEN** 界面自适应调整布局

### Requirement: 数据导入界面
系统 SHALL 提供Web页面进行Word文档上传和数据导入。

#### Scenario: 文件上传
- **WHEN** 用户点击上传按钮并选择.docx文件
- **THEN** 系统上传文件并显示文件信息

#### Scenario: 表格预览
- **WHEN** 文件上传成功
- **THEN** 系统显示文档中所有表格的列表和预览

#### Scenario: 列映射配置
- **WHEN** 用户选择目标表格
- **THEN** 系统显示列映射配置界面，允许指定项目、规格、验收、备注列

#### Scenario: 客户制程选择
- **WHEN** 用户配置完列映射
- **THEN** 系统显示客户和制程选择器

#### Scenario: 导入确认
- **WHEN** 用户点击导入按钮
- **THEN** 系统执行导入并显示进度和结果

### Requirement: 智能填充界面
系统 SHALL 提供Web页面进行智能匹配和文档填充。

#### Scenario: 目标文件上传
- **WHEN** 用户上传需要填充的Word文档
- **THEN** 系统解析文档并显示表格列表

#### Scenario: 匹配方式选择
- **WHEN** 用户选择表格后
- **THEN** 系统显示匹配方式选项（相似度/Embedding/LLM混合）

#### Scenario: 匹配预览
- **WHEN** 用户点击预览按钮
- **THEN** 系统显示每行的匹配结果、得分和阈值状态

#### Scenario: 得分详情查看
- **WHEN** 用户点击某行的得分
- **THEN** 系统弹窗显示详细的得分计算过程

#### Scenario: 执行填充
- **WHEN** 用户确认匹配结果并点击填充
- **THEN** 系统执行填充并提供下载链接

### Requirement: 基础数据管理界面
系统 SHALL 提供客户、制程、验收规格的Web管理页面。

#### Scenario: 客户管理
- **WHEN** 用户访问客户管理页面
- **THEN** 系统显示客户列表，支持新增、编辑、删除操作

#### Scenario: 制程管理
- **WHEN** 用户访问制程管理页面
- **THEN** 系统显示制程列表，支持按客户筛选

#### Scenario: 验收规格浏览
- **WHEN** 用户访问验收规格页面
- **THEN** 系统显示规格列表，支持按客户、制程筛选和搜索

### Requirement: 配置管理界面
系统 SHALL 提供AI服务、文本处理等配置的Web管理页面。

#### Scenario: AI服务配置
- **WHEN** 用户访问AI配置页面
- **THEN** 系统显示已配置的AI服务列表，支持新增、编辑、删除

#### Scenario: 连接测试
- **WHEN** 用户点击测试连接按钮
- **THEN** 系统测试AI服务连接并显示结果

#### Scenario: 文本处理配置
- **WHEN** 用户访问文本处理配置页面
- **THEN** 系统显示简繁转换、同义词、OK/NG等配置选项

### Requirement: 操作历史界面
系统 SHALL 提供操作历史查看和撤销功能的Web页面。

#### Scenario: 历史列表
- **WHEN** 用户访问操作历史页面
- **THEN** 系统显示操作历史列表，包含时间、类型、详情

#### Scenario: 撤销操作
- **WHEN** 用户点击可撤销操作的撤销按钮
- **THEN** 系统执行撤销并更新历史状态

## REMOVED Requirements

### Requirement: WinForms桌面界面
**Reason**: 架构升级为Web应用，不再使用WinForms桌面界面
**Migration**: 所有界面功能迁移到Vue3 Web前端
