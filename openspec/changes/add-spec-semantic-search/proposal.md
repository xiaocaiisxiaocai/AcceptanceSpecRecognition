# Change: 新增验收规格 AI 语义搜索

## Why
当前“验收规格”页面只有关键词检索与分组浏览，用户很难快速找出“语义相近但表述不同”的历史规格，尤其在跨客户、跨机型、跨制程复用验规时效率很低。  
同时，用户提出需要支持“批量搜索多条输入，并对每条搜索结果中的具体规格逐条修改”，这已经超出当前关键词筛选能力，需要新增独立的语义搜索流程。

## What Changes
- 在“验收规格”页面新增 `AI搜索` 入口，支持多行输入批量语义搜索。
- 新增验收规格语义搜索 API，按当前客户/机型/制程分组及 RBAC 数据范围返回结果。
- 复用现有 Embedding 与 `EmbeddingCache` 能力，对每条输入返回 TopN 语义相近规格及相似度分数。
- 搜索结果按“输入项”分组展示，支持对结果中的任意一条规格直接进入编辑并保存。
- 新增对应页面级/按钮级/API 权限定义，保持 RBAC 一致。

## Assumptions
- 批量搜索输入形式采用“多行文本输入”，每行视为一条独立查询。
- 第一版默认将查询文本与规格的组合文本 `项目 + 规格 + 验收标准 + 备注` 进行语义检索。
- 第一版只支持“逐条编辑搜索结果”，不支持自动批量改写多条规格。
- 第一版搜索范围默认继承当前分组上下文与数据权限，不提供跨页全库无范围搜索。

## Impact
- Affected specs: `user-interface`, `api`, `matching-engine`
- Affected code:
  - `web/src/views/base-data/specs/components/SpecTable.vue`
  - `web/src/api/spec.ts`
  - `src/AcceptanceSpecSystem.Api/Controllers/SpecsController.cs`
  - `src/AcceptanceSpecSystem.Core/Matching/*`
  - RBAC 权限种子与前端权限判断相关文件
