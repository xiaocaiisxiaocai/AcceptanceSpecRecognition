# Change: 前端主题与密度整改

## Why
当前前端存在紫色主题错配、多套硬编码色板并存、页面内部空间利用不足等问题，导致用户反馈“UI 太丑”和“内容显示区域太少”。这些问题主要集中在全局样式、表格高度、向导页头部和重复页面骨架上，不需要更换组件库或重写业务流程。

## What Changes
- 引入前端设计令牌，统一中性文字、背景、边框、语义色、决策色和 diff 色；保留 `#7C3AED` 作为点缀主色。
- 收紧全局密度，包括 Element Plus 组件尺寸、主内容边距、卡片内边距、表格行高与 footer 默认展示策略。
- 推广全高表格骨架，减少固定 `max-height`，提升 1080p 下核心表格可见行数。
- 合并 smart-fill、data-import 等向导页的导航性头部，减少重复标题、说明横幅和非内容占高。
- 统一上传区、决策标签、diff 配色、弹窗宽度、page-header、配置页下拉档位和简单 CRUD 页工具栏样式。
- 增加测试与门禁，防止重新引入大留白、硬编码色值和固定表格高度。
- 分批迁移 `web/src/views/**` 存量硬编码色值，按设计令牌映射表收敛到 `var(--app-*)` / `var(--el-*)`。
- dashboard 重做与 PureTableBar 接入属于阶段 4 可选增强，不作为本变更的必达验收项。

## Impact
- Affected specs: `user-interface`, `table-preview`
- Affected code: `web/src/style/**`, `web/src/layout/**`, `web/src/views/**`, `web/tests/**`, `docs/ui-guidelines.md`
- 不涉及数据库 schema、后端 API 合约、匹配算法或权限模型。
- 实施期间必须保持现有“壳 + 区块组件 + composable”拆分结构，`FrontendViewBoundaryRefactorTests` 和前端测试需持续通过。
