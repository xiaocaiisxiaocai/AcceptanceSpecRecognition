# Matching Knowledge Row Edit Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将匹配知识配置页从默认可编辑改为默认只读，并支持按行进入编辑、完成和取消。

**Architecture:** 保持现有分组录入与保存协议不变，只在前端视图层为每一行增加编辑态和草稿值。新增行默认进入编辑态；既有行默认展示只读文本，避免误触直接改脏；取消时回滚到进入编辑前的原值。

**Tech Stack:** Vue 3、TypeScript、Element Plus、xUnit 字符串回归测试

---

### Task 1: 补充页面回归约束

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试**
- [ ] **Step 2: 运行指定测试并确认失败**
- [ ] **Step 3: 实现最小页面改动**
- [ ] **Step 4: 再跑指定测试并确认通过**

### Task 2: 实现按行编辑

**Files:**
- Modify: `web/src/views/config/matching-knowledge/index.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 为分组行、冲突组行、单位换算行增加编辑态字段与草稿回滚**
- [ ] **Step 2: 将表格默认展示切为只读文本，补上编辑/完成/取消操作**
- [ ] **Step 3: 确保新增行默认进入编辑态，保存仍沿用现有 payload 结构**
- [ ] **Step 4: 运行相关测试和前端构建**
