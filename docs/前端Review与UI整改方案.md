# 前端全面 Review 与 UI 整改方案

**文档版本**：v1.0
**日期**：2026-07-04
**审查范围**：`web/` 前端全部 283 个源码文件（views 全量 + layout + style + components）
**审查方法**：布局框架与全局样式逐行精读 + 全部业务视图深度审查 + 量化统计（硬编码色值 / 表格组件使用 / 分页与写死高度分布）

---

## 0. 结论摘要（TL;DR）

用户反馈的两个问题，根因都不在 pure-admin 框架层，而在**主题失控**与**页面内部空间管理失控**：

| 反馈 | 根因 | 一句话方案 |
|------|------|-----------|
| **UI 太丑** | ① 全局被套上一层"活动页风格"的紫色主题（正文字色竟是深紫 `#4C1D95`、页面背景淡紫、表头淡紫），来源是自动生成的 `design-system/MASTER.md`（原本是为 Event 活动主题生成的模板）；② 各页面又各自硬编码了 **448 处**十六进制色值，Tailwind 灰系 / slate / indigo / emerald / GitHub diff 色 / Element 旧默认色**至少 6 套色板并存** | 建立设计令牌（Design Token）系统，全局中性化 + 紫色降级为点缀色，448 处硬编码分批归一 |
| **内容显示区域太少** | ① 核心表格写死 `max-height="400/500"` 却每页显示 100 行；② 向导页"页头标题 + 步骤条卡 + 面板内 h3 标题"三重堆叠吃掉首屏约 190–200px；③ 简单 CRUD 页"搜索独占一卡 + 双层卡嵌套 + 表格自然高 + pageSize 20"；④ 全局控件尺寸被放大（`--el-component-size: 36px`） | 推广"全高表格骨架"（specs/audit-logs 页已有最佳实践），向导页头部合并，全局密度收紧 |

整改分 4 个阶段，**阶段 1（1–2 天）即可让全站观感发生最大变化**（令牌落地 + 中性化 + 密度收紧），阶段 2 解放内容区，阶段 3 清理技术债，阶段 4 可选增强。全程**不换组件库、不重写既有「壳 + 区块组件 + composable」拆分架构**；方案中确有删除纯展示节点、合并向导头、减少嵌套等 DOM 调整，此类改动以 `FrontendViewBoundaryRefactorTests` 与前端单测为硬约束——改前先全局搜索测试对相应选择器/文案的引用，改后回归验证。**实施第一步是创建 OpenSpec 变更提案**（仓库已有 `user-interface` 规格 19 条 requirements 与 `table-preview` 规格，UI 改版须对照产出规格 delta，见阶段 0）。

---

## 1. 现状盘点

### 1.1 技术底座

- vue-pure-admin（Pure Admin Thin）+ Element Plus 2.11 + Tailwind CSS v4 + Vite 7
- 布局：vertical（侧栏 210px，折叠 54px）+ 顶栏 48px + 页签栏约 33px + 可选 footer
- `platform-config.json`：`Stretch: false`（内容全宽 ✅）、`HideFooter: false`、`EpThemeColor: #7C3AED`
- 密度守护测试已存在：`web/tests/layout-density.test.ts`（锁定 `.page` gap:12px/padding:0、禁止 24px 大留白）

### 1.2 框架层空间账本（1080p 视口约 937px 高）

| 项 | 占用 | 评价 |
|----|------|------|
| 顶栏 navbar | 48px | 正常 |
| 页签栏 tags-view | ~33px | 正常（可配置隐藏） |
| `.main-content` margin | 上下各 16px | 可收至 12px |
| footer | ~35px | 版权信息，建议默认隐藏 |
| **框架层合计** | **~148px（≈16%）** | **正常水平，不是主要矛盾** |

真正的空间浪费在页面内部（见第 2 章问题清单 A 组）。

### 1.3 全局主题现状（"丑"的第一根因）

`web/src/style/index.scss:8-65` 将 `design-system/acceptance-specification-system/MASTER.md`（自动生成，Category: Analytics Dashboard，"Event theme colors + Excitement accents"）的紫色模板直接接入了 Element Plus 变量：

