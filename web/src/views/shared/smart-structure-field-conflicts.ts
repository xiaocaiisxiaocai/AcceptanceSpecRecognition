import type { ColumnMappingTargetField } from "@/api/column-mapping-rules";
import type {
  SmartConfigConfirmRequest,
  SmartConfigFieldCandidate,
  SmartConfigRecognizedFieldName,
  SmartConfigRecognizedTable
} from "@/api/smart-config";

export type SmartStructureFieldConflictItem = {
  key: string;
  tableIndex: number;
  tableName: string;
  regionId: string;
  regionIndex: number;
  field: SmartConfigRecognizedFieldName;
  fieldLabel: string;
  dataStartRowIndex: number;
  dataEndRowIndex?: number | null;
  recommendedColumnIndex?: number | null;
  candidates: SmartConfigFieldCandidate[];
};

export type SmartStructureFieldConflictSelection = {
  key: string;
  tableIndex: number;
  regionId: string;
  regionIndex: number;
  field: SmartConfigRecognizedFieldName;
  columnIndex: number;
};

export const getSmartStructureRecommendedColumnIndex = (
  conflict: SmartStructureFieldConflictItem
) =>
  conflict.candidates.find(
    candidate => candidate.columnIndex === conflict.recommendedColumnIndex
  )?.columnIndex ??
  conflict.candidates.find(candidate => candidate.isRecommended)?.columnIndex;

export const createRecommendedSmartStructureFieldSelections = (
  conflicts: readonly SmartStructureFieldConflictItem[]
): Record<string, number | undefined> =>
  Object.fromEntries(
    conflicts.map(conflict => [
      conflict.key,
      getSmartStructureRecommendedColumnIndex(conflict)
    ])
  );

const fieldLabels: Record<string, string> = {
  Project: "项目列",
  Specification: "规格列",
  Acceptance: "验收列",
  Remark: "备注列"
};

const fieldColumnProperties: Record<
  string,
  | "projectColumnIndex"
  | "specificationColumnIndex"
  | "acceptanceColumnIndex"
  | "remarkColumnIndex"
> = {
  Project: "projectColumnIndex",
  Specification: "specificationColumnIndex",
  Acceptance: "acceptanceColumnIndex",
  Remark: "remarkColumnIndex"
};

const fieldTargets: Record<string, ColumnMappingTargetField> = {
  Project: 1 as ColumnMappingTargetField,
  Specification: 2 as ColumnMappingTargetField,
  Acceptance: 3 as ColumnMappingTargetField,
  Remark: 4 as ColumnMappingTargetField
};

const buildConflictKey = (
  tableIndex: number,
  regionId: string,
  field: string
) => `${tableIndex}:${regionId}:${field}`;

export const collectSmartStructureFieldConflicts = (
  tables: readonly SmartConfigRecognizedTable[],
  selectedTableIndexes: readonly number[]
): SmartStructureFieldConflictItem[] => {
  const selected = new Set(selectedTableIndexes);
  return tables
    .filter(table => selected.has(table.tableIndex))
    .flatMap(table =>
      (table.regions ?? []).flatMap(region =>
        (region.fieldConflicts ?? [])
          .filter(conflict => conflict.candidates.length > 1)
          .map(conflict => ({
            key: buildConflictKey(
              table.tableIndex,
              region.regionId,
              conflict.field
            ),
            tableIndex: table.tableIndex,
            tableName:
              table.tableName?.trim() || `工作表 ${table.tableIndex + 1}`,
            regionId: region.regionId,
            regionIndex: region.regionIndex,
            field: conflict.field,
            fieldLabel: fieldLabels[conflict.field] ?? conflict.field,
            dataStartRowIndex: region.dataStartRowIndex,
            dataEndRowIndex: region.dataEndRowIndex,
            recommendedColumnIndex: conflict.recommendedColumnIndex,
            candidates: conflict.candidates
          }))
      )
    );
};

