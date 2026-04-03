# 批量回复企业工作台视觉优化设计

## 目标
- 将批量回复页面从“带横幅的后台表单页”收敛为“稳重企业工作台”。
- 强化步骤导航、文件导航和 Sheet 工作区的层级关系。
- 保持企业后台气质，避免夸张渐变、强动效和宣传页式视觉。

## 设计要点
- 页头改为克制的工作台标题区，保留一句业务说明和规则提示，但不再抢主体。
- 步骤 Tab 更像流程导航条，而不是默认内容页标签。
- 来源文件和目标文件区域增加工作对象边框与摘要条，明确“当前正在处理哪份文件”。
- Sheet/表格工作区保持白底，但增强边框、分区标题、字段组与状态反馈的秩序感。
- 结果区采用企业汇总面板风格，强调执行统计和文件结果，而不是大面积彩色提示。

## 影响文件
- `web/src/views/batch-reply/index.vue`
- `web/src/views/smart-fill/components/BatchTableConfig.vue`
- `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