```
--color-primary: #7c3aed        主色紫（尚可接受）
--el-text-color-primary: #4c1d95  ← 全站正文变深紫（最刺眼）
--color-background: #faf5ff      ← 页面背景淡紫（reset.scss body 引用）
--el-table-header-bg-color: #f3e8ff  ← 表头淡紫
--el-table-border-color: #ede7f6 / 按钮描边 #e4d7fb / 卡头 #f8f5ff / 斑马纹 #faf7ff
--el-component-size: 36px        ← 全局控件放大（默认 32px），密度反向恶化
```

同时存在**字体栈双定义冲突**：`reset.scss:19` body 用 Inter 优先，`fonts.scss` 又对 html/body 定义 `--app-font-sans`（Segoe UI 优先），两处互相覆盖。

### 1.4 量化指标

| 指标 | 数值 | 说明 |
|------|------|------|
| views 内硬编码十六进制色 | **448 处** | Top：`#6b7280`×58、`#111827`×24、`#e5e7eb`×22、`#f8fafc`×20 —— 全是 Tailwind gray/slate 系，与全局紫主题脱节 |
| views 散装 CSS | `<style>` 块 ~3580 行 + 独立 `*.styles.css` 1434 行 ≈ **5000 行** | 无令牌，字号 11–18px、圆角 6/8/10/12/14/16/20/999px 混用 |
| 使用裸 `el-table` 的视图 | 24 个 | `pure-table` 0 个、`PureTableBar`（密度/列设置工具栏）仅 2 处 —— 框架自带能力闲置 |
| 表格写死 max-height | 280/300/360/400/420/460/500/560/580/620 共 12+ 处 | 不随视口伸展 |
| 分页默认值 | 20（10 处）/ 100 / 200 / 500 / 1000 并存 | 无统一标准 |

---

## 2. 问题清单（按严重度）

### A 组：内容显示区域太少

| # | 严重度 | 问题 | 证据位置 |
|---|--------|------|----------|
| A1 | 🔴 P0 | **核心预览表写死 500px 高 + 每页 100 行**：表头 ~55px + 行 ~48px，仅可见 8–9 行，用户在小窗里滚 100 行；表格上方还压着 el-tabs(border-card) + 统计标签行 + 筛选栏 | `smart-fill/components/MatchPreviewDataTable.vue:58`、`MatchPreviewTable.vue:79-80` |
| A2 | 🔴 P0 | **向导页三重标题堆叠**：页头标题 → 步骤条独占一张 el-card → 面板内又渲染 `h3.step-title + p.step-desc`，首屏 190–200px 被"导航性文案"吃掉；`step-content` 还写死 `min-height: 420/500px` 空撑 | `smart-fill/index.styles.css:13,157-164`、`SmartFillSteps.vue:12`、`data-import/index.styles.css:26,30-40` |
| A3 | 🔴 P0 | **简单 CRUD 页系统性压缩**：搜索仅 1 输入框 + 2 按钮却独占整卡（≈80px）；双层卡嵌套；`.page gap:12px` 与 `mb-4:16px` 叠加成 28px 双重间距；表格自然高 + pageSize 20 | `base-data/customers/index.vue:215-311`（processes、machine-models 近乎逐字节复制） |
| A4 | 🟠 P1 | **全局控件放大**：`--el-component-size: 36px`（默认 32px），所有输入框/按钮/下拉全线增高 12.5% | `style/index.scss:54` |
| A5 | 🟠 P1 | **数据导入主预览表 `max-height="400"`**，1080p 下浪费 300px+；分页固定 20 无更大选项 | `data-import/components/TablePreview.vue:259,283` |
| A6 | 🔴 P0 | **batch-reply 首屏约 300px 被非内容吃掉**：自定义大 header 卡（eyebrow + 30px h1 + 3 个 min-height:90px 统计卡，padding 22px 24px）+ rule-strip 规则条（~60px）+ workflow-panel 内 4 层容器嵌套（panel→tabs→card→tabs） | `batch-reply/index.vue:296-330`、`index.styles.css:12-103` |
| A7 | 🟠 P1 | **说明横幅泛滥**：每个 step-panel 顶部 title+desc 重复 5+ 处；智能确认步 desc + SummaryBanner + ConfirmPanel 内 4 处 el-alert 纵向排队；MatchConfig 每项下挂灰色 tip 单列长滚动 | `DataImportConfirmPanel.vue:185-460`、`MatchConfig.vue:436-928` |
| A8 | 🟠 P1 | **写死 max-width 居中留白**：import-confirm 1180px、import-result 1200px、import-progress-panel 720px、target-form 500px——大屏（2K/带鱼屏）两侧大片空白；差异单元格 min-height:88px 纵向巨占 | `data-import/index.styles.css:128,386,352,118,597` |
| A9 | 🟡 P2 | footer 常驻 ~35px、`.main-content` margin 16px 可再收紧 | `platform-config.json`、`lay-content/index.vue:236` |

