# Requirements Document

## Introduction

验收规范智能识别系统，用于根据历史记录自动识别和填写验收规范中的"实际规格/实际设计"和"备注"字段。系统需要处理语义相似、错别字、中英文符号差异等问题，并对关键字进行高亮卡控。由于是面向客户的回复，准确率要求达到99%以上。

## Glossary

- **Acceptance_Spec_System**: 验收规范智能识别系统，负责匹配历史记录并生成回复建议
- **Embedding_Service**: 向量嵌入服务，将文本转换为语义向量用于相似度计算
- **Text_Preprocessor**: 文本预处理器，负责标准化文本（符号转换、错别字修正等）
- **Keyword_Controller**: 关键字卡控器，负责识别和高亮关键字及其同义词
- **Confidence_Threshold**: 置信度阈值，用于判断匹配结果是否需要人工复核
- **History_Record**: 历史记录，存储项目、技术指标、实际规格、备注等信息（使用JSON文件存储）
- **Similarity_Score**: 相似度分数，表示查询与历史记录的匹配程度（0-1）

## Requirements

### Requirement 1: 文本预处理

**User Story:** As a 系统用户, I want 输入的文本能够被自动标准化处理, so that 中英文符号差异和常见错别字不会影响匹配准确性。

#### Acceptance Criteria

1. WHEN 用户输入包含中文标点符号的文本 THEN THE Text_Preprocessor SHALL 将中文标点转换为对应的英文标点
2. WHEN 用户输入包含全角字符的文本 THEN THE Text_Preprocessor SHALL 将全角字符转换为半角字符
3. WHEN 用户输入包含多余空格或换行的文本 THEN THE Text_Preprocessor SHALL 将其标准化为单个空格
4. WHEN 用户输入包含常见错别字的文本 THEN THE Text_Preprocessor SHALL 根据可配置的错别字映射表进行修正
5. WHEN 预处理规则需要调整 THEN THE Text_Preprocessor SHALL 支持通过配置文件修改预处理规则

### Requirement 2: 语义向量匹配

**User Story:** As a 系统用户, I want 系统能够理解文本的语义含义, so that 即使表述不同但含义相同的内容也能被正确匹配。

#### Acceptance Criteria

1. WHEN 用户提交项目名称和技术指标查询 THEN THE Embedding_Service SHALL 生成查询文本的语义向量
2. WHEN 生成语义向量后 THEN THE Acceptance_Spec_System SHALL 计算与历史记录向量的余弦相似度
3. WHEN 计算相似度完成 THEN THE Acceptance_Spec_System SHALL 返回相似度最高的Top-N条记录（N可配置，默认5）
4. WHEN Embedding模型需要更换 THEN THE Embedding_Service SHALL 支持配置不同的Embedding模型
5. WHEN 历史记录更新 THEN THE Embedding_Service SHALL 支持增量更新向量索引
6. WHEN 查询文本与历史记录语义相同但表述不同（如"电压"与"供电电压"、"额定功率"与"功率"、"3P/380V 50Hz"与"三相380伏50赫兹"） THEN THE Acceptance_Spec_System SHALL 识别为相似记录并返回匹配结果
7. WHEN 查询文本包含缩写或简称（如"PLC"与"可编程逻辑控制器"） THEN THE Acceptance_Spec_System SHALL 通过同义词扩展或Embedding语义理解进行匹配

### Requirement 8: 同义词与领域术语管理

**User Story:** As a 系统管理员, I want 能够管理领域专业术语和同义词, so that 系统能够识别相同语义的不同表述。

#### Acceptance Criteria

1. THE Acceptance_Spec_System SHALL 维护一个领域术语同义词库
2. WHEN 添加同义词关系 THEN THE Acceptance_Spec_System SHALL 支持定义双向或单向同义关系
3. WHEN 查询时遇到术语 THEN THE Acceptance_Spec_System SHALL 自动扩展查询包含其同义词
4. WHEN 同义词库更新 THEN THE Acceptance_Spec_System SHALL 支持批量导入导出同义词数据
5. THE Acceptance_Spec_System SHALL 预置常见工业领域术语同义词（如电气、PLC、传感器等领域）
6. WHEN 系统识别到潜在的新同义词关系（基于用户确认的匹配） THEN THE Acceptance_Spec_System SHALL 建议管理员添加到同义词库

### Requirement 3: 置信度判断与人工复核

**User Story:** As a 系统用户, I want 系统能够判断匹配结果的可信度, so that 低置信度的结果能够被标记出来进行人工复核，保证99%以上的准确率。

