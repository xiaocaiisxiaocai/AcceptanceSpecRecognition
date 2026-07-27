import { describe, expect, it } from "vitest";
import { loginRules, REGEXP_PWD } from "./rule";

const validatePassword = (value: string) =>
  new Promise<string | undefined>(resolve => {
    const rules = loginRules.password;
    const rule = (Array.isArray(rules) ? rules[0] : rules)!;
    rule.validator?.(
      {} as never,
      value,
      (error?: string | Error) => {
        resolve(error instanceof Error ? error.message : error);
      },
      {} as never,
      {} as never
    );
  });

describe("登录密码 4～200 位边界", () => {
  it.each([
    { length: 3, accepted: false },
    { length: 4, accepted: true },
    { length: 200, accepted: true },
    { length: 201, accepted: false }
  ])("$length 位密码 accepted=$accepted", async ({ length, accepted }) => {
    const password = "p".repeat(length);

    expect(REGEXP_PWD.test(password)).toBe(accepted);
    expect(await validatePassword(password)).toBe(
      accepted ? undefined : "密码长度必须在4到200位之间"
    );
  });
});
