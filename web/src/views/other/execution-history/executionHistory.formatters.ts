import {
  formatApiUtcDateTime,
  parseApiUtcDateTime
} from "../../../utils/date-time.ts";

export const parseExecutionHistoryDateTime = (value?: string) =>
  parseApiUtcDateTime(value);

export const formatExecutionHistoryDateTime = (value?: string) =>
  formatApiUtcDateTime(value);