#### Acceptance Criteria

1. WHEN 匹配结果的Similarity_Score高于高置信度阈值（可配置，默认0.95） THEN THE Acceptance_Spec_System SHALL 标记为"自动填充"并直接使用
2. WHEN 匹配结果的Similarity_Score介于中置信度阈值（可配置，默认0.85）和高置信度阈值之间 THEN THE Acceptance_Spec_System SHALL 标记为"建议填充-需复核"
3. WHEN 匹配结果的Similarity_Score低于中置信度阈值 THEN THE Acceptance_Spec_System SHALL 标记为"低置信度-需人工处理"
4. WHEN 用户确认或修正匹配结果 THEN THE Acceptance_Spec_System SHALL 将反馈记录用于后续优化
5. WHEN 置信度阈值需要调整 THEN THE Acceptance_Spec_System SHALL 支持通过配置界面修改各级阈值

### Requirement 4: 关键字卡控与高亮

**User Story:** As a 系统用户, I want 系统能够识别并高亮关键字及其同义词, so that 重要信息能够被突出显示，便于快速审核。

#### Acceptance Criteria

1. WHEN 文本中包含关键字库中的关键字 THEN THE Keyword_Controller SHALL 对该关键字进行高亮标记
2. WHEN 文本中包含关键字的同义词 THEN THE Keyword_Controller SHALL 同样对该同义词进行高亮标记
3. WHEN 关键字库需要更新 THEN THE Keyword_Controller SHALL 支持通过管理界面添加、修改、删除关键字
4. WHEN 同义词关系需要定义 THEN THE Keyword_Controller SHALL 支持为每个关键字配置多个同义词
5. WHEN 高亮样式需要调整 THEN THE Keyword_Controller SHALL 支持配置不同的高亮颜色和样式

### Requirement 5: 历史记录管理

**User Story:** As a 系统管理员, I want 能够管理历史记录数据, so that 系统能够基于准确的历史数据进行匹配。

#### Acceptance Criteria

1. THE Acceptance_Spec_System SHALL 使用JSON文件存储历史记录数据
2. WHEN 新的验收记录完成 THEN THE Acceptance_Spec_System SHALL 将记录追加到历史记录文件
3. WHEN 存储新记录 THEN THE Acceptance_Spec_System SHALL 同时生成并缓存该记录的语义向量
4. WHEN 历史记录需要修正 THEN THE Acceptance_Spec_System SHALL 支持编辑历史记录并重新生成向量
5. WHEN 查询历史记录 THEN THE Acceptance_Spec_System SHALL 支持按项目、技术指标等字段进行筛选
6. THE Acceptance_Spec_System SHALL 提供预置的工业领域示例历史记录数据（电气参数、PLC配置、传感器规格等）

### Requirement 6: 参数配置管理

**User Story:** As a 系统管理员, I want 所有关键参数都可以配置和调整, so that 系统能够根据实际使用情况进行优化。

#### Acceptance Criteria

1. THE Acceptance_Spec_System SHALL 提供统一的参数配置界面
2. WHEN 修改配置参数 THEN THE Acceptance_Spec_System SHALL 支持实时生效或重启后生效（根据参数类型）
3. WHEN 配置参数被修改 THEN THE Acceptance_Spec_System SHALL 记录修改历史（包括修改人、时间、修改前后值）
4. THE Acceptance_Spec_System SHALL 支持配置以下参数：
   - Embedding模型选择
   - 相似度计算方法
   - 各级置信度阈值
   - Top-N返回数量
   - 错别字映射表
   - 关键字库和同义词
   - 高亮样式配置
5. WHEN 配置参数超出合理范围 THEN THE Acceptance_Spec_System SHALL 拒绝修改并提示错误信息

### Requirement 7: 配置数据文件管理

**User Story:** As a 系统管理员, I want 配置数据以文件形式存储和管理, so that 可以方便地编辑和版本控制。

#### Acceptance Criteria

1. THE Acceptance_Spec_System SHALL 使用JSON或YAML文件存储同义词库数据
2. THE Acceptance_Spec_System SHALL 使用JSON或YAML文件存储关键字库数据
3. THE Acceptance_Spec_System SHALL 使用JSON或YAML文件存储错别字映射表数据
4. THE Acceptance_Spec_System SHALL 提供预置的工业领域同义词库文件（工业术语、缩写对照等）
5. THE Acceptance_Spec_System SHALL 提供预置的关键字库文件（需要高亮的重要术语）
6. THE Acceptance_Spec_System SHALL 提供预置的错别字映射表文件
7. WHEN 配置文件被修改 THEN THE Acceptance_Spec_System SHALL 支持热加载或重启后生效