### B 组：UI 太丑

| # | 严重度 | 问题 | 证据位置 |
|---|--------|------|----------|
| B1 | 🔴 P0 | **紫色主题错配**：深紫正文 `#4c1d95`、淡紫背景/表头/边框全家桶（见 1.3），"活动页模板"套在企业数据工具上 | `style/index.scss:8-65`、`reset.scss:23` |
| B2 | 🔴 P0 | **多套色板并存、无令牌**：**batch-reply 整页是一套独立蓝色 VI**（eyebrow `#2158a8`、active-bar `#2f6bb2`、标题 `#173d73` 系），与全局紫、与 data-import 全冲突；置信度标签用 Element 旧默认绿橙红加 `!important` 锁死对抗紫主题；file-compare 一页内三套 diff 配色；智能识别同一功能 indigo 横幅 + 蓝灰卡片 + 蓝色 tip 三套皮肤；FileUpload 组件内联 SVG `#409EFF`(Element 蓝) 配紫色描边 `#e4d7fb` 蓝紫打架 | `batch-reply/index.styles.css:16-129`、`MatchPreviewTable.styles.css:62-80`、`UnifiedDiffView.vue:760-815` vs `CompareTableGrid.vue:272-286` vs `file-compare/index.vue:830-853`、`SmartStructureSummaryBanner.vue:247` vs `SmartStructureConfirmCard.vue:407-434`、`FileUpload.vue:117,164` |
| B3 | 🔴 P0 | **首页 dashboard 空洞且配色跑偏**：仅一行 4 个 KPI 卡、无图表、下方大片空白；绿/蓝渐变卡与紫主题冲撞——登录后第一印象即"丑 + 空" | `dashboard/index.vue:203-256,305-311` |
| B4 | 🟠 P1 | **字号/圆角/阴影失控**：字号 11–18px 混用；圆角 6/8/10/12/14/16/20/999px 八档并存；数据卡片 hover 抬升阴影（对数据页是噪音） | 全站散装 CSS；`element-plus.scss:29-31` |
| B5 | 🟠 P1 | **同一动作三种外观**：上传区三套样式（FileUpload / SourceUploadPanel / TargetFilesPanel，图标尺寸 60/56/52、描边紫/灰蓝各异、文案不一）；结果页两套（el-result+自定义 stats vs el-alert+el-table）；label-width 五花八门（96/100/110/120/132px）；页壳两套（`.page` vs file-compare 的 `.compare-page`）；file-compare 用原生 `<table>` 而非 el-table | `FileUpload.vue:157`、`SourceUploadPanel.vue:81-90`、`TargetFilesPanel.vue:100-113`、`file-compare/index.vue:444-680` |
| B6 | 🟡 P2 | **规范执行不齐**：`docs/ui-guidelines.md` 的 config-page 类与下拉档位体系，auth-roles、rbac/permissions 缺失；system-users/permissions 用裸 `w-[180px]`；非配置页误用 `config-select-popper`；档位值发散（200/240/300） | `auth-roles/index.vue:538,719`、`rbac/permissions/index.vue:77,84`、`system-users/index.vue:447` |
| B7 | 🟡 P2 | 字体栈双定义冲突（Inter vs Segoe UI）；`page-title` 全局 20px 却被 dashboard 等页各自重定义为 22px | `reset.scss:19-21` vs `fonts.scss`、`dashboard/index.vue:268` |
| B8 | 🟡 P2 | welcome 页仅一行灰字"正在跳转…"；弹窗宽度无规格（520/640/1080/1100/1200px 并存） | `welcome/index.vue:17`、各弹窗组件 |

