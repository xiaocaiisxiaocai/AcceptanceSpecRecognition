# Configuration Capability

## ADDED Requirements

### Requirement: 配置数据模型
系统必须（SHALL）支持完整的配置数据结构，包含列映射、匹配方式、AI参数和阈值。

#### Scenario: 配置数据结构完整性
- **GIVEN** 用户创建新配置
- **WHEN** 系统验证配置
- **THEN** 配置必须包含以下字段：
  - name: 配置名称（用户自定义）
  - version: 配置版本号
  - columnMapping: 列映射设置
  - matchingConfig: 匹配配置（方式、阈值、算法）
  - aiConfig: AI参数配置
- **AND** 缺少必填字段时提示错误

---

### Requirement: 列映射配置
系统必须（SHALL）支持配置源表格和目标表格的列映射关系。

#### Scenario: 配置源表格列映射
- **GIVEN** 用户编辑配置的列映射
- **WHEN** 用户指定源表格列
- **THEN** 用户可以设置：
  - 项目列索引（默认0）
  - 规格列索引（默认1）
  - 验收列索引（默认2）
  - 备注列索引（默认3）

#### Scenario: 配置目标表格列映射
- **GIVEN** 用户编辑配置的列映射
- **WHEN** 用户指定目标表格列
- **THEN** 用户可以设置：
  - 项目列索引（用于查找）
  - 规格列索引（用于查找）
  - 验收列索引（用于填充）
  - 备注列索引（用于填充）

---

### Requirement: 匹配配置
系统必须（SHALL）支持配置匹配方式和相关参数。

#### Scenario: 配置匹配方式
- **GIVEN** 用户编辑配置的匹配设置
- **WHEN** 用户选择匹配方式
- **THEN** 用户可以选择：
  - similarity: 文本相似度匹配
  - embedding: Embedding向量匹配
  - llm_embedding: LLM+Embedding混合匹配

#### Scenario: 配置相似度算法
- **GIVEN** 用户选择文本相似度匹配
- **WHEN** 用户配置算法
- **THEN** 用户可以选择：
  - levenshtein: Levenshtein距离
  - jaccard: Jaccard相似度
  - cosine: 余弦相似度

#### Scenario: 配置匹配阈值
- **GIVEN** 用户编辑配置
- **WHEN** 用户设置阈值
- **THEN** 用户可以输入0-1之间的数值
- **AND** 默认值为0.8
- **AND** 超出范围时显示错误提示

#### Scenario: 配置列权重
- **GIVEN** 用户编辑配置
- **WHEN** 用户设置项目列和规格列的权重
- **THEN** 两列权重之和必须等于1
- **AND** 默认值各为0.5

---

### Requirement: AI参数配置
系统必须（SHALL）支持配置AI服务的连接参数。

#### Scenario: 配置OpenAI参数
- **GIVEN** 用户选择OpenAI作为AI提供商
- **WHEN** 用户填写配置
- **THEN** 用户需要提供：
  - API Key
  - Embedding模型名称（可选，默认text-embedding-ada-002）
  - LLM模型名称（可选，默认gpt-4）

#### Scenario: 配置Azure OpenAI参数
- **GIVEN** 用户选择Azure OpenAI作为AI提供商
- **WHEN** 用户填写配置
- **THEN** 用户需要提供：
  - Endpoint URL
  - API Key
  - Embedding部署名称
  - LLM部署名称

#### Scenario: 配置Ollama参数
- **GIVEN** 用户选择Ollama作为AI提供商
- **WHEN** 用户填写配置
- **THEN** 用户需要提供：
  - 服务地址（默认http://localhost:11434）
  - Embedding模型名称
  - LLM模型名称

---

### Requirement: 配置保存
系统必须（SHALL）支持将配置保存到本地文件系统。

#### Scenario: 保存新配置
- **GIVEN** 用户完成配置编辑
- **AND** 输入配置名称"客户A-制程X"
- **WHEN** 用户点击保存
- **THEN** 系统将配置保存为JSON文件
- **AND** 文件名为"客户A-制程X.json"
- **AND** 显示"配置保存成功"

#### Scenario: 覆盖已存在的配置
- **GIVEN** 已存在名为"客户A-制程X"的配置
- **WHEN** 用户保存同名配置
- **THEN** 系统提示"配置已存在，是否覆盖？"
- **AND** 用户确认后覆盖原文件

#### Scenario: 配置名称包含非法字符
- **GIVEN** 用户输入配置名称包含"/"或"\\"
- **WHEN** 用户尝试保存
- **THEN** 系统显示错误"配置名称不能包含特殊字符"

---

### Requirement: 配置加载
系统必须（SHALL）支持从本地加载已保存的配置。

#### Scenario: 加载配置列表
- **GIVEN** 用户打开配置管理界面
- **WHEN** 系统加载配置
- **THEN** 系统扫描配置目录
- **AND** 显示所有已保存的配置名称和创建时间

#### Scenario: 加载指定配置
- **GIVEN** 用户选择配置"客户A-制程X"
- **WHEN** 用户点击加载
- **THEN** 系统读取JSON文件
- **AND** 将配置应用到当前会话
- **AND** 显示"配置加载成功"

#### Scenario: 加载损坏的配置文件
- **GIVEN** 配置文件JSON格式损坏
- **WHEN** 系统尝试加载
- **THEN** 系统显示错误"配置文件损坏，无法加载"
- **AND** 提供删除该配置的选项

---

### Requirement: 配置导入导出
系统必须（SHALL）支持配置的导入和导出功能。

