## 1. Specification

- [x] 1.1 等待 OpenSpec 提案批准后再开始实现。

## 2. Core 模型与 Prompt

- [x] 2.1 为 LLM 结构裁决请求增加历史参考案例模型。
- [x] 2.2 扩展结构识别 Prompt 渲染，加入 `referenceCasesJson`。
- [x] 2.3 更新智能结构识别默认 Prompt，要求 LLM 参考历史案例但不得直接复制。
- [x] 2.4 添加 Core 单元测试，验证 Prompt 含历史案例且空案例合法。

## 3. Application 查询与编排

- [x] 3.1 在 `DocumentTemplateAppService` 增加同客户相似模板查询方法。
- [x] 3.2 按表头相似度、使用次数和更新时间选择 Top 3 模板。
- [x] 3.3 在 `SmartConfigurationAppService` 调用 LLM 结构裁决前构造参考案例。
- [x] 3.4 保持模板精确命中、规则高置信和 LLM 预算逻辑不变。

## 4. Tests

- [x] 4.1 添加 Application/API 测试：低置信进入 LLM 时传入同客户历史案例。
- [x] 4.2 添加回归测试：无客户或无模板时传空案例且识别流程不失败。
- [x] 4.3 添加回归测试：模板命中 AutoApply 时不调用 LLM。
- [x] 4.4 运行相关 Core/API 测试。

## 5. Documentation

- [x] 5.1 更新智能结构识别增强分析文档，标记第一阶段落地范围。