### 值得保留的亮点（整改时不要破坏）

- **specs 页的"锁外层滚动 + 动态视口高 + 左树右表 split 布局 + 表格 height=100%"** —— 全站空间利用最佳范式，本方案将其推广为通用骨架
- audit-logs 的全高表格 + 唯一严格遵守下拉档位规范
- UnifiedDiffView 的左右并排 diff + hunk 折叠 + 字符级高亮；CompareTableGrid 的 sticky 表头 + 双栏滚动同步
- SmartStructureConfirmCard 的字段级三态裁决交互（只需换皮肤）
- MatchConfig 的高级区折叠渐进披露；batch-reply 的 summary-line 一行汇总模式（值得全站推广）
- data-import 的 el-affix 吸顶步骤条 + 固定底部操作栏（导航定位清晰，向导页骨架改造可直接沿用）；ConfirmPanel 用 el-collapse 折叠 + 单行概览 grid，密度优；TableSelector 自带紧凑模式开关 + 搜索 + 全选
- 各页已按"壳 + 区块组件 + composable"拆分，重构地基好

---

## 3. 整改方案

### 3.1 设计方向决策（✅ 已确认：方案 A，2026-07-04）

**主色取舍** —— 两个选项：

- **方案 A（✅ 已选定）：保留紫 `#7C3AED` 作为品牌主色，但降级为"点缀色"**。紫只出现在：按钮/链接/选中态/进度条/菜单激活线。文字、背景、边框、表头全部中性化。理由：改动面最小（不动 `EpThemeColor`）、保留辨识度、且 448 处硬编码的 Top 色值本来就是中性灰——直接把它们"转正"为令牌，大半是等值替换，迁移成本最低。
- ~~方案 B：整体换企业蓝（如 `#2563EB`）~~（已否决：改动面大且失去现有辨识度）。

以下按 **方案 A** 展开。

### 3.2 第一层：设计令牌系统（新建 `web/src/style/tokens.scss`）

```scss
:root {
  /* ===== 色彩：中性基底（取自现有 448 处硬编码的最高频值，等值转正） ===== */
  --app-bg-page: #f5f6f8;            /* 页面背景，替换淡紫 #faf5ff */
  --app-bg-card: #ffffff;
  --app-text-primary: #111827;       /* 正文，替换深紫 #4c1d95 */
  --app-text-secondary: #6b7280;     /* 次要文字（现最高频灰 ×58） */
  --app-text-disabled: #9ca3af;
  --app-border: #e5e7eb;
  --app-border-light: #f3f4f6;
  --app-fill-hover: #f5f7fa;

  /* ===== 品牌与语义色（全站唯一来源） ===== */
  --app-primary: #7c3aed;            /* 紫：仅用于交互元素 */
  --app-primary-light: #f3effd;      /* 选中底/hover 底 */
  --app-success: #16a34a;  --app-success-bg: #f0fdf4;
  --app-warning: #d97706;  --app-warning-bg: #fffbeb;
  --app-danger:  #dc2626;  --app-danger-bg:  #fef2f2;
  --app-info:    #6b7280;  --app-info-bg:    #f9fafb;

  /* ===== 业务决策语义色（匹配决策/识别裁决全站统一） ===== */
  --app-decision-auto: var(--app-success);      /* AutoApply / 自动采用 */
  --app-decision-review: var(--app-warning);    /* ManualReview / 需确认 */
  --app-decision-reject: var(--app-danger);     /* 拒绝 / 失败 */
  --app-decision-ai: var(--app-primary);        /* AI 裁决参与标识 */

  /* ===== diff 配色（统一采用 GitHub 系，UnifiedDiffView 已在用） ===== */
  --app-diff-add-bg: #e6ffec;  --app-diff-add-emphasis: #acf2bd;  --app-diff-add-text: #22863a;
  --app-diff-del-bg: #ffeef0;  --app-diff-del-emphasis: #fdb8c0;  --app-diff-del-text: #cb2431;

  /* ===== 字号（五档封顶） ===== */
  --app-font-xs: 12px;   /* 辅助/标签 */
  --app-font-sm: 13px;   /* 表格 */
  --app-font-md: 14px;   /* 正文/表单 */
  --app-font-lg: 16px;   /* 区块标题 */
  --app-font-xl: 20px;   /* 页标题（全站唯一，禁止各页重定义 22px） */

  /* ===== 圆角（三档封顶） ===== */
  --app-radius-sm: 6px;    /* 控件/按钮/输入框 */
  --app-radius-md: 10px;   /* 卡片/面板 */
  --app-radius-lg: 12px;   /* 弹窗 */
  /* tag 保留 999px pill */

  /* ===== 阴影（静态两档，数据卡片禁用 hover 抬升） ===== */
  --app-shadow-card: 0 1px 2px rgb(0 0 0 / 5%);
  --app-shadow-overlay: 0 10px 15px rgb(0 0 0 / 10%);
}
```

