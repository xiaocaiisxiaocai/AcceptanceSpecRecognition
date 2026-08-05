const timezoneSuffixPattern = /(?:Z|[+-]\d{2}:?\d{2})$/i;
const dateTimeWithoutTimezonePattern =
  /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?$/;

/**
 * 后端时间统一按 UTC 保存。兼容 MySQL datetime 读回后丢失时区标记的历史响应。
 */
export const normalizeApiUtcDateTime = (value: string) => {
  const text = value.trim();
  if (timezoneSuffixPattern.test(text)) return text;
  return dateTimeWithoutTimezonePattern.test(text)
    ? `${text.replace(" ", "T")}Z`
    : text;
};

export const parseApiUtcDateTime = (value?: string | null) => {
  if (!value?.trim()) return null;
  const date = new Date(normalizeApiUtcDateTime(value));
  return Number.isNaN(date.getTime()) ? null : date;
};

export const formatApiUtcDateTime = (
  value?: string | null,
  locales?: string | string[],
  options?: Intl.DateTimeFormatOptions
) => {
  const date = parseApiUtcDateTime(value);
  return date ? date.toLocaleString(locales, options) : "-";
};

/** 将日期选择器产生的本地时间点转换为 API 使用的 UTC ISO 字符串。 */
export const toApiUtcDateTime = (value?: Date | null) =>
  value && !Number.isNaN(value.getTime()) ? value.toISOString() : undefined;
