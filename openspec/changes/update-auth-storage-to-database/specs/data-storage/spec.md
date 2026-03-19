## ADDED Requirements

### Requirement: 系统用户数据持久化
系统 SHALL 在数据库中持久化系统用户账号信息，而不是通过配置文件存储账号密码。

#### Scenario: 用户表结构
- **WHEN** 系统应用数据库迁移
- **THEN** 数据库中创建 `SystemUsers` 表并包含用户名、密码哈希、角色权限、启用状态等字段

#### Scenario: 用户名唯一
- **WHEN** 系统保存系统用户账号
- **THEN** `Username` 字段保持唯一约束

#### Scenario: 密码哈希存储
- **WHEN** 系统写入或更新用户密码
- **THEN** 数据库存储 PBKDF2 哈希值而非明文密码

---

### Requirement: 默认账号初始化
系统 SHALL 在用户表为空时自动写入默认账号，确保首次部署后可登录。

#### Scenario: 首次启动初始化
- **WHEN** 系统启动且 `SystemUsers` 表为空
- **THEN** 系统自动写入默认 `admin` 与 `common` 账号

#### Scenario: 非首次启动不重复初始化
- **WHEN** 系统启动且 `SystemUsers` 表已有数据
- **THEN** 系统不重复写入默认账号
