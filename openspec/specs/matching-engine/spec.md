# Matching Engine Capability

## Purpose
定义当前已实现的文本相似度匹配、候选排序、阈值过滤与文本预处理行为，作为智能匹配与预览结果输出的基础能力说明。

## Requirements

### Requirement: 文本相似度匹配
系统必须（SHALL）支持基于文本相似度算法的匹配功能。

#### Scenario: Levenshtein距离匹配
- **GIVEN** 用户启用Levenshtein算法
- **AND** 输入查询文本"不锈钢管材"
- **WHEN** 系统执行匹配
- **THEN** 系统计算查询文本与候选【项目+规格】组合文本的Levenshtein相似度
- **AND** 返回相似度得分（0-1之间，1为完全匹配）

#### Scenario: Jaccard相似度匹配
- **GIVEN** 用户启用Jaccard算法
- **AND** 输入查询文本"304不锈钢 外径50mm"
- **WHEN** 系统执行匹配
- **THEN** 系统对文本进行分词并计算词集合的Jaccard系数
- **AND** 返回相似度得分（0-1之间）

#### Scenario: 余弦相似度匹配
- **GIVEN** 用户启用Cosine算法
- **AND** 输入查询文本
- **WHEN** 系统执行匹配
- **THEN** 系统将文本向量化（TF-IDF或词频）
- **AND** 计算向量间的余弦相似度
- **AND** 返回相似度得分（0-1之间）

---

### Requirement: 项目与规格组合文本匹配
系统必须（SHALL）基于【项目】与【规格】拼接后的组合文本进行匹配计算。

#### Scenario: 组合文本匹配
- **GIVEN** 用户输入项目="不锈钢管"，规格="Φ50×3mm"
- **WHEN** 系统执行匹配
- **THEN** 系统将项目与规格拼接为"不锈钢管 Φ50×3mm"
- **AND** 使用拼接文本参与相似度计算

---

### Requirement: 候选结果排序与Top-N
系统必须（SHALL）按得分降序返回候选结果，并限制返回数量。

#### Scenario: Top-N候选
- **GIVEN** 匹配结果包含多条候选
- **AND** 用户配置最大候选数为5
- **WHEN** 系统返回匹配结果
- **THEN** 仅返回得分最高的前5条候选
- **AND** 候选列表按得分降序排列

---

### Requirement: 阈值过滤
系统必须（SHALL）支持可配置的匹配阈值，低于阈值的结果自动过滤。

#### Scenario: 应用阈值过滤
- **GIVEN** 用户设置匹配阈值为0.3
- **AND** 匹配结果中有得分0.95、0.42、0.28的记录
- **WHEN** 系统应用阈值过滤
- **THEN** 系统返回得分0.95与0.42的记录
- **AND** 过滤得分0.28的记录

#### Scenario: 无结果达到阈值
- **GIVEN** 用户设置匹配阈值为0.9
- **AND** 所有匹配结果得分均低于0.9
- **WHEN** 系统应用阈值过滤
- **THEN** 系统返回空结果集

---

### Requirement: 匹配结果包含算法得分明细
系统必须（SHALL）在匹配结果中返回各算法的得分明细。

#### Scenario: 返回算法得分明细
- **GIVEN** 用户启用Levenshtein、Jaccard、Cosine算法
- **WHEN** 系统返回候选结果
- **THEN** 每条结果包含各算法的得分明细（如"Levenshtein"、"Jaccard"、"Cosine"）

---

### Requirement: 默认选择最高得分
系统必须（SHALL）将得分最高的候选作为默认最佳匹配。

#### Scenario: 默认最佳匹配
- **GIVEN** 候选结果得分分别为0.95、0.88、0.82
- **WHEN** 系统生成预览结果
- **THEN** 得分0.95的结果被标记为最佳匹配

---

### Requirement: 文本预处理集成
系统必须（SHALL）在匹配前应用文本预处理能力。

#### Scenario: 简繁转换后匹配
- **GIVEN** 用户启用简繁转换（简体→台湾繁体）
- **AND** 查询文本为"不锈钢"（简体）
- **AND** 候选文本包含"不鏽鋼"（繁体）
- **WHEN** 系统执行匹配
- **THEN** 系统先完成简繁转换再进行匹配

#### Scenario: 同义词替换后匹配
- **GIVEN** 用户启用同义词替换
- **AND** 同义词组包含"外径"与"OD"
- **AND** 查询文本为"OD50mm"
- **WHEN** 系统执行匹配
- **THEN** 系统将"OD"替换为"外径"后参与匹配

#### Scenario: OK/NG格式统一
- **GIVEN** 用户启用OK/NG格式转换
- **AND** 查询文本包含"OK"
- **WHEN** 系统执行匹配
- **THEN** 系统按配置的OK标准格式统一文本后参与匹配
