## ADDED Requirements

### Requirement: LLM 配置项
系统 SHALL 在智能填充的匹配配置步骤提供 LLM 复核与生成的开关与参数配置。

#### Scenario: 配置 LLM 复核
- **WHEN** 用户进入匹配配置页面
- **THEN** 页面提供“启用 LLM 复核”开关与说明

#### Scenario: 配置 LLM 生成
- **WHEN** 用户进入匹配配置页面
- **THEN** 页面提供“启用 LLM 生成建议”开关与触发阈值说明

---

### Requirement: LLM 结果展示
系统 SHALL 在匹配预览与详情中展示 LLM 得分/理由或生成建议，并标记其来源，支持流式渲染。

#### Scenario: 详情展示 LLM 得分（流式）
- **WHEN** 用户查看匹配详情
- **THEN** 系统实时展示 LLM 复核得分与说明信息

#### Scenario: 展示生成建议（流式）
- **WHEN** 行为低置信度或无匹配且启用 LLM 生成
- **THEN** 预览中实时展示 LLM 生成的验收/备注建议

---

### Requirement: 生成建议可选填
系统 SHALL 允许用户选择 LLM 生成建议并执行填充。

#### Scenario: 选择生成建议填充
- **GIVEN** 系统返回 LLM 生成建议
- **WHEN** 用户选择该建议并执行填充
- **THEN** 系统将建议内容写入目标文档的验收/备注列

---

### Requirement: 仅展示最佳匹配
系统 SHALL 在匹配预览中仅展示最佳匹配结果，不提供候选选择列表。

#### Scenario: 仅展示最佳匹配
- **WHEN** 用户查看匹配预览
- **THEN** 页面仅展示最佳匹配结果

---

### Requirement: 不匹配原因展示
系统 SHALL 在无匹配时展示明确原因。

#### Scenario: 展示不匹配原因
- **GIVEN** 当前行无匹配结果
- **WHEN** 用户查看匹配预览
- **THEN** 页面展示不匹配原因（如范围内无候选、得分低于阈值、LLM降级说明）
