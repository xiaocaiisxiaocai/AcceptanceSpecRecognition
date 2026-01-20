# API Capability

## Purpose
提供基础数据的RESTful接口，覆盖客户、制程与验收规格等基础数据的查询、维护与前端/外部工具集成调用场景。

## Requirements

### Requirement: 基础数据RESTful API
系统 SHALL 通过ASP.NET Core Web API提供基础数据访问接口。

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