**Element Plus 变量重映射**（改 `index.scss`，删除紫色全家桶）：

```scss
--el-color-primary: var(--app-primary);
--el-text-color-primary: var(--app-text-primary);   /* 深紫 → 中性 */
--el-color-success: var(--app-success);             /* 统一语义色，去掉标签 !important 对抗 */
--el-color-warning: var(--app-warning);
--el-color-danger: var(--app-danger);
--el-table-header-bg-color: #f9fafb;                /* 淡紫 → 中性 */
--el-table-border-color: var(--app-border);
--el-table-row-hover-bg-color: var(--app-fill-hover);
--el-card-border-color: var(--app-border);
--el-border-radius-base: var(--app-radius-sm);
--el-component-size: 32px;                          /* 36 → 32，恢复默认密度 */
```

同步项：`reset.scss` body 背景与字体栈合并（保留 `fonts.scss` 的 `--app-font-sans`，删除 reset 中的 Inter 定义）；`dark.scss` 按同一令牌语义补暗色值；`element-plus.scss` 删除 `.el-card:hover` 阴影抬升。

### 3.3 第二层：布局与密度（全局改动清单）

| # | 改动 | 文件 | 预期收益 |
|---|------|------|----------|
| L1 | `--el-component-size: 36px → 32px` | `style/index.scss:54` | 每个表单行/工具栏省 4–8px，全站累计显著 |
| L2 | `.main-content margin: 16px → 12px` | `layout/components/lay-content/index.vue:236` | 上下左右各 +4px |
| L3 | `HideFooter: true`（版权信息移入"关于"） | `public/platform-config.json` | +35px |
| L4 | `.el-card__body padding: 16px → 12px` | `style/element-plus.scss:41` | 每层卡省 8px，双层卡省 16px |
| L5 | `.el-table .el-table__cell padding: 8px 0 → 6px 0` | `style/element-plus.scss:59` | 每 10 行省 ~40px，多看 1–2 行 |
| L6 | page-header 紧凑化：标题与操作按钮同行；副标题改为 title 提示或删除 | `style/index.scss:158-175` + 各页 | 每页 +20–30px |
| L7 | 弹窗宽度规格化：S=480 / M=640 / L=960 / XL=1200 四档封顶，并统一改用 `width="min(Npx, calc(100vw - 32px))"` 响应式写法（DataImportDifferenceDialog 已是此写法，DuplicateResolutionDialog 960px 固定则小屏溢出），替换 520/1080/1100 等散值 | 各弹窗组件 | 一致性 + 小屏不溢出 |
| L8 | 分页统一：列表页默认 50，选项 [20,50,100,200]；预览表默认 50；specs 大分页按业务保留 | 各页 script | 与表格可视高匹配 |

### 3.4 第三层：统一骨架与组件（复用现有最佳实践，不新造轮子）

**① 全高表格页骨架（本方案最重要的单项改动）**
把 specs/audit-logs 已验证的模式抽为全局 CSS 类 + 约定：

```
.page--fill {                       /* 页面锁定视口高，内部自己滚 */
  height: calc(100vh - var(--app-chrome-height, 105px));  /* 81px 顶部 + main-content 边距 */
  overflow: hidden;
}
.page--fill .table-region { flex: 1; min-height: 0; }      /* el-table :height="100%" */
```

