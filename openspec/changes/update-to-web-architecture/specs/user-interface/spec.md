## MODIFIED Requirements

### Requirement: 响应式布局
系统 SHALL 支持在不同尺寸屏幕上的自适应布局。

#### Scenario: 自适应布局
- **WHEN** 用户在不同尺寸的屏幕上访问系统
- **THEN** 界面自适应调整布局

---

### Requirement: 匹配方式选择
系统 SHALL 在智能填充界面提供匹配方式选项。

#### Scenario: 匹配方式选项
- **WHEN** 用户选择表格后
- **THEN** 系统显示匹配方式选项（相似度/Embedding/LLM混合）

---

### Requirement: 得分详情查看
系统 SHALL 在匹配预览中提供详细得分计算过程展示。

#### Scenario: 得分详情弹窗
- **WHEN** 用户点击某行的得分
- **THEN** 系统弹窗显示详细的得分计算过程

---

### Requirement: 操作历史撤销
系统 SHALL 支持可撤销操作的撤销能力。

#### Scenario: 撤销操作
- **WHEN** 用户点击可撤销操作的撤销按钮
- **THEN** 系统执行撤销并更新历史状态