#### Scenario: 导出配置
- **GIVEN** 用户选择要导出的配置
- **WHEN** 用户点击导出按钮
- **THEN** 系统弹出文件保存对话框
- **AND** 用户选择保存位置
- **AND** 系统将配置导出为.json文件
- **AND** API Key等敏感信息被清空或加密

#### Scenario: 导入配置
- **GIVEN** 用户点击导入按钮
- **WHEN** 用户选择.json配置文件
- **THEN** 系统验证文件格式
- **AND** 如果格式正确，将配置添加到配置列表
- **AND** 提示用户填写敏感信息（如API Key）

#### Scenario: 导入无效配置文件
- **GIVEN** 用户选择的文件不是有效的配置文件
- **WHEN** 系统验证格式
- **THEN** 系统显示错误"无效的配置文件格式"

---

### Requirement: 配置版本管理
系统必须（SHALL）支持配置的版本控制。

#### Scenario: 配置版本升级
- **GIVEN** 系统升级后配置格式发生变化
- **WHEN** 系统加载旧版本配置
- **THEN** 系统自动迁移配置到新版本格式
- **AND** 保留原配置作为备份
- **AND** 显示"配置已升级到最新版本"

#### Scenario: 不兼容的配置版本
- **GIVEN** 配置版本过旧，无法自动迁移
- **WHEN** 系统尝试加载
- **THEN** 系统显示错误"配置版本过旧，请重新创建配置"

---

### Requirement: 文本处理配置
系统必须（SHALL）支持文本处理相关功能的配置管理。

#### Scenario: 配置简繁转换
- **GIVEN** 用户进入文本处理设置界面
- **WHEN** 用户配置简繁转换功能
- **THEN** 用户可以设置：
  - 启用/禁用简繁转换
  - 转换模式选择（简体→台湾繁体、台湾繁体→简体）
- **AND** 默认禁用

#### Scenario: 配置同义词功能
- **GIVEN** 用户进入文本处理设置界面
- **WHEN** 用户配置同义词功能
- **THEN** 用户可以设置：
  - 启用/禁用同义词替换
  - 进入同义词管理界面
- **AND** 默认禁用

#### Scenario: 配置OK/NG格式转换
- **GIVEN** 用户进入文本处理设置界面
- **WHEN** 用户配置OK/NG格式转换
- **THEN** 用户可以设置：
  - 启用/禁用格式转换
  - OK标准显示文本（默认"OK"）
  - NG标准显示文本（默认"NG"）
- **AND** 默认禁用

#### Scenario: 配置关键字高亮
- **GIVEN** 用户进入文本处理设置界面
- **WHEN** 用户配置关键字高亮功能
- **THEN** 用户可以设置：
  - 启用/禁用关键字高亮
  - 高亮颜色选择（颜色选择器）
  - 进入关键字管理界面
- **AND** 默认禁用，默认颜色黄色(#FFFF00)

---

### Requirement: 同义词管理
系统必须（SHALL）提供同义词管理功能，支持增删改查和导入导出。

#### Scenario: 添加同义词组
- **GIVEN** 用户进入同义词管理界面
- **WHEN** 用户点击添加按钮
- **THEN** 弹出输入对话框
- **AND** 用户输入逗号分隔的同义词（如"不锈钢,不鏽鋼,SUS"）
- **AND** 系统保存同义词组，第一个词为标准词

#### Scenario: 编辑同义词组
- **GIVEN** 用户选中一个同义词组
- **WHEN** 用户点击编辑按钮
- **THEN** 弹出编辑对话框，显示当前同义词
- **AND** 用户可以添加、删除、修改词语
- **AND** 用户可以更改标准词

#### Scenario: 删除同义词组
- **GIVEN** 用户选中一个或多个同义词组
- **WHEN** 用户点击删除按钮
- **THEN** 系统提示确认删除
- **AND** 用户确认后删除选中的同义词组

#### Scenario: 导出同义词
- **GIVEN** 用户在同义词管理界面
- **WHEN** 用户点击导出按钮
- **THEN** 弹出文件保存对话框
- **AND** 导出为CSV文件，每行一个同义词组

#### Scenario: 导入同义词
- **GIVEN** 用户在同义词管理界面
- **WHEN** 用户点击导入按钮并选择CSV文件
- **THEN** 系统解析文件内容
- **AND** 批量添加同义词组
- **AND** 显示导入结果（成功X条，跳过Y条重复）

---

### Requirement: 关键字管理
系统必须（SHALL）提供关键字管理功能，支持增删和导入。

#### Scenario: 添加关键字
- **GIVEN** 用户进入关键字管理界面
- **WHEN** 用户点击添加按钮
- **THEN** 弹出输入对话框
- **AND** 用户输入关键字
- **AND** 系统保存关键字（自动去重）

#### Scenario: 批量添加关键字
- **GIVEN** 用户进入关键字管理界面
- **WHEN** 用户点击批量添加按钮
- **THEN** 弹出多行输入对话框
- **AND** 用户输入多个关键字（每行一个或逗号分隔）
- **AND** 系统批量保存，自动去重

#### Scenario: 删除关键字
- **GIVEN** 用户选中一个或多个关键字
- **WHEN** 用户点击删除按钮
- **THEN** 系统删除选中的关键字

#### Scenario: 导入关键字
- **GIVEN** 用户在关键字管理界面
- **WHEN** 用户点击导入按钮并选择文本文件
- **THEN** 系统解析文件（每行一个关键字）
- **AND** 批量添加关键字，自动去重
- **AND** 显示导入结果