适用页面：customers/processes/machine-models、system-users、auth-roles、permissions、execution-history、audit-logs（已有）、specs（已有）、column-mapping-rules、prompt-templates 列表区。
**同时删除全部写死的 `max-height="280~620"`**（弹窗内表格改 `max-height="60vh"` 类视口相对值）。

**② 向导页骨架（smart-fill / data-import 双页同改）**
- 步骤条不再独占一张 el-card：页头一行 = 页标题(左) + el-steps simple 模式(右)，合并后头部从 ~190px 压到 ~60px
- 删除面板内 `h3.step-title + p.step-desc`（步骤条已表达"我在哪"），说明文字收进步骤条节点的 tooltip
- 删除 `step-content min-height: 420/500px`；`step-actions` 的 `margin-top:32px + padding-top:16px` 收敛为 12px 并改为 sticky 底部操作条
- batch-reply：大 header 卡压缩为"一行页题 + 内联统计 chips"（30px h1 与 90px 统计卡取消），rule-strip 改为页头旁"?"图标 popover，workflow-panel 4 层嵌套（panel→tabs→card→tabs）减至 2 层，整页独立蓝色 VI 全部改走全局令牌

**③ 统一三件散装组件**
- **AppUploadZone**：合并三套上传区样式为一个带插槽的组件（虚线框 + 图标 + 主/副文案），data-import/batch-reply 两页替换
- **决策标签**：置信度/匹配决策标签统一走 `--app-decision-*` 令牌，删除 `MatchPreviewTable.styles.css:62-80` 的 `!important` 硬编码
- **diff 配色**：CompareTableGrid 与 file-compare 段落卡改用 `--app-diff-*`，与 UnifiedDiffView 归一

**④ 表格工具栏**
简单 CRUD 页取消"搜索独占一卡"：搜索输入 + 筛选下拉 + 主操作按钮合并为表格卡头部的单行 toolbar（specs 页 toolbar 即现成样板）。有余力再接入 `PureTableBar`（获得密度切换/列设置/全屏，组件已在仓库内闲置）。

### 3.5 第四层：页面级整改清单

| 页面 | 问题 | 改法 | 优先级 |
|------|------|------|--------|
| dashboard | 4 个 KPI 卡 + 大片空白；绿/蓝渐变冲撞 | KPI 卡换令牌中性风格；下方补 2 个 ECharts 图表（匹配决策占比环图 + 近 30 天执行趋势折线，数据源 `/api/dashboard` 已有）+ 最近执行记录表 | P1 |
| smart-fill | A1/A2/B2 | 全高预览表 + 向导头合并 + 决策标签令牌化；MatchConfig 改双列布局、tip 收进 label 的 tooltip | P0–P1 |
| batch-reply | A6 独立蓝 VI + 300px 首屏开销 + 4 层嵌套 | header 压缩、rule-strip 改 popover、嵌套减层、全页色值走令牌（本页是色板归一工作量最大的单页） | P0–P1 |
| data-import | A2/A5/A8 + 智能识别三套皮肤 | 向导头合并（保留 affix + 固定底栏模式）+ TablePreview 全高 + max-width 1180/1200 放开为全宽 + 识别横幅/卡片统一令牌 | P0–P1 |
| file-compare | 页壳独立、三套 diff 色、pane 高度写死 62vh/640px | 接入 `.page` 壳 + diff 令牌归一 + pane 高度改 flex 自适应 | P1 |
| customers/processes/machine-models | A3（三页复制） | 换全高骨架 + 单行 toolbar；顺手抽公共 composable 消除三页复制 | P1 |
| specs / audit-logs | 已是最佳实践 | 仅令牌替换，作为骨架样板 | P2 |
| config 各页 + auth-roles/permissions | B6 规范执行不齐 | 补 config-page 类、popper-class、档位类替换裸 w-[180px]；档位收敛为 200/240/300 三档 | P2 |
| execution-history | 灰蓝渐变、max-height 560/620 | 令牌化 + 视口相对高度 | P2 |
| welcome | 仅"正在跳转…" | 登录后直接重定向 dashboard，删除此页（或做成快捷入口页） | P2 |
| login | pure-admin 原版，尚可 | 仅换品牌色令牌与系统名，不动结构 | P3 |

