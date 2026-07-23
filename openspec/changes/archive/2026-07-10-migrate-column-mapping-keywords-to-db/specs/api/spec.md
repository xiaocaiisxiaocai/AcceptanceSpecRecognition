## ADDED Requirements

### Requirement: 列映射规则恢复默认词 API
系统 SHALL 提供 `POST /api/column-mapping-rules/restore-defaults` 接口，用于按词补齐缺失的内置（Builtin、全局）表头字段默认词。接口 SHALL 支持可选 `targetField` 参数以仅恢复指定字段；SHALL NOT 增删改手动、学习或客户级规则。接口 SHALL 受管理接口权限授权约束。

#### Scenario: 恢复全部字段默认词
- **WHEN** 已授权用户调用 restore-defaults 且不带 `targetField`
- **THEN** 所有内置全局词已缺失的字段被重新补齐，返回成功

#### Scenario: 仅恢复指定字段
- **WHEN** 已授权用户调用 restore-defaults 并指定 `targetField`
- **THEN** 仅该字段缺失的内置默认词被补齐，其余字段不受影响

#### Scenario: 不影响用户自定义规则
- **WHEN** restore-defaults 执行
- **THEN** 手动、学习与客户级规则保持不变
