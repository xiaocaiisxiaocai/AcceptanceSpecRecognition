# Project Context

## Purpose
验收规格管理系统（Acceptance Specification System）是一个Windows桌面应用程序，用于帮助企业验收工程师高效管理和复用验收规格数据。系统从Word文档中提取验收表格数据，存储到本地数据库，并在新文档中通过AI智能匹配技术自动填充历史验收数据。

### 核心目标
1. 批量提取Word文档中的验收表格数据
2. 按客户和制程维度组织存储数据
3. 通过智能匹配（相似度/Embedding/LLM）自动填充新文档
4. 提供配置复用和操作历史管理
5. **支持在线AI服务和本地私有化部署，满足不同安全需求**

## Tech Stack

### 核心框架
| 层次 | 技术选型 | 说明 |
|------|----------|------|
| UI框架 | WinForms (.NET 8) | 桌面应用界面 |
| 后端 | C# .NET 8 | 业务逻辑处理 |
| ORM | **Entity Framework Core** | **Code First模式** |
| 数据库 | SQLite | 本地数据存储 |
| Word处理 | DocumentFormat.OpenXml | Office文档操作 |
| AI框架 | Microsoft Semantic Kernel | AI服务编排 |

### AI服务支持

#### 在线服务
| 服务 | 用途 | 特点 |
|------|------|------|
| OpenAI API | LLM + Embedding | 最新模型、快速集成 |
| Azure OpenAI | LLM + Embedding | 企业合规、SLA保障 |

#### 本地私有化服务
| 服务 | 用途 | 特点 |
|------|------|------|
| Ollama | LLM + Embedding | 开源免费、离线可用 |
| LM Studio | LLM + Embedding | GUI友好、易于管理 |
| 自定义端点 | LLM + Embedding | OpenAI兼容API |

### NuGet包
- Microsoft.EntityFrameworkCore.Sqlite
- Microsoft.EntityFrameworkCore.Design
- DocumentFormat.OpenXml
- Microsoft.SemanticKernel
- Microsoft.SemanticKernel.Connectors.OpenAI
- Newtonsoft.Json
- OpenCCDotNet（简繁转换）

## Project Conventions

### Code Style
- 命名规范：C#标准PascalCase（类、方法）、camelCase（局部变量）
- 接口命名：以I开头（如IMatchService）
- 异步方法：以Async后缀（如GetDataAsync）
- 注释语言：中文注释，关键逻辑必须注释

### Architecture Patterns
- **分层架构**：Presentation → Business → Data
- **依赖注入**：通过构造函数注入，使用Microsoft.Extensions.DependencyInjection
- **EF Core Code First**：实体优先，迁移管理Schema变更
- **工厂模式**：AI服务创建使用工厂模式支持多提供商

### EF Core Conventions
- 实体类放置在`Entities`文件夹
- DbContext配置使用Fluent API
- 迁移文件统一放置在`Migrations`文件夹
- 使用`dotnet ef migrations add`生成迁移
- 启动时自动应用待执行迁移

### Testing Strategy
- 单元测试：xUnit + Moq
- 数据层测试：使用InMemory SQLite
- AI集成测试：Mock AI服务响应
- 覆盖率目标：核心业务逻辑≥80%

### Git Workflow
- 主分支：main
- 功能分支：feature/xxx
- 修复分支：fix/xxx
- 提交信息：中文，格式为"类型: 描述"（如"feat: 添加Embedding匹配功能"）

## Domain Context

### 验收规格数据结构
- **项目**：验收项目名称（如"不锈钢管材"）
- **规格**：具体规格参数（如"Φ50×3mm"）
- **验收**：验收标准和方法
- **备注**：补充说明

### 匹配逻辑
- 查找依据：【项目】+【规格】组合
- 填充目标：【验收】+【备注】列
- 多结果处理：取最高匹配分
- 阈值过滤：低于阈值的结果自动丢弃

### 数据组织
- 按【客户】→【制程】层级组织
- 每条数据记录来源Word文件
- 支持数据溯源和撤销

### AI服务部署模式
- **在线模式**：使用云端API（OpenAI/Azure），需要网络连接
- **私有化模式**：使用本地服务（Ollama/LM Studio），离线可用
- **混合模式**：优先本地，本地不可用时降级到在线或相似度匹配

## Important Constraints
- **离线支持**：相似度匹配和本地AI服务必须支持离线使用
- **数据安全**：API Key等敏感信息加密存储
- **文件格式**：仅支持.docx格式
- **语言**：界面仅支持中文
- **单机部署**：无需服务端，本地运行
- **EF Core迁移**：Schema变更必须通过迁移管理，保证数据安全

## External Dependencies

### AI服务
| 服务类型 | 服务名称 | 用途 | 必需 |
|----------|----------|------|------|
| 在线 | OpenAI API | LLM + Embedding | 可选 |
| 在线 | Azure OpenAI | LLM + Embedding | 可选 |
| 本地 | Ollama | LLM + Embedding | 可选 |
| 本地 | LM Studio | LLM + Embedding | 可选 |
| 本地 | 自定义端点 | LLM + Embedding | 可选 |

### 推荐本地模型
| 用途 | 推荐模型 | 说明 |
|------|----------|------|
| LLM | qwen2:7b, llama3:8b | 中文能力强 |
| Embedding | nomic-embed-text, bge-m3 | 中文向量化 |
