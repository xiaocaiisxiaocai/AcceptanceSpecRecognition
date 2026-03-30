## ADDED Requirements
### Requirement: Prompt 模板预览与重置 API
系统 SHALL 提供 Prompt 模板预览和按场景恢复系统默认内容的 API。

#### Scenario: 预览模板
- **WHEN** 前端发送 Prompt 模板预览请求
- **THEN** 系统返回模板校验结果、样例渲染内容与结构化输出校验结果

#### Scenario: 按场景恢复默认模板
- **WHEN** 前端请求恢复某个系统模板场景的默认内容
- **THEN** 系统仅重置该场景模板内容
- **AND** 返回重置后的模板数据

## MODIFIED Requirements
### Requirement: 基础数据RESTful API
系统 SHALL 通过ASP.NET Core Web API提供基础数据与系统配置访问接口。

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

#### Scenario: Prompt 模板配置接口
- **WHEN** 前端访问 Prompt 模板相关接口
- **THEN** 系统按系统模板场景返回模板数据
- **AND** 系统支持模板校验预览与按场景恢复默认内容
