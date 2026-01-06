# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

验收规范智能识别系统 - 基于 Embedding + LLM 的验收规范智能匹配系统，支持语义匹配、错别字修正、同义词扩展和关键字高亮。

## 常用命令

### 后端 (C# .NET 8)

```bash
# 启动后端 API（在 backend 目录）
cd backend
dotnet run --project src/AcceptanceSpecRecognition.Api

# 运行所有测试
dotnet test

# 运行单个测试文件
dotnet test --filter "FullyQualifiedName~TextPreprocessorTests"

# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~TextPreprocessorTests.NormalizeSymbols_ShouldConvertChinesePunctuation"

# 构建项目
dotnet build
```

### 前端 (React + TypeScript + Vite)

```bash
# 启动开发服务器（在 frontend 目录）
cd frontend
npm run dev

# 运行测试
npm run test

# 监视模式运行测试
npm run test:watch

# 构建生产版本
npm run build

# 预览生产构建
npm run preview

# ESLint 检查
npm run lint
```

### 从项目根目录运行

```bash
# 后端
dotnet run --project backend/src/AcceptanceSpecRecognition.Api
dotnet test backend/tests/AcceptanceSpecRecognition.Tests

# 前端
npm run dev --prefix frontend
npm run test --prefix frontend
```

## 架构概览

```
├── backend/
│   ├── src/
│   │   ├── AcceptanceSpecRecognition.Api/      # Web API 层，Controllers
│   │   └── AcceptanceSpecRecognition.Core/     # 核心业务逻辑
│   │       ├── Interfaces/                      # 服务接口定义
│   │       ├── Models/                          # 数据模型
│   │       └── Services/                        # 服务实现
│   └── tests/AcceptanceSpecRecognition.Tests/  # xUnit + FsCheck 测试
├── frontend/
│   └── src/
│       ├── components/                          # 通用 UI 组件
│       ├── pages/                               # 页面组件
│       ├── services/                            # API 调用服务
│       └── types/                               # TypeScript 类型定义
└── .kiro/specs/                                 # 需求和设计文档
```

### 核心服务架构

后端采用分层架构，核心服务通过 DI 注册：

- `ITextPreprocessor` - 文本预处理（符号标准化、错别字修正、单位标准化）
- `IEmbeddingService` - OpenAI Embedding 向量生成
- `ISynonymExpander` - 同义词扩展
- `IKeywordHighlighter` - 关键字高亮
- `ILLMService` - GPT-4o-mini 智能分析
- `IMatchingEngine` - 核心匹配引擎（整合预处理、向量匹配、LLM 精排）
- `IBatchProcessor` - 批量处理
- `IAuditLogger` - 审计日志
- `IConfigManager` - 配置管理

### 匹配流程

1. **预处理层**: 符号标准化 → 错别字修正 → 单位标准化
2. **匹配层**: Embedding 召回 → 同义词扩展 → 相似度计算 → LLM 精排 → 置信度评估
3. **输出层**: 结果格式化 → 关键字高亮 → 置信度标记

### API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | /api/match | 单条匹配查询 |
| POST | /api/match/batch | 批量匹配 |
| GET/POST | /api/history | 历史记录管理 |
| GET/PUT | /api/config | 系统配置 |
| GET | /api/audit | 审计日志 |
| GET | /api/health | 健康检查 |

### 数据文件

所有数据存储在 `backend/src/AcceptanceSpecRecognition.Api/data/` 目录：
- `config.json` - 系统配置（含 OpenAI API Key）
- `history_records.json` - 历史记录
- `synonyms.json` - 同义词库
- `keywords.json` - 关键字库
- `vector_cache.json` - 向量缓存

## 测试

后端使用 xUnit + FsCheck 进行单元测试和属性测试，前端使用 Vitest + React Testing Library。

属性测试覆盖关键不变量：文本预处理语义完整性、向量维度一致性、相似度范围、置信度分级正确性等。

## 技术栈

- **后端**: C# .NET 8, ASP.NET Core Web API, Swashbuckle (Swagger)
- **前端**: React 19, TypeScript, Vite, Ant Design 6, TanStack Query
- **AI**: OpenAI text-embedding-3-small, GPT-4o-mini
- **测试**: xUnit, FsCheck, Moq (后端); Vitest, React Testing Library (前端)
