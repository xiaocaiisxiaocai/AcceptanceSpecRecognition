## MODIFIED Requirements

### Requirement: 数据库存储
系统 SHALL 使用MySQL 8.0数据库存储所有业务数据，通过Entity Framework Core进行数据访问。

#### Scenario: 数据库连接
- **WHEN** 系统启动时
- **THEN** 系统连接到MySQL数据库（Server=localhost, Database=acceptance_spec_db, User=root）

#### Scenario: 字符集支持
- **WHEN** 存储包含中文的数据时
- **THEN** 系统正确存储和读取中文字符（使用utf8mb4字符集）

#### Scenario: 数据库迁移
- **WHEN** 数据模型发生变更时
- **THEN** 系统通过EF Core Migrations管理数据库结构变更

### Requirement: 数据库连接配置
系统 SHALL 支持通过配置文件配置MySQL数据库连接。

#### Scenario: 连接字符串配置
- **WHEN** 部署系统时
- **THEN** 可通过appsettings.json配置数据库连接字符串

#### Scenario: 连接池管理
- **WHEN** 多用户并发访问时
- **THEN** 系统使用连接池管理数据库连接

### Requirement: 并发访问支持
系统 SHALL 支持多用户同时访问数据库。

#### Scenario: 并发读取
- **WHEN** 多个用户同时查询数据
- **THEN** 系统正确返回各自的查询结果

#### Scenario: 并发写入
- **WHEN** 多个用户同时写入数据
- **THEN** 系统通过事务保证数据一致性

#### Scenario: 乐观锁控制
- **WHEN** 两个用户同时编辑同一条记录
- **THEN** 后提交的用户收到冲突提示

## ADDED Requirements

### Requirement: RESTful API访问
系统 SHALL 通过ASP.NET Core Web API提供数据访问接口。

#### Scenario: API数据查询
- **WHEN** 前端发送GET请求到/api/customers
- **THEN** 系统返回JSON格式的客户列表

#### Scenario: API数据创建
- **WHEN** 前端发送POST请求到/api/customers
- **THEN** 系统创建客户记录并返回创建结果

#### Scenario: API数据更新
- **WHEN** 前端发送PUT请求到/api/customers/{id}
- **THEN** 系统更新指定客户记录

#### Scenario: API数据删除
- **WHEN** 前端发送DELETE请求到/api/customers/{id}
- **THEN** 系统删除指定客户记录

### Requirement: 文件存储
系统 SHALL 将上传的Word文件存储在服务器文件系统中。

#### Scenario: 文件上传存储
- **WHEN** 用户上传Word文档
- **THEN** 系统将文件保存到uploads/word-files/{date}/{guid}.docx

#### Scenario: 填充文件存储
- **WHEN** 系统生成填充后的文档
- **THEN** 系统将文件保存到uploads/filled-files/{date}/{guid}.docx

#### Scenario: 文件清理
- **WHEN** 关联的数据记录被删除
- **THEN** 系统清理对应的物理文件
