## 1. OpenSpec 与基线
- [x] 1.1 审阅 `docs/前端Review与UI整改方案.md`、`openspec/specs/user-interface/spec.md`、`openspec/specs/table-preview/spec.md`。
- [ ] 1.2 采集关键页面截图基线：dashboard、smart-fill、data-import、batch-reply、file-compare、customers、specs、audit-logs，覆盖亮色与暗色。（未补勾：整改前基线截图在本轮实施前未采集；后续尝试 Playwright、本地 MCP 浏览器与 Edge CDP 自动采集当前截图时，受本机 Playwright 运行件缺失、浏览器插件元数据缺失、Edge headless 无法访问 Vite 本地页面影响，未能补齐完整 8 页亮/暗截图基线。）
- [x] 1.3 确认 `openspec validate update-ui-density-and-theme --strict` 通过，并取得实施批准。

## 2. 全局主题令牌与密度
- [x] 2.1 新建 `web/src/style/tokens.scss`，定义中性色、品牌色、语义色、决策色、diff 色、字号、圆角和阴影令牌。
- [x] 2.2 修改 `web/src/style/index.scss`，将 Element Plus 变量映射到令牌，移除紫色正文、淡紫背景、淡紫表头与 36px 全局控件尺寸。
- [x] 2.3 修改 `web/src/style/reset.scss` 与 `web/src/style/fonts.scss`，统一字体栈与页面背景来源。
- [x] 2.4 修改 `web/src/style/dark.scss`，补齐暗色模式下的语义色、决策色与 diff 色。
- [x] 2.5 修改 `web/src/style/element-plus.scss` 与布局样式，收紧卡片、表格、主内容边距，并移除数据卡片 hover 抬升。
- [x] 2.6 修改 `web/public/platform-config.json`，默认隐藏 footer 或将等效空间策略写入配置。
- [x] 2.7 更新 `web/tests/layout-density.test.ts`，断言控件尺寸、主内容边距、卡片内边距和表格固定高度门禁。

## 3. 内容区与表格骨架
- [x] 3.1 从 specs/audit-logs 现有模式抽取全高页面与表格区域 CSS 约定。
- [x] 3.2 改造 customers、processes、machine-models 等简单 CRUD 页，搜索与主操作合并到单行工具栏，表格区域使用全高骨架。
- [x] 3.3 改造 smart-fill 预览表，移除固定 500px 高度，使 1080p 下预览表可见行数达到验收标准。
- [x] 3.4 改造 data-import 表格预览，移除固定 400px 高度，分页默认值与可见高度匹配。
- [x] 3.5 改造 execution-history、file-compare 等固定高度表格或面板，改为视口相对高度或 flex 自适应。
- [x] 3.6 统一列表页分页默认值和选项：常规列表默认 50，选项收敛为 `[20, 50, 100, 200]`；specs 等大分页页面按业务保留。

## 4. 向导页与散装组件统一
- [x] 4.1 改造 smart-fill 向导头，合并页标题与步骤条，删除重复展示的 `step-title` 和长说明文案。
- [x] 4.2 改造 data-import 向导头，保留清晰导航与底部操作条，同时减少顶部非内容占高。
- [x] 4.3 改造 batch-reply header、rule-strip 与 workflow-panel，压缩首屏非内容区域并减少嵌套层级。
- [x] 4.4 抽取或统一上传区视觉，覆盖 FileUpload、SourceUploadPanel、TargetFilesPanel 等入口。
- [x] 4.5 统一匹配决策、置信度、AI 裁决标签样式，删除对 Element Plus 旧默认色的 `!important` 对抗。
- [x] 4.6 统一 UnifiedDiffView、CompareTableGrid、file-compare 的 diff 配色令牌。
- [x] 4.7 规格化弹窗宽度为 S/M/L/XL 四档，并使用响应式 `min()` 写法避免小屏溢出。
- [x] 4.8 紧凑化 page-header：标题与主操作同排，副标题改为 tooltip、折叠说明或删除。
- [x] 4.9 补齐配置页规范：`config-page`、`popper-class`、下拉档位类和裸 `w-[180px]` 替换，档位收敛为 200/240/300。
- [x] 4.10 分批替换 `web/src/views/**` 存量硬编码色值，Top 色值按等值令牌转正，边缘色值按用途归入文字、边框、填充、语义色或 diff 令牌。（已追加补强：data-import 差异确认区、智能结构确认卡片、welcome 与登录页旧模板色值继续令牌化。）

## 5. 文档、测试与验收
- [x] 5.1 更新 `docs/ui-guidelines.md`，记录令牌、密度、全高表格、弹窗宽度、下拉档位和禁用硬编码色值规则。
- [x] 5.2 新增或更新前端 Node 测试，限制 `views/**` 新增十六进制色值和固定三位数 `max-height`。
- [x] 5.3 更新布局密度测试，使“不得新增大面积装饰性留白”落到可验证断言：控件尺寸 ≤ 32px、主内容边距 ≤ 12px、卡片内边距 ≤ 12px、表格固定高度黑名单。
- [x] 5.4 明确阶段 4 可选项：dashboard 重做、PureTableBar 接入、welcome 处理只在本变更范围允许时执行，不作为阶段 1–3 必达验收项。（已额外完成 dashboard 重做：补充匹配采用分布图、周期业务量图与最近执行记录；已完成 welcome 路由收口：不再暴露 `/welcome` 子路由；已将旧 `design-system/acceptance-specification-system/MASTER.md` 标注为废弃，避免再次接入紫色活动模板。PureTableBar 接入仍按原方案作为可选增强，当前简单 CRUD 已采用全高骨架和单行 toolbar，不作为本轮必达项。）
- [x] 5.5 运行 `cd web; pnpm typecheck`。
- [x] 5.6 运行 `cd web; pnpm test`。
- [x] 5.7 手工冒烟 smart-fill 预览到下载、data-import 五步导入、batch-reply 预览到执行。（已使用真实文件 `C:\Users\SAC\Desktop\泰國投收板機及串線設備驗規-大厚板.xlsx` 完成：data-import 导入成功 20 条；smart-fill 仅精确匹配预览命中 20 行、执行填充 20 行并下载结果；batch-reply 首表预览、重复键按默认“保留首条”处理、执行成功 1 份并下载结果。）
- [ ] 5.8 对比关键页面整改前后截图，确认 1080p 下 smart-fill 预览表可见行数不少于 15 行，向导页导航性内容占高不超过 80px。（未补勾：已通过真实流程截图与冒烟验证确认 smart-fill 当前预览表可见行数满足要求，data-import 与 batch-reply 当前流程也已留存截图；但整改前截图不存在，无法完成“前后对比”。后续自动补采完整当前截图也因 1.2 所述本机浏览器自动化环境问题未完成。）
