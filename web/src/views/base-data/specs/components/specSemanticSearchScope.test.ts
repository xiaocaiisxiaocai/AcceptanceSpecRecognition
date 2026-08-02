import { describe, expect, it } from "vitest";
import type { SpecSemanticSearchRequest } from "@/api/spec";
import { buildSemanticSearchScopeKey } from "./specSemanticSearchScope";

describe("验收规格语义搜索作用域", () => {
  it("部门或客户分组变化后生成不同的搜索作用域键", () => {
    const scopeA: SpecSemanticSearchRequest = {
      orgUnitId: 6,
      customerId: 1,
      machineModelId: 2,
      processId: 3,
      queries: ["平台精度"],
      topK: 5,
      minScore: 0.5
    };
    const scopeB: SpecSemanticSearchRequest = {
      ...scopeA,
      customerId: 9
    };
    const otherDepartment: SpecSemanticSearchRequest = {
      ...scopeA,
      orgUnitId: 7
    };

    expect(buildSemanticSearchScopeKey(scopeA)).toBe(
      '{"orgUnitId":6,"customerId":1,"machineModelId":2,"processId":3,"queries":["平台精度"],"topK":5,"minScore":0.5}'
    );
    expect(buildSemanticSearchScopeKey(scopeA)).not.toBe(
      buildSemanticSearchScopeKey(scopeB)
    );
    expect(buildSemanticSearchScopeKey(scopeA)).not.toBe(
      buildSemanticSearchScopeKey(otherDepartment)
    );
  });
});
