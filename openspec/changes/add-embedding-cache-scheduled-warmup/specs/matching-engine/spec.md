## MODIFIED Requirements
### Requirement: Embedding 向量匹配
系统 SHALL 使用 Embedding 向量相似度作为第一阶段召回能力，而不是最终裁决依据。系统 SHALL 优先复用当前模型、当前用途与当前文本指纹匹配的有效缓存；缓存缺失时可按需生成并写回缓存。

#### Scenario: Embedding 用于召回候选
- **GIVEN** 输入查询文本"不锈钢管材"
- **WHEN** 系统执行匹配
- **THEN** 系统计算查询文本与候选【项目+规格】组合文本的 Embedding 相似度
- **AND** 使用该相似度召回 TopK 候选
- **AND** 不直接以 Embedding 分数作为最终高置信判定结果
- **AND** 候选向量优先从匹配用途的有效缓存读取

### Requirement: 验收规格语义检索
系统 SHALL 支持针对验收规格数据执行语义检索，并返回 TopN 相似结果。系统 SHALL 使用语义搜索用途的 Embedding 缓存，避免复用智能匹配用途的候选向量。

#### Scenario: 组合文本语义检索
- **GIVEN** 一条查询文本
- **WHEN** 系统执行验收规格语义检索
- **THEN** 系统将查询文本与候选规格的组合文本进行 Embedding 相似度计算
- **AND** 组合文本至少包含项目、规格、验收标准、备注信息
- **AND** 候选向量优先从语义搜索用途的有效缓存读取

#### Scenario: 批量查询复用候选集合
- **GIVEN** 一次请求中包含多条查询文本
- **WHEN** 系统执行语义检索
- **THEN** 系统复用同一批候选规格集合完成多条查询计算
- **AND** 分别返回每条查询的 TopN 结果

#### Scenario: 结果按相似度排序
- **GIVEN** 某条查询命中了多条候选规格
- **WHEN** 系统返回结果
- **THEN** 系统按相似度得分降序排列候选结果

#### Scenario: 结果应用最小分数过滤
- **GIVEN** 用户设置最小分数阈值
- **WHEN** 系统返回语义检索结果
- **THEN** 系统过滤掉低于阈值的候选
- **AND** 保留高于等于阈值的结果

### Requirement: 导入阶段疑似重复识别
系统 SHALL 在导入阶段支持基于规则和 AI 的疑似重复识别。系统 SHALL 在启用 AI 疑似重复识别时优先复用导入重复识别用途的有效 Embedding 缓存，避免每次导入都重复计算全部既有候选向量。

#### Scenario: 规则命中优先
- **GIVEN** 导入行与数据库已有规格完全一致，或者项目与规格完全一致但验收/备注不同
- **WHEN** 系统执行导入前检查
- **THEN** 系统优先返回规则命中结果
- **AND** 不再对该导入行继续执行 AI 疑似重复识别

#### Scenario: Embedding 召回语义候选
- **GIVEN** 导入行未命中规则层
- **AND** 用户启用了 AI 疑似重复识别
- **WHEN** 系统执行导入前检查
- **THEN** 系统基于“项目 + 规格”组合文本计算 Embedding 相似度
- **AND** 返回得分达到阈值的 Top-K 既有规格候选
- **AND** 既有候选向量优先从导入重复识别用途的有效缓存读取

#### Scenario: LLM 复核语义候选
- **GIVEN** 用户启用了 LLM 复核
- **AND** Embedding 已召回至少一条候选
- **WHEN** 系统执行语义复核
- **THEN** 系统对候选的项目与规格语义是否代表同一条验收规格进行复核
- **AND** 仅将达到 LLM 通过阈值的候选作为语义命中结果返回

#### Scenario: 覆盖已有记录
- **GIVEN** 用户在导入确认中选择覆盖已有
- **WHEN** 系统执行最终导入
- **THEN** 系统更新命中的已有规格记录
- **AND** 不新增一条重复的验收规格记录
- **AND** 系统使该规格受影响的 Embedding 缓存失效或等待后台重新生成

## ADDED Requirements
### Requirement: Embedding 缓存定时预热
系统 SHALL 支持在配置的低峰时间批量预热验收规格 Embedding 缓存。

#### Scenario: 定时补齐历史缓存
- **GIVEN** 系统配置了每日预热时间
- **AND** 历史验收规格存在缺失的匹配用途缓存
- **WHEN** 到达预热时间
- **THEN** 后台任务批量生成缺失向量
- **AND** 写入 `EmbeddingCaches`
- **AND** 单轮处理数量不得超过配置上限

#### Scenario: Embedding 服务不可用
- **GIVEN** 到达预热时间
- **AND** Embedding 服务不可用
- **WHEN** 后台任务尝试预热缓存
- **THEN** 系统记录失败日志
- **AND** 不删除已有缓存
- **AND** 不改变智能填充请求中 Embedding 服务不可用时显式失败的行为
