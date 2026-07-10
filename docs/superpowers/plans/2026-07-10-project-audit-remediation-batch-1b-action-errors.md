# Project Audit Remediation Batch 1B Action Error Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除、批删、恢复和重置操作只吞掉用户主动取消，并向用户展示权限、服务端、网络和解析错误。

**Architecture:** 新增只负责识别 Element Plus 确认框取消值的小型 helper，各页面保留现有确认、权限、请求、成功提示和刷新流程。异常分支统一复用 `getRequestErrorMessage`，不抽取通用 `confirmAndRun`。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Axios、Vitest、Node test。

---

## 文件边界

**创建：**

- `web/src/utils/message-box.ts`：确认框主动取消判断。
- `web/src/utils/message-box.test.ts`：取消值和真实错误分类测试。
- `web/tests/destructive-action-errors.test.ts`：审核确认入口的源码守卫。

**修改：**

- `web/src/views/base-data/customers/index.vue`
- `web/src/views/base-data/processes/index.vue`
- `web/src/views/base-data/machine-models/index.vue`
- `web/src/views/base-data/specs/components/SpecTable.vue`
- `web/src/views/config/system-users/index.vue`
- `web/src/views/config/smart-structure-routing-rules/index.vue`
- `web/src/views/config/prompt-templates/index.vue`
- `web/src/views/config/auth-roles/index.vue`
- `web/src/views/config/column-mapping-rules/index.vue`
- `web/src/views/config/ai-services/index.vue`

## Task 1：建立确认框取消分类

**Files:**

- Create: `web/src/utils/message-box.ts`
- Create: `web/src/utils/message-box.test.ts`

- [x] **Step 1：编写失败测试**

测试 `isMessageBoxCancel`：

```typescript
expect(isMessageBoxCancel("cancel")).toBe(true);
expect(isMessageBoxCancel("close")).toBe(true);
expect(isMessageBoxCancel("canceled")).toBe(false);
expect(isMessageBoxCancel({ response: { status: 403 } })).toBe(false);
expect(isMessageBoxCancel({ response: { status: 500 } })).toBe(false);
expect(isMessageBoxCancel(new Error("Network Error"))).toBe(false);
expect(isMessageBoxCancel({ isAxiosError: true, code: "ERR_CANCELED" })).toBe(false);
```

- [x] **Step 2：运行测试确认失败**

Run:

```powershell
pnpm --dir web test:vitest -- src/utils/message-box.test.ts
```

Expected: FAIL，helper 不存在。

- [x] **Step 3：实现严格判断**

```typescript
export function isMessageBoxCancel(error: unknown): boolean {
  return error === "cancel" || error === "close";
}
```

不得把 Axios 的 `ERR_CANCELED` 归为确认框取消；请求取消仍属于调用链需要显式处理的异常。

- [x] **Step 4：运行 helper 测试确认通过**

Run: Step 2 命令。

Expected: PASS。

## Task 2：建立破坏性操作源码守卫

**Files:**

- Create: `web/tests/destructive-action-errors.test.ts`

- [x] **Step 1：编写失败守卫**

测试读取文件边界中的 10 个 Vue 文件并验证：

- 每个文件导入 `isMessageBoxCancel`。
- 每个待治理 `catch` 使用具名 `error` 参数。
- catch 中先判断 `isMessageBoxCancel(error)`，非取消分支调用 `getRequestErrorMessage(error, ...)`。
- 不再存在只含 `// 用户取消` 或 `// cancelled` 的无条件空 catch。

对客户、制程、机型和 SpecTable 额外确认单删与批删两个路径均已覆盖。

- [x] **Step 2：运行守卫确认失败**

Run:

```powershell
pnpm --dir web test:node -- tests/destructive-action-errors.test.ts
```

Expected: FAIL，当前多个入口仍无条件吞掉异常。

## Task 3：替换基础数据单删与批删路径

**Files:**

- Modify: `web/src/views/base-data/customers/index.vue`
- Modify: `web/src/views/base-data/processes/index.vue`
- Modify: `web/src/views/base-data/machine-models/index.vue`
- Modify: `web/src/views/base-data/specs/components/SpecTable.vue`

- [x] **Step 1：为四个文件增加共享导入**

```typescript
import { isMessageBoxCancel } from "@/utils/message-box";
import { getRequestErrorMessage } from "@/utils/error-message";
```

已有 `getRequestErrorMessage` 导入时不重复添加。

- [x] **Step 2：替换单删和批删 catch**

使用一致模式，fallback 按页面语义分别为“删除失败”或“批量删除失败”：

```typescript
} catch (error) {
  if (isMessageBoxCancel(error)) return;
  ElMessage.error(getRequestErrorMessage(error, "删除失败"));
}
```

请求异常时不得清空勾选、调用 `loadData()` 或显示成功消息；保留响应 `code != 0` 的现有错误提示。

- [x] **Step 3：运行源码守卫观察剩余失败**

Run: Task 2 Step 2 命令。

Expected: 这四个文件通过，配置类页面仍失败。

## Task 4：替换配置类删除、恢复和重置路径

**Files:**

- Modify: `web/src/views/config/system-users/index.vue`
- Modify: `web/src/views/config/smart-structure-routing-rules/index.vue`
- Modify: `web/src/views/config/prompt-templates/index.vue`
- Modify: `web/src/views/config/auth-roles/index.vue`
- Modify: `web/src/views/config/column-mapping-rules/index.vue`
- Modify: `web/src/views/config/ai-services/index.vue`

- [x] **Step 1：逐文件应用取消和错误模式**

覆盖审核指出的用户删除、路由规则删除、提示词模板删除、角色删除、列映射恢复默认、AI 服务删除路径。每个 catch 使用对应 fallback；已有正确实现也统一调用 `isMessageBoxCancel`，删除散落的字符串判断。

- [x] **Step 2：确认状态更新只发生在成功分支**

逐文件检查：

- 成功消息只在 `res.code === 0` 后显示。
- 列表刷新、选择清空、弹窗关闭只在成功后执行。
- 权限不足、500、网络断开和解析异常进入 `ElMessage.error`。

- [x] **Step 3：运行 helper、源码守卫和类型检查**

Run:

```powershell
pnpm --dir web test:vitest -- src/utils/message-box.test.ts src/utils/error-message.test.ts
pnpm --dir web test:node -- tests/destructive-action-errors.test.ts
pnpm --dir web typecheck
```

Expected: 全部 PASS。

- [x] **Step 4：运行前端全量回归**

Run:

```powershell
pnpm --dir web test
```

Expected: 全部 PASS。

- [x] **Step 5：提交 1B**

```powershell
git add web/src/utils/message-box.ts web/src/utils/message-box.test.ts web/tests/destructive-action-errors.test.ts web/src/views/base-data/customers/index.vue web/src/views/base-data/processes/index.vue web/src/views/base-data/machine-models/index.vue web/src/views/base-data/specs/components/SpecTable.vue web/src/views/config/system-users/index.vue web/src/views/config/smart-structure-routing-rules/index.vue web/src/views/config/prompt-templates/index.vue web/src/views/config/auth-roles/index.vue web/src/views/config/column-mapping-rules/index.vue web/src/views/config/ai-services/index.vue docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1b-action-errors.md
git commit -m "fix: 显示破坏性操作请求错误"
```
