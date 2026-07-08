## ADDED Requirements

### Requirement: 列映射规则内置默认词启动幂等补齐
系统 SHALL 在启动时按字段幂等补齐内置（`Source=Builtin`、全局 `CustomerId=null`）表头字段默认词：对每个目标字段，当数据库不存在该字段的任何内置全局规则时，SHALL 播种该字段的全部默认词（`Enabled=true`、`MatchMode=Contains`）；当已存在至少一条时，SHALL 整组跳过而不改动。补齐过程 SHALL NOT 修改或删除任何手动（Manual）、学习（Learned）或客户级规则。

#### Scenario: 空库首次启动播种默认词
- **WHEN** `ColumnMappingRules` 表不含任何内置全局规则时系统启动
- **THEN** 四个目标字段的内置默认词被写入，标记为内置来源、全局、包含匹配

#### Scenario: 重复启动不产生重复
- **WHEN** 已播种内置默认词的系统再次启动
- **THEN** 各字段整组跳过，不新增重复规则，用户对内置词的停用/新增保持不变

#### Scenario: 不影响非内置规则
- **WHEN** 启动补齐执行
- **THEN** 手动、学习与客户级规则不被增删改
