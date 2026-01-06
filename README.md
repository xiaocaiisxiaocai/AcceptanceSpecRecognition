# 验收规范智能识别系统

基于 Embedding + LLM 的验收规范智能匹配系统，支持语义匹配、错别字修正、同义词扩展和关键字高亮。

## 技术栈

- **后端**: C# .NET 8 Web API
- **前端**: React + TypeScript + Ant Design
- **AI**: OpenAI Embedding + GPT-4o-mini
- **数据存储**: JSON 文件

## 项目结构

```
├── backend/                    # 后端项目
│   ├── src/
│   │   ├── AcceptanceSpecRecognition.Api/     # Web API
│   │   └── AcceptanceSpecRecognition.Core/    # 核心业务逻辑
│   └── tests/                  # 单元测试和属性测试
├── frontend/                   # React 前端
│   ├── src/
│   │   ├── components/         # 通用组件
│   │   ├── pages/              # 页面组件
│   │   ├── services/           # API 服务
│   │   └── types/              # TypeScript 类型定义
│   └── ...
└── .kiro/specs/               # 需求和设计文档
```

## 快速开始

### 1. 配置 OpenAI API Key

编辑 `backend/src/AcceptanceSpecRecognition.Api/data/config.json`，填入你的 OpenAI API Key：

```json
{
  "embedding": {
    "apiKey": "your-openai-api-key"
  },
  "llm": {
    "apiKey": "your-openai-api-key"
  }
}
```

### 2. 启动后端

```bash
cd backend
dotnet run --project src/AcceptanceSpecRecognition.Api
```

后端将在 http://localhost:5000 启动。

### 3. 启动前端

```bash
cd frontend
npm install
npm run dev
```

前端将在 http://localhost:5173 启动。

## 功能特性

### 核心功能
- **语义匹配**: 基于 Embedding 向量的语义相似度匹配
- **LLM 精排**: 使用 GPT 进行候选结果分析和冲突检测
- **置信度分级**: 高/中/低/无匹配四级置信度
- **批量处理**: 支持 CSV 文件批量匹配

### 文本预处理
- 中英文标点符号标准化
- 全角/半角字符转换
- 错别字自动修正
- 单位标准化（伏→V，千瓦→kW）

### 辅助功能
- 同义词扩展
- 关键字高亮
- 审计日志
- 配置管理

## API 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | /api/match | 单条匹配查询 |
| POST | /api/match/batch | 批量匹配 |
| GET | /api/history | 获取历史记录 |
| POST | /api/history | 添加历史记录 |
| GET | /api/config | 获取系统配置 |
| PUT | /api/config | 更新系统配置 |
| GET | /api/audit | 查询审计日志 |

## 运行测试

```bash
cd backend
dotnet test
```

## 配置说明

### 匹配配置
- `topN`: 返回候选数量（默认5）
- `highConfidenceThreshold`: 高置信度阈值（默认0.92）
- `mediumConfidenceThreshold`: 中置信度阈值（默认0.80）
- `lowConfidenceThreshold`: 低置信度阈值（默认0.60）
- `useLLMForFinalDecision`: 是否使用LLM进行最终决策

### 数据文件
- `history_records.json`: 历史记录数据
- `synonyms.json`: 同义词库
- `keywords.json`: 关键字库
- `typo_corrections.json`: 错别字映射
- `unit_mappings.json`: 单位映射（系统内置）
