## 1. Specification
- [x] 1.1 将 `matching-knowledge` 现行能力描述收敛为“已移除旧接口和旧页面”
- [x] 1.2 清理 proposal / design / tasks 中仍指向 `GET/PUT /api/matching-knowledge` 的旧叙述

## 2. Backend
- [x] 2.1 删除 `MatchingKnowledgeController` 与 `MatchingKnowledgeDraftsController`
- [x] 2.2 删除 matching-knowledge 相关 DTO、服务和运行时外部配置入口
- [x] 2.3 保留匹配知识为匹配引擎内部运行时能力，不再对外暴露读写契约

## 3. Frontend
- [x] 3.1 删除 matching-knowledge 配置页、草稿弹窗和前端 API 封装
- [x] 3.2 删除路由、权限点与导航入口

## 4. Verification
- [x] 4.1 补充后端回归测试，验证旧接口返回 `404`
- [x] 4.2 补充前端回归测试，验证旧页面和旧 API 文件已移除
- [x] 4.3 运行 `openspec validate refactor-matching-knowledge-runtime-source --strict`