### Requirement 8: 匹配结果输出

**User Story:** As a 系统用户, I want 获得格式化的匹配结果, so that 可以直接用于填写验收规范表格。

#### Acceptance Criteria

1. WHEN 匹配成功 THEN THE Acceptance_Spec_System SHALL 返回"实际规格/实际设计"字段的建议值
2. WHEN 匹配成功 THEN THE Acceptance_Spec_System SHALL 返回"备注"字段的建议值
3. WHEN 返回结果 THEN THE Acceptance_Spec_System SHALL 包含置信度等级标识
4. WHEN 返回结果 THEN THE Acceptance_Spec_System SHALL 对关键字进行高亮处理
5. WHEN 返回结果 THEN THE Acceptance_Spec_System SHALL 显示匹配的历史记录来源（便于追溯）

### Requirement 9: 批量处理能力

**User Story:** As a 系统用户, I want 能够批量处理多条验收规范, so that 可以提高工作效率。

#### Acceptance Criteria

1. WHEN 用户上传包含多条验收规范的数据 THEN THE Acceptance_Spec_System SHALL 支持批量匹配处理
2. WHEN 批量处理完成 THEN THE Acceptance_Spec_System SHALL 返回每条记录的匹配结果和置信度
3. WHEN 批量结果中存在低置信度记录 THEN THE Acceptance_Spec_System SHALL 汇总标记需要人工复核的条目
4. THE Acceptance_Spec_System SHALL 支持导出批量处理结果

### Requirement 10: 匹配失败处理

**User Story:** As a 系统用户, I want 系统能够妥善处理无法匹配的情况, so that 不会遗漏任何需要处理的验收项。

#### Acceptance Criteria

1. WHEN 历史记录中没有相似匹配（所有相似度低于最低阈值，可配置，默认0.5） THEN THE Acceptance_Spec_System SHALL 标记为"无匹配-需人工处理"
2. WHEN 多条历史记录相似度非常接近（差值小于可配置阈值，默认0.02） THEN THE Acceptance_Spec_System SHALL 返回多个候选项供用户选择

### Requirement 11: 数值与单位智能处理

**User Story:** As a 系统用户, I want 系统能够智能处理数值和单位的不同表述, so that "380V"和"380伏"、"1KW"和"1000W"能被正确识别为相同含义。

#### Acceptance Criteria

1. WHEN 文本包含数值和单位 THEN THE Text_Preprocessor SHALL 识别并标准化单位表述（如V/伏、A/安、W/瓦、Hz/赫兹）
2. WHEN 文本包含不同量级的单位 THEN THE Text_Preprocessor SHALL 支持单位换算识别（如1KW=1000W）
3. WHEN 文本包含范围表述 THEN THE Text_Preprocessor SHALL 识别范围格式（如"380V±10%"、"380-420V"）
4. THE Acceptance_Spec_System SHALL 提供可配置的单位映射表
5. WHEN 文本包含电气类型前缀（如DC/直流、AC/交流） THEN THE Text_Preprocessor SHALL 将其作为独立的关键属性保留，不进行合并
6. WHEN 匹配时 THEN THE Acceptance_Spec_System SHALL 依赖Embedding模型的语义理解能力，自动区分DC24V与AC24V、单相与三相等关键电气属性差异
7. THE Acceptance_Spec_System SHALL 选用对工业/电气领域有良好语义理解能力的Embedding模型，确保互斥概念（如DC/AC、单相/三相、常开/常闭）不会被错误匹配

### Requirement 12: 审计与追溯

**User Story:** As a 系统管理员, I want 系统记录所有操作日志, so that 可以追溯问题和审计使用情况。

#### Acceptance Criteria

1. WHEN 用户执行匹配查询 THEN THE Acceptance_Spec_System SHALL 记录查询内容、匹配结果、置信度
2. WHEN 用户确认或修改匹配结果 THEN THE Acceptance_Spec_System SHALL 记录用户操作和最终选择
3. WHEN 配置被修改 THEN THE Acceptance_Spec_System SHALL 记录修改详情
4. THE Acceptance_Spec_System SHALL 支持按时间范围查询操作日志
5. THE Acceptance_Spec_System SHALL 使用JSON文件存储操作日志
