import { computed, ref } from "vue";
import type { ComputedRef, Ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getExcelPreviewColumnIndexes,
  getPreviewCellValue,
  getWordPreviewColumnIndexes,
  normalizeExcelMappingByTable,
  shouldBackfillProjectFromSpecification
} from "../dataImport.helpers";
import { buildImportRegionKey } from "../dataImport.regions";
import type {
  ImportPreviewGroup,
  ImportPreviewRow,
  TableImportConfig
} from "../dataImport.types";

type UseDataImportPreviewSelectionOptions = {
  isExcelFile: ComputedRef<boolean>;
  tableConfigs: Ref<TableImportConfig[]>;
};

const isBlankPreviewValue = (value: string) => value.trim().length === 0;

export const isAcceptanceAndRemarkBlank = (
  row: Pick<ImportPreviewRow, "acceptance" | "remark">
) => isBlankPreviewValue(row.acceptance) && isBlankPreviewValue(row.remark);

export function useDataImportPreviewSelection(
  options: UseDataImportPreviewSelectionOptions
) {
  const excludedRowIndexMap = ref<Record<number, number[]>>({});
  const importPreviewSelectionKeys = ref<string[]>([]);

  const getExcludedRowIndexes = (tableIndex: number) =>
    excludedRowIndexMap.value[tableIndex] || [];

  const getExcludedRowIndexSet = (tableIndex: number) =>
    new Set(getExcludedRowIndexes(tableIndex));

  const setExcludedRowIndexes = (tableIndex: number, rowIndexes: number[]) => {
    const deduped = Array.from(
      new Set(
        rowIndexes
          .filter(index => Number.isInteger(index) && index >= 0)
          .sort((a, b) => a - b)
      )
    );

    if (deduped.length === 0) {
      const { [tableIndex]: _, ...rest } = excludedRowIndexMap.value;
      excludedRowIndexMap.value = rest;
      return;
    }

    excludedRowIndexMap.value = {
      ...excludedRowIndexMap.value,
      [tableIndex]: deduped
    };
  };

  const importPreviewGroups = computed<ImportPreviewGroup[]>(() => {
    return options.tableConfigs.value.flatMap(cfg => {
      const previewData = cfg.previewData;
      const labelPrefix = options.isExcelFile.value ? "工作表" : "表格";
      const displayName = cfg.tableInfo?.name?.trim();
      const tableLabel = displayName
        ? `${labelPrefix} ${cfg.tableIndex + 1}（${displayName}）`
        : `${labelPrefix} ${cfg.tableIndex + 1}`;

      if (!previewData) {
        return [
          {
            key: buildImportRegionKey(cfg.tableIndex),
            tableIndex: cfg.tableIndex,
            label: tableLabel,
            rows: []
          }
        ];
      }

      const excludedRowIndexes = getExcludedRowIndexSet(cfg.tableIndex);
      const rows = previewData.rows.map((rowValues, rowIndex) => {
        const rowLocation = cfg.excelPreviewRowLocations?.[rowIndex];
        const rowConfig = rowLocation
          ? options.isExcelFile.value && "headerRowStart" in rowLocation.mapping
            ? { ...cfg, excelMapping: rowLocation.mapping }
            : !options.isExcelFile.value &&
                "headerRowIndex" in rowLocation.mapping
              ? { ...cfg, wordMapping: rowLocation.mapping }
              : cfg
          : cfg;
        const columnIndexes = options.isExcelFile.value
          ? getExcelPreviewColumnIndexes(rowConfig)
          : getWordPreviewColumnIndexes(rowConfig);
        const excelMapping = options.isExcelFile.value
          ? normalizeExcelMappingByTable(cfg.tableInfo, rowConfig.excelMapping)
          : null;
        const specification = getPreviewCellValue(
          rowValues,
          columnIndexes.specificationColumn
        );
        return {
          key: `${cfg.tableIndex}:${rowLocation?.regionId ?? "default"}:${rowIndex}`,
          tableIndex: cfg.tableIndex,
          regionId: rowLocation?.regionId,
          regionIndex: rowLocation?.regionIndex,
          relativeRowIndex: rowLocation?.relativeRowIndex,
          rowIndex,
          displayRowNumber: options.isExcelFile.value
            ? (rowLocation?.displayRowNumber ??
              (excelMapping?.dataStartRow ?? 1) + rowIndex)
            : (cfg.wordMapping?.dataStartRowIndex ?? 0) + rowIndex + 1,
          project:
            (rowLocation?.mapping.isSpecificationOnly ??
            shouldBackfillProjectFromSpecification(cfg))
              ? specification
              : getPreviewCellValue(rowValues, columnIndexes.projectColumn),
          specification,
          acceptance: getPreviewCellValue(
            rowValues,
            columnIndexes.acceptanceColumn
          ),
          remark: getPreviewCellValue(rowValues, columnIndexes.remarkColumn)
        };
      });

      if (options.isExcelFile.value) {
        const regionGroups = new Map<
          string,
          Pick<ImportPreviewGroup, "key" | "regionId" | "regionIndex">
        >();
        for (const row of rows) {
          if (!row.regionId || regionGroups.has(row.regionId)) continue;
          regionGroups.set(row.regionId, {
            key: buildImportRegionKey(cfg.tableIndex, row.regionId),
            regionId: row.regionId,
            regionIndex: row.regionIndex
          });
        }

        if (regionGroups.size > 0) {
          return Array.from(regionGroups.values()).map(region => ({
            ...region,
            tableIndex: cfg.tableIndex,
            label: `${tableLabel}· 区域 ${(region.regionIndex ?? 0) + 1}`,
            rows: rows.filter(
              row =>
                row.regionId === region.regionId &&
                !excludedRowIndexes.has(row.rowIndex)
            )
          }));
        }
      }

      return [
        {
          key: buildImportRegionKey(cfg.tableIndex),
          tableIndex: cfg.tableIndex,
          label: tableLabel,
          rows: rows.filter(row => !excludedRowIndexes.has(row.rowIndex))
        }
      ];
    });
  });

  const importPreviewRowMap = computed(() => {
    const rowMap = new Map<string, ImportPreviewRow>();
    for (const group of importPreviewGroups.value) {
      for (const row of group.rows) {
        rowMap.set(row.key, row);
      }
    }
    return rowMap;
  });

  const removedPreviewRowCount = computed(() => {
    return options.tableConfigs.value.reduce(
      (sum, cfg) => sum + getExcludedRowIndexes(cfg.tableIndex).length,
      0
    );
  });

  const selectedImportPreviewRowsCount = computed(
    () => importPreviewSelectionKeys.value.length
  );

  const blankAcceptanceRemarkRows = computed(() =>
    importPreviewGroups.value.flatMap(group =>
      group.rows.filter(isAcceptanceAndRemarkBlank)
    )
  );

  const irrelevantPreviewRowCount = computed(
    () => blankAcceptanceRemarkRows.value.length
  );

  const selectedImportPreviewRowKeySet = computed(
    () => new Set(importPreviewSelectionKeys.value)
  );

  const selectedIrrelevantPreviewRowCount = computed(
    () =>
      blankAcceptanceRemarkRows.value.filter(row =>
        selectedImportPreviewRowKeySet.value.has(row.key)
      ).length
  );

  const allIrrelevantPreviewRowsSelected = computed(
    () =>
      irrelevantPreviewRowCount.value > 0 &&
      selectedIrrelevantPreviewRowCount.value ===
        irrelevantPreviewRowCount.value
  );

  const someIrrelevantPreviewRowsSelected = computed(
    () =>
      selectedIrrelevantPreviewRowCount.value > 0 &&
      !allIrrelevantPreviewRowsSelected.value
  );

  const handleImportPreviewSelectionChange = (
    tableIndex: number,
    rows: ImportPreviewRow[],
    visibleRows?: ImportPreviewRow[]
  ) => {
    const visibleRowKeys = visibleRows
      ? new Set(visibleRows.map(row => row.key))
      : null;
    const remainingKeys = importPreviewSelectionKeys.value.filter(key =>
      visibleRowKeys
        ? !visibleRowKeys.has(key)
        : !key.startsWith(`${tableIndex}:`)
    );
    importPreviewSelectionKeys.value = [
      ...remainingKeys,
      ...rows.map(row => row.key)
    ];
  };

  const handleSelectIrrelevantRowsChange = (selected: boolean) => {
    const irrelevantRowKeys = new Set(
      blankAcceptanceRemarkRows.value.map(row => row.key)
    );

    if (!selected) {
      importPreviewSelectionKeys.value =
        importPreviewSelectionKeys.value.filter(
          key => !irrelevantRowKeys.has(key)
        );
      return;
    }

    const selectedKeys = new Set(importPreviewSelectionKeys.value);
    importPreviewSelectionKeys.value = [
      ...importPreviewSelectionKeys.value,
      ...blankAcceptanceRemarkRows.value
        .map(row => row.key)
        .filter(key => !selectedKeys.has(key))
    ];
  };

  const excludeImportPreviewRows = (
    rows: Pick<ImportPreviewRow, "tableIndex" | "rowIndex" | "key">[]
  ) => {
    if (rows.length === 0) return 0;

    const rowsByTable = new Map<number, number[]>();
    const removedKeys = new Set<string>();
    for (const row of rows) {
      const list = rowsByTable.get(row.tableIndex) || [];
      list.push(row.rowIndex);
      rowsByTable.set(row.tableIndex, list);
      removedKeys.add(row.key);
    }

    for (const [tableIndex, rowIndexes] of rowsByTable.entries()) {
      setExcludedRowIndexes(tableIndex, [
        ...getExcludedRowIndexes(tableIndex),
        ...rowIndexes
      ]);
    }

    importPreviewSelectionKeys.value = importPreviewSelectionKeys.value.filter(
      key => !removedKeys.has(key)
    );

    return rows.length;
  };

  const handleRemoveSinglePreviewRow = async (row: ImportPreviewRow) => {
    try {
      await ElMessageBox.confirm(
        `确认从本次待导入清单中移除第 ${row.displayRowNumber} 行吗？该操作不会修改原文件。`,
        "移除待导入数据",
        {
          confirmButtonText: "移除",
          cancelButtonText: "取消",
          type: "warning"
        }
      );

      excludeImportPreviewRows([row]);
      ElMessage.success("已移除 1 条待导入数据");
    } catch {
      // 用户取消
    }
  };

  const handleRemoveSelectedPreviewRows = async () => {
    if (importPreviewSelectionKeys.value.length === 0) {
      ElMessage.warning("请先勾选要移除的待导入数据");
      return;
    }

    const selectedRows = importPreviewSelectionKeys.value
      .map(key => importPreviewRowMap.value.get(key))
      .filter((row): row is ImportPreviewRow => !!row);

    if (selectedRows.length === 0) {
      ElMessage.warning("当前选中的待导入数据已失效，请重新选择");
      importPreviewSelectionKeys.value = [];
      return;
    }

    try {
      await ElMessageBox.confirm(
        `确认从本次待导入清单中批量移除 ${selectedRows.length} 条数据吗？该操作不会修改原文件。`,
        "批量移除待导入数据",
        {
          confirmButtonText: "移除",
          cancelButtonText: "取消",
          type: "warning"
        }
      );

      const removedCount = excludeImportPreviewRows(selectedRows);
      ElMessage.success(`已移除 ${removedCount} 条待导入数据`);
    } catch {
      // 用户取消
    }
  };

  const handleRestoreRemovedPreviewRows = () => {
    excludedRowIndexMap.value = {};
    importPreviewSelectionKeys.value = [];
    ElMessage.success("已恢复全部待导入数据");
  };

  return {
    excludedRowIndexMap,
    importPreviewSelectionKeys,
    getExcludedRowIndexes,
    getExcludedRowIndexSet,
    setExcludedRowIndexes,
    importPreviewGroups,
    removedPreviewRowCount,
    selectedImportPreviewRowsCount,
    irrelevantPreviewRowCount,
    allIrrelevantPreviewRowsSelected,
    someIrrelevantPreviewRowsSelected,
    handleImportPreviewSelectionChange,
    handleSelectIrrelevantRowsChange,
    handleRemoveSinglePreviewRow,
    handleRemoveSelectedPreviewRows,
    handleRestoreRemovedPreviewRows,
    excludeImportPreviewRows
  };
}