### 3.6 暗黑模式同步

`dark.scss` 已有中性暗色基底（`#0b0c12` 系），与本方案中性化方向一致。需要：语义色/决策色/diff 色补暗色变体（如 diff-add-bg 暗色下用 `rgb(46 160 67 / 15%)`），并在阶段 3 色板归一时同步替换。

---

## 4. 实施路线图

```
阶段 0  立项与基线（0.5–1 天）
  ├─ 主色决策：✅ 已完成（2026-07-04 确认方案 A，见 3.1）
  ├─ 创建 OpenSpec 变更提案：openspec/changes/<change-id>/（proposal.md + tasks.md +
  │   user-interface / table-preview 规格 delta），openspec validate 通过并经批准后方可动码
  └─ 截图基线（关键 8 页 × 亮/暗两态）

阶段 1  P0 全局换肤 + 密度收紧（1–2 天）★ 观感变化最大
  ├─ tokens.scss 落地，index.scss 紫色全家桶替换，reset/fonts 字体栈合并
  ├─ L1–L5 密度五改（component-size / main-content / footer / card padding / cell padding）
  ├─ dark.scss 同步
  └─ 更新 layout-density.test.ts 断言新值 + 前端全量测试/typecheck 通过

阶段 2  P0–P1 内容区解放（2–3 天）
  ├─ 全高表格骨架推广（删 12+ 处写死 max-height）
  ├─ smart-fill / data-import 向导头合并、min-height 移除、分页调整
  ├─ 简单 CRUD 三页换骨架 + 单行 toolbar
  └─ batch-reply alert 收敛 + 减嵌套

阶段 3  P1–P2 色板归一与组件统一（3–5 天，可拆多个 PR 分批）
  ├─ 448 处硬编码色按映射表替换（Top 色值等值转正，边缘色值就近归档）
  ├─ AppUploadZone / 决策标签 / diff 配色三件套统一
  ├─ 弹窗宽度四档规格化、page-header 紧凑化
  └─ 规范补齐（config-page / popper / 档位）+ ui-guidelines.md 更新为令牌版

阶段 4  P2–P3 增强（2–3 天，可选）
  ├─ dashboard 重做（图表 + 最近执行）
  ├─ welcome 处理、PureTableBar 接入、暗黑模式精校
  └─ 截图回归对比、清理 design-system/MASTER.md（标注废弃或重生成）
```

**总工作量估算**：核心（阶段 1–2）约 3–5 人日；完整（含阶段 3–4）约 9–13 人日。🟡 中置信度（70–90%），按 448 处色值替换的自动化程度浮动。

---

## 5. 验收标准与守护机制

1. **密度守护测试升级**：`layout-density.test.ts` 增加断言——`--el-component-size: 32px`、`.main-content` margin ≤ 12px、`TablePreview.vue`/`MatchPreviewDataTable.vue` 不得出现写死的 `max-height="[0-9]{3}"`。
2. **色值准入门禁**：新增 node 测试（或 stylelint `declaration-property-value-disallowed-list`）——`views/**` 禁止新增十六进制色值，只允许 `var(--app-*)` / `var(--el-*)`（存量白名单逐阶段收缩）。
3. **架构边界不破坏**：改动全程保持"壳 + 区块组件 + composable"结构，`FrontendViewBoundaryRefactorTests` 必须持续通过。
4. **功能回归**：`pnpm typecheck` + `pnpm test`（vitest 双轨）全绿；smart-fill 预览 → 执行 → 下载、data-import 五步导入、batch-reply 全流程手工冒烟。
5. **体验验收指标**：
   - 1080p 下 smart-fill 预览表可见行数 ≥ 15 行（现约 8–9 行）
   - 向导页首屏"导航性内容"占高 ≤ 80px（现约 190–200px）
   - 全站正文颜色为中性 `#111827`，无深紫文字
   - 同一语义（成功/警告/危险/diff）全站色值唯一

---

## 6. 风险与注意事项

