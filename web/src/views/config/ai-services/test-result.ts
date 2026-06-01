import type {
  AiServiceConfig,
  AiServiceTestResult
} from "@/api/ai-service";
import { TEST_ACTION_LABEL } from "./constants";

export type TestResultTagType = "success" | "danger" | "warning" | "info";
export type TestResultCategory =
  | "success"
  | "auth"
  | "endpoint"
  | "rate-limit"
  | "timeout"
  | "remote"
  | "general";

export interface TestResultTag {
  label: string;
  type: TestResultTagType;
}

export interface TestResultDetail {
  label: string;
  value: string;
}

export interface InlineTestResultCard {
  rowId: number;
  rowName: string;
  success: boolean;
  category: TestResultCategory;
  statusText: string;
  summary: string;
  message: string;
  tags: TestResultTag[];
  details: TestResultDetail[];
}

const buildTestResultTags = (
  success: boolean,
  category: TestResultCategory
): TestResultTag[] => {
  const tags: TestResultTag[] = [
    {
      label: TEST_ACTION_LABEL,
      type: "info"
    },
    {
      label: success ? "成功" : "失败",
      type: success ? "success" : "danger"
    }
  ];

  if (success) {
    tags.push({
      label: "连接正常",
      type: "success"
    });
    return tags;
  }

  const categoryTagMap: Record<TestResultCategory, TestResultTag | null> = {
    success: null,
    auth: { label: "ApiKey", type: "danger" },
    endpoint: { label: "Endpoint", type: "warning" },
    "rate-limit": { label: "限流", type: "warning" },
    timeout: { label: "超时", type: "warning" },
    remote: { label: "远端服务", type: "info" },
    general: { label: "连接异常", type: "info" }
  };

  const categoryTag = categoryTagMap[category];
  if (categoryTag) {
    tags.push(categoryTag);
  }

  return tags;
};

const inferTestResultCategory = (
  success: boolean,
  message: string
): TestResultCategory => {
  if (success) return "success";

  if (
    message.includes("鉴权失败") ||
    message.includes("ApiKey") ||
    message.toLowerCase().includes("invalid authentication") ||
    message.toLowerCase().includes("invalid token")
  ) {
    return "auth";
  }

  if (message.includes("Endpoint") || message.includes("地址无效")) {
    return "endpoint";
  }

  if (
    message.includes("限流") ||
    message.includes("额度受限") ||
    message.includes("429")
  ) {
    return "rate-limit";
  }

  if (message.includes("超时")) {
    return "timeout";
  }

  if (message.includes("远端接口服务异常") || message.includes("HTTP 5")) {
    return "remote";
  }

  return "general";
};

const buildTestResultDetails = (
  row: AiServiceConfig,
  result: Pick<
    AiServiceTestResult,
    | "elapsedMs"
    | "serviceElapsedMs"
    | "targetModel"
    | "targetEndpoint"
    | "hostPort"
    | "httpStatusCode"
  >
): TestResultDetail[] => {
  const details: TestResultDetail[] = [
    { label: "服务", value: row.name },
    { label: "总耗时", value: `${result.elapsedMs}ms` }
  ];

  if (typeof result.serviceElapsedMs === "number") {
    details.push({ label: "接口耗时", value: `${result.serviceElapsedMs}ms` });
  }
  if (result.targetModel) {
    details.push({ label: "模型", value: result.targetModel });
  }
  if (result.targetEndpoint) {
    details.push({ label: "Endpoint", value: result.targetEndpoint });
  }
  if (result.hostPort) {
    details.push({ label: "宿主", value: result.hostPort });
  }
  if (result.httpStatusCode) {
    details.push({ label: "HTTP", value: String(result.httpStatusCode) });
  }

  return details;
};

export const buildInlineTestResultCard = (
  row: AiServiceConfig,
  result: AiServiceTestResult
): InlineTestResultCard => {
  const category = inferTestResultCategory(
    result.success,
    result.message || ""
  );

  return {
    rowId: row.id,
    rowName: row.name,
    success: result.success,
    category,
    statusText: result.success ? "连接正常" : "需要处理",
    summary: `${TEST_ACTION_LABEL}${result.success ? "成功" : "失败"}`,
    message: result.message || (result.success ? "测试通过" : "连接测试失败"),
    tags: buildTestResultTags(result.success, category),
    details: buildTestResultDetails(row, result)
  };
};

export const getTestResultCardClass = (
  category: TestResultCategory,
  success: boolean
) => {
  if (success) return "ai-test-result-card--success";

  const classMap: Record<TestResultCategory, string> = {
    success: "ai-test-result-card--success",
    auth: "ai-test-result-card--auth",
    endpoint: "ai-test-result-card--endpoint",
    "rate-limit": "ai-test-result-card--rate-limit",
    timeout: "ai-test-result-card--timeout",
    remote: "ai-test-result-card--remote",
    general: "ai-test-result-card--general"
  };

  return classMap[category];
};
