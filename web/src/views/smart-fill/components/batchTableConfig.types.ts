import type { TableInfo } from "@/api/document";
import type { BatchTableConfig } from "@/api/matching";

/** 带勾选状态的表格配置项 */
export interface BatchTableConfigItem extends BatchTableConfig {
  /** 是否被选中 */
  selected: boolean;
  /** 表格信息（用于展示） */
  tableInfo: TableInfo;
  /** 字段映射是否仍由规则自动推导；用户手动改列后不再自动覆盖 */
  mappingAutoDetected?: boolean;
}
