const timezoneSuffixPattern = /(Z|[+-]\d{2}:?\d{2})$/i;
const dateTimeWithoutTimezonePattern =
  /^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?$/;

export const parseExecutionHistoryDateTime = (value?: string) => {
  if (!value) return null;

  const text = value.trim();
  if (!text) return null;

  // 后端时间按 UTC 入库；MySQL datetime 读回后会丢失时区标记，这里补回 UTC 语义。
  const normalized = timezoneSuffixPattern.test(text)
    ? text
    : dateTimeWithoutTimezonePattern.test(text)
      ? `${text.replace(" ", "T")}Z`
      : text;

  const date = new Date(normalized);
  return Number.isNaN(date.getTime()) ? null : date;
};

export const formatExecutionHistoryDateTime = (value?: string) => {
  const date = parseExecutionHistoryDateTime(value);
  return date ? date.toLocaleString() : "-";
};