| 风险 | 缓解 |
|------|------|
| `--el-component-size` 与 cell padding 全局收紧可能引起个别页面挤压 | 阶段 1 后全页面走查一遍，个别页面用局部 `size` 属性豁免 |
| KeepAlive 缓存页在样式热替换下可能残留旧样式 | 验证时强刷；样式改动不涉及组件状态，风险低 |
| 全高骨架改造涉及 flex 链路（`min-height: 0` 缺失会导致不滚动） | 以 specs 页现成实现为唯一样板复制，先改一页验证再批量 |
| 448 处色值替换量大易引入视觉回归 | 按"Top 色值等值转正"策略，大半替换前后渲染值不变；每批 PR 附截图对比 |
| E2E/集成/前端测试可能依赖 DOM 结构或文案 | 向导头合并、删除 step-title、减嵌套**会改变 DOM 层级**——改前全局搜索 `web/tests` 与 `src` 内联用例对相应选择器/文案的引用，逐项确认后再改，改后 `pnpm test` 全绿 |
| 涉及 UI 实质改版（`user-interface` 规格在管） | **实施前置条件**：先创建 OpenSpec 变更提案并通过 `openspec validate` 与批准（见阶段 0），不得跳过提案直接改码 |

---

## 附录 A：色板归一映射表（节选）

| 现存硬编码 | 出现次数 | 归一到 |
|-----------|---------|--------|
| `#6b7280` / `#4b5563` / `#606266` / `#909399` | 58+13+6+8 | `--app-text-secondary` |
| `#111827` / `#1f2937` / `#374151` / `#0f172a` | 24+6+12+n | `--app-text-primary` |
| `#e5e7eb` / `#e2e8f0` | 22+n | `--app-border` |
| `#f8fafc` / `#f9fafb` / `#f3f4f6` / `#f1f5f9` | 20+5+5+n | `--app-info-bg` 或 `--app-fill-hover`（按用途） |
| `#67c23a` `#e6a23c` `#f56c6c`（EP 旧默认 + !important） | 置信度标签 | `--app-decision-auto/review/reject` |
| `#16a34a` `#059669` `#047857`（三种绿） | 分散 | `--app-success` |
| `#dc2626` `#b91c1c` `#cb2431`（三种红） | 分散 | `--app-danger`（diff 场景走 `--app-diff-del-text`） |
| indigo 系 `#eef2ff #c7d2fe #312e81`（识别横幅） | 18 | `--app-primary-light` + `--app-primary` 派生 |
| 蓝系 `#3b82f6 #1d4ed8 #eff6ff #173d73 #2f6bb2` 等 | 分散 | 提示类 → `--app-info-*`；强调类 → `--app-primary` |

## 附录 B：向导页头部合并示意

```
改造前（~190px）                          改造后（~60px）
┌────────────────────────────┐           ┌────────────────────────────┐
│ 智能填充                     │ 24px      │ 智能填充  ①上传─②表格─③配置─④预览 │ 48px
│ 副标题说明文字…              │ 20px      ├────────────────────────────┤
├────────────────────────────┤ gap 12    │ ┌ 业务内容（全高，内部滚动）┐   │
│ ┌ el-card: 步骤条 ────────┐ │ ~70px     │ │  el-table height=100%  │   │
│ └────────────────────────┘ │           │ │  可见行数 8–9 → 15+     │   │
├────────────────────────────┤ gap 12    │ └────────────────────────┘   │
│ ┌ el-card ───────────────┐ │           │ [sticky 操作条: 上一步/下一步] │
│ │ h3 匹配预览             │ │ ~60px     └────────────────────────────┘
│ │ p  确认匹配结果…        │ │
│ │ （业务内容 max-h 500px）│ │
```

---

*实施状态更新（2026-07-04）：主色方案 A 已确认，OpenSpec 变更 `update-ui-density-and-theme` 已创建并获准实施。阶段 1–3 已按任务清单完成；阶段 4 中 dashboard 重做与 welcome 路由收口已额外完成，PureTableBar 接入仍为可选增强。截图基线与前后对比未标记完成：整改前截图缺失，且本机浏览器自动化环境未能补齐完整当前截图。*
