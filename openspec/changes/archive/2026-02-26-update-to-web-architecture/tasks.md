# 实施任务清单（Web架构版）

## 1. 项目重构初始化

- [x] 1.1 删除WinForms项目（src/AcceptanceSpecSystem）
- [x] 1.2 创建ASP.NET Core Web API项目（AcceptanceSpecSystem.Api）
- [x] 1.3 配置项目引用（Api引用Core和Data）
- [x] 1.4 配置Swagger/OpenAPI
- [x] 1.5 配置CORS策略
- [x] 1.6 配置统一异常处理中间件
- [x] 1.7 配置统一响应格式
- [x] 1.8 更新解决方案文件

## 2. 数据层MySQL适配

- [x] 2.1 替换NuGet包（Microsoft.EntityFrameworkCore.Sqlite → Pomelo.EntityFrameworkCore.MySql）
- [x] 2.2 更新AppDbContext配置（MySQL连接）
- [x] 2.3 删除旧的SQLite迁移文件
- [x] 2.4 创建MySQL数据库
- [x] 2.5 生成MySQL初始迁移
- [x] 2.6 应用迁移创建表结构
- [x] 2.7 更新测试项目数据库配置
- [x] 2.8 验证所有数据层测试通过

## 3. API层实现

### 3.1 基础设施
- [x] 3.1.1 创建统一响应模型（ApiResponse<T>）
- [x] 3.1.2 创建分页请求/响应模型
- [x] 3.1.3 创建DTOs基础结构

### 3.2 客户管理API
- [x] 3.2.1 CustomerDto/CreateCustomerRequest/UpdateCustomerRequest
- [x] 3.2.2 CustomersController（CRUD）
- [x] 3.2.3 获取客户的制程列表接口

### 3.3 制程管理API
- [x] 3.3.1 ProcessDto/CreateProcessRequest/UpdateProcessRequest
- [x] 3.3.2 ProcessesController（CRUD）
- [x] 3.3.3 获取制程的验收规格列表接口

### 3.4 验收规格API
- [x] 3.4.1 AcceptanceSpecDto/CreateSpecRequest
- [x] 3.4.2 SpecsController（CRUD）
- [x] 3.4.3 批量导入接口
- [x] 3.4.4 按客户/制程筛选接口

### 3.5 文档处理API
- [x] 3.5.1 DocumentsController
- [x] 3.5.2 文件上传接口（POST /api/documents/upload）
- [x] 3.5.3 获取表格列表接口（GET /api/documents/{id}/tables）
- [x] 3.5.4 获取表格数据预览接口
- [x] 3.5.5 数据导入接口（POST /api/documents/import）
- [x] 3.5.6 文件存储服务实现

### 3.6 智能匹配API
- [x] 3.6.1 MatchingController
- [x] 3.6.2 匹配预览接口（POST /api/matching/preview）
- [x] 3.6.3 执行填充接口（POST /api/matching/execute）
- [x] 3.6.4 下载填充结果接口（GET /api/matching/download/{id}）

### 3.7 AI服务配置API
- [x] 3.7.1 AiServicesController
- [x] 3.7.2 配置CRUD接口
- [x] 3.7.3 连接测试接口（POST /api/ai-services/{id}/test）

### 3.8 文本处理API
- [x] 3.8.1 TextProcessingController（配置管理）
- [x] 3.8.2 SynonymsController（同义词CRUD）
- [x] 3.8.3 KeywordsController（关键字CRUD）

### 3.9 其他API
- [x] 3.9.1 HistoryController（操作历史查询）
- [x] 3.9.2 PromptTemplatesController（Prompt模板CRUD）

## 4. 业务层补充（Core）

### 4.1 文本处理模块
- [x] 4.1.1 简繁转换服务（OpenCCNET集成）
- [x] 4.1.2 同义词服务
- [x] 4.1.3 关键字服务
- [x] 4.1.4 OK/NG格式转换服务
- [x] 4.1.5 文本预处理管道

### 4.2 匹配引擎
- [x] 4.2.1 相似度算法实现（Levenshtein、Jaccard、Cosine）
- [x] 4.2.2 Embedding匹配服务
- [x] 4.2.3 混合匹配服务

### 4.3 AI集成层
- [x] 4.3.1 Semantic Kernel配置
- [x] 4.3.2 OpenAI连接器
- [x] 4.3.3 Azure OpenAI连接器
- [x] 4.3.4 Ollama连接器
- [x] 4.3.5 AI服务工厂

## 5. 前端开发（pure-admin）

### 5.1 项目初始化
- [x] 5.1.1 克隆pure-admin-thin模板
- [x] 5.1.2 配置API代理（vite.config.ts）
- [x] 5.1.3 配置axios拦截器
- [x] 5.1.4 配置路由和菜单

### 5.2 API模块
- [x] 5.2.1 创建API请求封装
- [x] 5.2.2 客户管理API
- [x] 5.2.3 制程管理API
- [x] 5.2.4 验收规格API
- [x] 5.2.5 文档处理API
- [x] 5.2.6 匹配API

### 5.3 基础数据页面
- [x] 5.3.1 客户管理页面（列表、新增、编辑、删除）
- [x] 5.3.2 制程管理页面
- [x] 5.3.3 验收规格列表页面

### 5.4 数据导入页面
- [x] 5.4.1 文件上传组件
- [x] 5.4.2 表格选择组件
- [x] 5.4.3 表格预览组件
- [x] 5.4.4 列映射配置组件
- [x] 5.4.5 客户/制程选择组件
- [x] 5.4.6 导入确认和进度显示

### 5.5 智能填充页面
- [x] 5.5.1 目标文件上传
- [x] 5.5.2 匹配方式选择
- [x] 5.5.3 匹配预览表格
- [x] 5.5.4 得分详情弹窗
- [x] 5.5.5 确认填充和下载

### 5.6 配置管理页面
- [x] 5.6.1 AI服务配置页面
- [x] 5.6.2 连接测试功能
- [x] 5.6.3 文本处理配置页面
- [x] 5.6.4 Prompt模板编辑器

### 5.7 其他页面
- [x] 5.7.1 仪表盘（统计概览）
- [x] 5.7.2 同义词管理页面
- [x] 5.7.3 关键字管理页面
- [x] 5.7.4 操作历史页面

## 6. 集成测试

- [x] 6.1 API单元测试
- [x] 6.2 API集成测试
- [x] 6.3 前后端联调测试
- [x] 6.4 端到端测试

## 7. 部署配置

- [x] 7.1 后端Dockerfile
- [x] 7.2 前端Dockerfile
- [x] 7.3 docker-compose.yml
- [x] 7.4 Nginx配置
- [x] 7.5 部署文档

## 依赖关系

```
1.项目重构初始化
    │
    ├──► 2.数据层MySQL适配
    │           │
    │           ▼
    │    3.API层实现 ◄────────┐
    │           │              │
    │           ▼              │
    │    4.业务层补充 ─────────┘
    │           │
    │           ▼
    └──► 5.前端开发（可与3-4并行）
                │
                ▼
         6.集成测试
                │
                ▼
         7.部署配置
```

## 可并行任务

- 任务3（API层）和 任务5.1-5.2（前端基础）可并行
- 任务4（业务层）和 任务5.3-5.7（前端页面）可并行
- 多个Controller可并行开发
