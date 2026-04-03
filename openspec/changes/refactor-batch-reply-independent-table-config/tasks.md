## 1. 规格与计划
- [x] 1.1 更新 `refactor-batch-reply-independent-table-config` 变更提案，明确采用“步骤 Tab -> 文件 Tab -> Sheet/表格 Tab”结构
- [x] 1.2 更新用户界面规格，明确移除独立预检查区并将预览收敛到当前 Sheet/表格上下文
- [x] 1.3 编写本轮实现计划，覆盖测试、页面重构和验证命令

## 2. 测试先行
- [x] 2.1 先补前端/回归测试，锁定步骤 Tab、文件 Tab、Sheet/表格 Tab 文案和层级
- [x] 2.2 先补回归测试，明确页面中不再出现独立“预检查/当前表回写预览”区域
- [x] 2.3 运行新增测试并确认它们先失败，证明当前实现仍停留在旧结构

## 3. 页面重构
- [x] 3.1 重构 `web/src/views/batch-reply/index.vue` 的顶层步骤 Tab，拆分为“来源文件 / 目标文件 / 执行结果”
- [x] 3.2 将来源文件步骤改为“文件 Tab -> Sheet/表格 Tab”，让行设置和项目/规格/验收/备注列配置直接落在对应 Sheet/表格内
- [x] 3.3 将目标文件步骤改为“目标文件 Tab -> Sheet/表格 Tab”，让来源表选择与行列配置直接落在对应 Sheet/表格内
- [x] 3.4 移除独立预检查/预览结果卡片，把预览按钮与反馈收拢到当前 Sheet/表格上下文
- [x] 3.5 保持现有执行契约，只按当前配置是否完整决定文件是否可执行

## 4. 验证
- [x] 4.1 运行 `openspec validate refactor-batch-reply-independent-table-config --strict`
- [x] 4.2 运行前端相关测试，确认新结构和旧文案移除都通过
- [x] 4.3 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`
- [x] 4.4 运行 `pnpm --dir web build`
