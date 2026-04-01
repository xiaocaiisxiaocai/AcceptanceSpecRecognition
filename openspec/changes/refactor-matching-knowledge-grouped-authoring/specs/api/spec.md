## MODIFIED Requirements
### Requirement: 统一匹配知识配置 API
系统 SHALL 提供统一的匹配知识配置 API，用于读取、保存和重置当前生效的结构化匹配知识，并对外暴露分组式作者视图而不是运行时内部展开模型。

#### Scenario: 读取当前匹配知识配置
- **WHEN** 前端发送 `GET /api/matching-knowledge`
- **THEN** 系统返回当前生效配置对应的实体组、单位组、字段组、左右冲突组和单位换算配置
- **AND** 三类别名组以“首项为标准值”的顺序语义返回
- **AND** 不直接把内部展开后的逐条别名字典作为页面编辑主模型返回

#### Scenario: 保存匹配知识配置
- **GIVEN** 用户已编辑匹配知识配置的分组作者视图
- **WHEN** 前端发送 `PUT /api/matching-knowledge`
- **THEN** 系统校验并持久化整套分组配置
- **AND** 系统在保存时将实体组、单位组、字段组展开为运行时别名字典
- **AND** 系统在保存时将左右冲突组展开为运行时冲突词对
- **AND** 后续匹配请求读取更新后的展开配置

#### Scenario: 保存时拒绝词项归属冲突
- **GIVEN** 同一个词在同一分类下被提交到两个不同分组
- **WHEN** 前端发送 `PUT /api/matching-knowledge`
- **THEN** 系统拒绝保存
- **AND** 返回明确的冲突词项与分类说明

#### Scenario: 保存时折叠重复冲突组合
- **GIVEN** 某条左右冲突组展开后会生成重复或对称重复的冲突对
- **WHEN** 系统处理保存请求
- **THEN** 系统自动折叠重复冲突对
- **AND** 不把重复组合写入最终生效配置

#### Scenario: 恢复为系统默认配置
- **WHEN** 前端发送恢复默认配置请求
- **THEN** 系统将当前匹配知识恢复为系统默认配置
- **AND** 返回重置后的完整分组作者视图

#### Scenario: 旧配置接口移除
- **WHEN** 客户端访问 `/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
- **THEN** 系统不再提供这些旧配置接口
