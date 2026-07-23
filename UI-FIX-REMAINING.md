# UI/UX 修复进度 - 剩余待办

## 已完成 ✅
- **Task#1 令牌漏网清理** - 全部硬编码色值已替换为语义令牌（data-import/smart-fill/prompt-templates/BatchTableConfig/MatchConfig/ScoreDetail系列/Spec弹窗/AI服务配置）
- **Task#3 登录页品牌色统一** - 主色切换为 SAA Logo 蓝，完成登录页蓝阶和暗色主题适配
- **Task#4 config 头部统一** - 统一标题、工具栏及角色/用户/权限等列表页全高表格骨架
- **Task#5 孪生向导页一致** - 固定底栏、唯一主按钮、上传区表单纵向堆叠
- **Task#6 规格表格列截断** - 100 条/页，客户/项目/规格列实测为 160/180/260px
- **Task#7 角色权限选择器** - 创建/编辑复用 RoleFormDialog，权限改为四组树形选择
- **Task#8 仪表盘可视化** - 匹配度/采用率环图，导入量/任务数最近 7 天趋势图
- **Task#2 无障碍交互缺口** - 可点击 div 支持键盘，密码开关具有 aria-label，业务弹窗、配置表单与向导必填项均启用字段级内联校验

## 本轮实施记录 📋

### P1 高价值改动

**Task#8 仪表盘可视化 ✅** (P1-item1)
- 文件：`src/views/dashboard/index.vue:234-287`
- 改动：
  1. 匹配度/采用率卡：34px 大数字改 `<el-progress type="circle" :percentage="xx" />`
  2. 导入量/任务数卡：加 7 日 sparkline（用现有 ECharts）
  3. 收敛四卡下方留白
- 难度：中（需写 ECharts mini 图表）

**Task#7 角色权限选择器 ✅** (P1-item2)
- 文件：`src/views/config/auth-roles/index.vue:692-955`
- 改动：
  1. 扁平多选框改为 `<el-tree :show-checkbox="true" node-key="id">`，按类型分组（菜单/页面/按钮/接口四组）
  2. 批量按钮收敛为"全选/清空/展开/折叠"四个（纯树操作，不再分类型）
  3. 抽 `RoleFormDialog.vue` 组件，消除创建/编辑弹窗 360 行复制
- 难度：高（需重构权限数据结构为树）

**Task#5 孪生向导页一致 ✅** (P1-item4)
- 文件对：
  - `src/views/data-import/index.vue` + `index.styles.css`
  - `src/views/smart-fill/index.vue` + `index.styles.css`
- 改动：
  1. **底部操作栏统一**：都用 `position: fixed`（data-import/index.styles.css:591 样板）
  2. **收敛双主按钮**：删除页内"识别"按钮，只保留底栏唯一主按钮
  3. **上传区表单统一**：都用纵向堆叠（三下拉 `flex-direction: column`）
- 难度：中（需小心智能识别按钮的事件绑定）

**Task#4 config 头部统一 ✅** (P1-item5)
- 文件清单（9个config子页 + rbac/permissions）：
  - `src/views/config/database-backup/index.vue` - page-title 22px/650 覆写
  - `src/views/config/embedding-warmup/index.vue` - page-title 22px/650 覆写
  - `src/views/config/org-units/index.vue` - 标题塞卡内
  - `src/views/rbac/permissions/index.vue` - 完全无 H1
  - `src/views/rbac/auth-roles/index.vue` - 表格未用全高骨架
  - `src/views/rbac/system-users/index.vue` - 表格未用全高骨架
  - （其余 config 子页类似问题）
- 改动：
  1. 统一 page-title：20px/600，放 `.page-header`
  2. 工具栏统一用 `.list-card-toolbar`（而非裸 flex）
  3. 表格全高骨架：包裹 `.full-height-table-wrapper`（参考 specs/index.vue:179-189）
- 样板：`src/views/base-data/specs/index.vue`、`src/views/config/audit-logs/index.vue`
- 难度：中（批量结构对齐，需逐页测试）

### P2 打磨

**Task#2 无障碍缺口 ✅** (P2-item7)
- 改动清单：
  1. 可点击 div 补 `role="button" tabindex="0" @keydown.enter/space`
     - `src/views/file-compare/components/UnifiedDiffView.vue` - diff 展开控件
     - `src/views/smart-fill/components/MatchConfig.vue:751` - 折叠区
     - （全局搜：`@click` 且非 button/a 标签的交互元素）
  2. 登录页图标开关：`src/views/login/index.vue` - 密码可见性切换加 `aria-label="切换密码可见性"`
  3. 弹窗表单内联校验：按真实提交约束补 `formRules` 与字段 `prop`，不再仅靠 toast；筛选/只读表单不挂空规则
- 难度：低（机械添加属性）

**Task#3 登录页品牌色统一 ✅** (P2-item8)
- 决策：改回蓝色主色（SAA logo 深蓝为准）
- 改动：
  1. `src/style/tokens.scss:11` - `--app-primary: #064790` (SAA logo 蓝，从 public/logo.svg 提取)
  2. `src/style/login.css:4-7` - light-3/5/7/9 派生色改为蓝阶
  3. 验证暗黑模式、全站紫色点缀→蓝色
- 难度：**高风险**（全站视觉变动，需完整回归）
- **注意**：这是最后一步，改完后需跑全部测试 + 视觉回归

## 改动后验证检查单
- [x] `pnpm test:vitest` - 96 通过
- [x] `pnpm test:node` - 247 通过
- [x] `pnpm typecheck` - 0 错误
- [x] `pnpm build` - 构建通过
- [x] 启动 `pnpm dev`，逐页视觉验证：
  - [x] 登录页（主色蓝）
  - [x] 仪表盘（环形进度 + sparkline）
  - [x] 数据导入（底栏固定 + 单主按钮）
  - [x] 智能填充（同上）
  - [x] 验收规格（列宽舒展 + 100条/页）
  - [x] 角色管理（树形权限选择器）
  - [x] 9个config页及权限字典（头部统一 + 全高表格，无横向溢出）
- [x] 暗黑模式切换（蓝色主色适配）
- [x] 窄屏 390px（无横向溢出）

## 技术债与风险
1. **主色改蓝影响面**：全站 `--app-primary` 引用 279 处，`--app-primary-light` 背景块会从紫→蓝
2. **角色权限树**：需改后端返回数据结构或前端重组（若后端返扁平数组需前端 groupBy）
3. **孪生页双按钮**：需确认"页内识别按钮"是否有独立业务逻辑（会话恢复？），不能简单删
4. **config 页全高骨架**：需检查每页的 DOM 结构是否支持 flex-direction:column + flex:1

## 文件备份
- 截图已留存：`audit-01.png` ~ `audit-12.png`（可对比改前改后）
- Playwright 快照：`.playwright-mcp/*.yml`（18个，可删）
