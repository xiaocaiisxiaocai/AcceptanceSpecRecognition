# Data Storage Capability

## Purpose
定义当前系统的数据存储方式、核心数据组织与索引策略，确保验收规格数据在MySQL中可按客户与制程组合稳定查询并支持迁移。

## Requirements

### Requirement: MySQL数据库存储
系统 SHALL 使用MySQL数据库存储业务数据，并通过Entity Framework Core访问。

#### Scenario: 数据库连接
- **WHEN** 系统启动时
- **THEN** 系统使用配置的连接字符串建立MySQL连接

#### Scenario: 字符集支持
- **WHEN** 存储包含中文的数据
- **THEN** 系统以utf8mb4字符集正确存储与读取

---

### Requirement: 数据库迁移管理
系统 SHALL 使用EF Core Migrations管理数据库结构变更。

#### Scenario: 自动应用迁移
- **WHEN** 系统启动且存在待执行迁移
- **THEN** 系统应用所有待执行迁移

---

### Requirement: 验收规格按客户制程组合组织
系统 MUST 将验收规格按“客户 + 制程”的组合维度组织。

#### Scenario: 组合维度查询
- **WHEN** 用户选择一个客户与一个制程
- **THEN** 系统以该 (CustomerId, ProcessId) 组合为边界查询验收规格条目

---

### Requirement: 客户与制程独立维护
系统 SHALL 保持Customer与Process为独立基础数据。

#### Scenario: 制程无客户外键
- **WHEN** 系统维护制程数据
- **THEN** Process不包含CustomerId外键

---

### Requirement: 组合筛选索引
系统 SHALL 为验收规格建立用于组合筛选的索引。

#### Scenario: 组合索引
- **WHEN** 系统按 (CustomerId, ProcessId) 查询验收规格
- **THEN** 查询使用对应索引或等价实现
