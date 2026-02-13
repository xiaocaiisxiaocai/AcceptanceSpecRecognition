## ADDED Requirements

### Requirement: 机型管理API
系统 SHALL 提供机型基础数据的RESTful接口。

#### Scenario: 机型列表查询
- **WHEN** 前端发送GET请求到/api/machine-models
- **THEN** 系统返回JSON格式的机型列表

#### Scenario: 机型创建
- **WHEN** 前端发送POST请求到/api/machine-models
- **THEN** 系统创建机型记录并返回创建结果

#### Scenario: 机型更新
- **WHEN** 前端发送PUT请求到/api/machine-models/{id}
- **THEN** 系统更新指定机型记录

#### Scenario: 机型删除
- **WHEN** 前端发送DELETE请求到/api/machine-models/{id}
- **THEN** 系统删除指定机型记录

---

## MODIFIED Requirements

### Requirement: 基础数据RESTful API
系统 SHALL 通过ASP.NET Core Web API提供基础数据访问接口，并支持机型维度。

#### Scenario: 验收规格创建/更新
- **WHEN** 前端创建或更新验收规格
- **THEN** CustomerId 必填，ProcessId/ MachineModelId 可为空

#### Scenario: 验收规格筛选
- **WHEN** 前端按客户、制程、机型筛选验收规格
- **THEN** 系统按(CustomerId, ProcessId, MachineModelId)组合条件返回结果
