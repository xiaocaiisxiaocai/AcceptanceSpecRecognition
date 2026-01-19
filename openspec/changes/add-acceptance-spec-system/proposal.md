# Change: 验收规格管理系统

## Why

企业在进行产品验收时，需要处理大量Word格式的验收规格文档。这些文档包含标准化的表格数据（项目、规格、验收标准、备注），但目前缺乏有效的工具来：
1. 批量提取和存储这些表格数据
2. 在新文档中智能匹配和填充历史验收数据
3. 基于不同客户和制程复用历史验收经验

本系统旨在通过AI智能匹配（相似度、Embedding、LLM）帮助用户快速填充新验收文档，提高工作效率并保证验收标准的一致性。

## What Changes

### 核心功能
- **Word文档处理**：支持.docx格式，提取表格数据（含嵌套表格），处理合并单元格
- **数据存储**：按客户+制程维度存储验收数据到SQLite，记录数据来源
- **智能匹配引擎**：支持三种匹配方式
  - 文本相似度匹配（字符串相似度算法）
  - Embedding向量匹配（支持本地/云端模型）
  - LLM+Embedding混合匹配（使用Semantic Kernel框架）
- **配置管理**：保存/加载匹配配置，支持导入导出
- **用户界面**：WinForms桌面应用，支持预览、确认、批量处理

### 数据模型
- 固定四列结构：项目、规格、验收、备注
- 按【项目+规格】查找，填充【验收+备注】
- 多结果取最高匹配分，支持可配置阈值

### AI集成
- LLM提供商：Azure OpenAI、OpenAI API、Ollama本地模型
- Embedding模型：云端API + 本地模型（如bge-base-zh）
- 框架：Microsoft Semantic Kernel

## Impact

- **Affected specs**:
  - word-processing（新增）
  - data-storage（新增）
  - matching-engine（新增）
  - ai-integration（新增）
  - configuration（新增）
  - user-interface（新增）

- **Affected code**:
  - 新建完整WinForms应用
  - 数据库schema设计
  - AI服务集成层

## 技术栈

| 层次 | 技术选型 |
|------|----------|
| 前端 | WinForms (.NET 8) |
| 后端 | .NET 8 |
| 数据库 | SQLite |
| Word处理 | DocumentFormat.OpenXml |
| AI框架 | Microsoft Semantic Kernel |
| 本地LLM | Ollama |
| Embedding | Azure OpenAI / OpenAI / 本地模型 |

## 用户故事

### US-1: 数据导入
作为验收工程师，我希望能够选择Word文档中的特定表格，将其数据按客户和制程分类存储，以便后续查询和复用。

### US-2: 智能填充
作为验收工程师，我希望上传新的Word验收模板后，系统能根据【项目+规格】自动匹配历史验收数据，预览匹配结果和得分详情，确认后自动填充【验收+备注】列。

### US-3: 配置复用
作为验收工程师，我希望能够保存当前的匹配配置（列映射、匹配方式、阈值等），并在下次使用时快速加载，避免重复设置。

## 开放问题

1. ~~本地Embedding模型具体选用哪个？~~ → 支持用户配置
2. ~~相似度算法使用哪种？~~ → 提供Levenshtein、Jaccard、Cosine等多种选择
3. 是否需要支持撤销已填充到Word的内容？→ 需要操作历史/撤销功能
