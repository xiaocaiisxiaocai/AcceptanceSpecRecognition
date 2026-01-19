# Change: 架构升级 - 从桌面应用到Web应用

## Why

原系统设计为WinForms桌面应用，存在以下局限性：
1. 仅支持单机使用，无法多用户协作
2. 部署和更新需要在每台电脑上安装
3. 无法远程访问，必须在本机操作
4. SQLite数据库不适合多用户并发访问

为了满足企业级应用需求，需要将系统升级为前后端分离的Web应用架构。

## What Changes

### **BREAKING** 架构变更
- 前端：WinForms → Vue3 + pure-admin + Element Plus
- 后端：无独立后端 → ASP.NET Core 8 Web API
- 数据库：SQLite → MySQL 8.0
- 部署：单机exe → 前后端分离部署

### 新增功能
- RESTful API接口，支持系统集成
- Swagger API文档
- 支持多用户同时访问
- 现代化Web管理界面

### 保留功能
- Word文档处理（DocumentFormat.OpenXml）
- 智能匹配引擎（相似度、Embedding、LLM）
- AI集成（Semantic Kernel）
- 文本处理（简繁转换、同义词、关键字）

### 移除功能
- WinForms桌面界面
- 离线使用能力（改为内网部署）

## Impact

- **Affected specs**:
  - user-interface（重写为Web界面规格）
  - data-storage（修改为MySQL配置）

- **Affected code**:
  - 删除：`src/AcceptanceSpecSystem/`（WinForms项目）
  - 新增：`src/AcceptanceSpecSystem.Api/`（Web API项目）
  - 新增：`web/acceptance-spec-admin/`（Vue3前端项目）
  - 修改：`src/AcceptanceSpecSystem.Data/`（MySQL适配）

## 技术栈

| 层次 | 技术选型 |
|------|----------|
| 前端框架 | Vue3 + pure-admin |
| UI组件库 | Element Plus |
| 前端语言 | TypeScript |
| 状态管理 | Pinia |
| 构建工具 | Vite |
| CSS框架 | Tailwind CSS |
| 后端框架 | ASP.NET Core 8 Web API |
| API文档 | Swagger/OpenAPI |
| 数据库 | MySQL 8.0 |
| ORM | EF Core 8 + Pomelo.EntityFrameworkCore.MySql |
| Word处理 | DocumentFormat.OpenXml |
| AI框架 | Microsoft Semantic Kernel |

## 用户故事

### US-1: 多用户访问
作为验收工程师，我希望能够通过浏览器访问系统，不需要安装任何软件，可以在任何电脑上工作。

### US-2: 数据共享
作为质量管理人员，我希望团队成员导入的验收数据能够实时共享，避免数据重复录入。

### US-3: 远程管理
作为IT管理员，我希望能够集中部署和管理系统，统一配置AI服务和用户权限。