export const applySmartStructureFieldSelectionsToTable = (
  table: SmartConfigRecognizedTable,
  selections: readonly SmartStructureFieldConflictSelection[]
): SmartConfigRecognizedTable => {
  const tableSelections = selections.filter(
    selection => selection.tableIndex === table.tableIndex
  );
  if (tableSelections.length === 0) return table;

  const regions = (table.regions ?? []).map(region => {
    const regionSelections = tableSelections.filter(
      selection =>
        selection.regionId === region.regionId ||
        selection.regionIndex === region.regionIndex
    );
    if (regionSelections.length === 0) return region;

    let nextRegion = { ...region };
    let fields = [...(region.fields ?? [])];
    const resolvedFields = new Set<string>();
    for (const selection of regionSelections) {
      const property = fieldColumnProperties[selection.field];
      if (!property) continue;
      const conflict = region.fieldConflicts?.find(
        item => item.field === selection.field
      );
      const candidate = conflict?.candidates.find(
        item => item.columnIndex === selection.columnIndex
      );
      nextRegion = { ...nextRegion, [property]: selection.columnIndex };
      fields = fields.map(field =>
        field.field === selection.field
          ? {
              ...field,
              columnIndex: selection.columnIndex,
              header:
                candidate?.header ??
                region.headers[selection.columnIndex] ??
                field.header,
              confidence: candidate?.confidence ?? field.confidence,
              source: "UserConfirmed"
            }
          : field
      );
      resolvedFields.add(selection.field);
    }

    return {
      ...nextRegion,
      fields,
      fieldConflicts: (region.fieldConflicts ?? []).filter(
        conflict => !resolvedFields.has(conflict.field)
      ),
      issues: (region.issues ?? []).filter(
        issue =>
          issue.code !== "AmbiguousFieldCandidates" ||
          !issue.field ||
          !resolvedFields.has(issue.field)
      )
    };
  });
  const primary = regions[0];
  return {
    ...table,
    ...(primary
      ? {
          projectColumnIndex: primary.projectColumnIndex,
          specificationColumnIndex: primary.specificationColumnIndex,
          acceptanceColumnIndex: primary.acceptanceColumnIndex,
          remarkColumnIndex: primary.remarkColumnIndex,
          fields: primary.fields
        }
      : {}),
    regions,
    fieldConflicts: regions.flatMap(region => region.fieldConflicts ?? []),
    issues: (table.issues ?? []).filter(
      issue =>
        issue.code !== "AmbiguousFieldCandidates" ||
        !issue.field ||
        !tableSelections.some(selection => selection.field === issue.field)
    )
  };
};

export const applySmartStructureFieldSelectionsToDraft = (
  request: SmartConfigConfirmRequest,
  table: SmartConfigRecognizedTable,
  selections: readonly SmartStructureFieldConflictSelection[]
): SmartConfigConfirmRequest => {
  const tableSelections = selections.filter(
    selection => selection.tableIndex === table.tableIndex
  );
  if (tableSelections.length === 0) return request;

  const regions = (request.regions ?? []).map(region => {
    const selectionsForRegion = tableSelections.filter(
      selection =>
        selection.regionId === region.regionId ||
        selection.regionIndex === region.regionIndex
    );
    return selectionsForRegion.reduce((current, selection) => {
      const property = fieldColumnProperties[selection.field];
      return property
        ? { ...current, [property]: selection.columnIndex }
        : current;
    }, region);
  });
  const primary = regions[0];
  const learnedColumns = [...(request.learnedColumns ?? [])];
  for (const selection of tableSelections) {
    const targetField = fieldTargets[selection.field];
    if (targetField == null) continue;
    const region = table.regions?.find(
      item =>
        item.regionId === selection.regionId ||
        item.regionIndex === selection.regionIndex
    );
    const header = region?.headers[selection.columnIndex]?.trim();
    if (!header) continue;
    const existingIndex = learnedColumns.findIndex(
      item => item.targetField === targetField
    );
    const learnedColumn = { header, targetField };
    if (existingIndex >= 0) learnedColumns[existingIndex] = learnedColumn;
    else learnedColumns.push(learnedColumn);
  }

  return {
    ...request,
    ...(primary
      ? {
          projectColumnIndex: primary.projectColumnIndex,
          specificationColumnIndex: primary.specificationColumnIndex,
          acceptanceColumnIndex: primary.acceptanceColumnIndex,
          remarkColumnIndex: primary.remarkColumnIndex
        }
      : {}),
    regions,
    learnedColumns,
    userModifiedStructure: true
  };
};
