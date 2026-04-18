import type { ColumnMapping as WordColumnMapping } from "@/api/document";
import {
  ColumnMappingMatchMode,
  ColumnMappingTargetField,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";

type WordRuleFieldKey =
  | "projectColumnIndex"
  | "specificationColumnIndex"
  | "acceptanceColumnIndex"
  | "remarkColumnIndex";

type WordRuleResult = {
  projectColumnIndex?: number;
  specificationColumnIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
};

const normalizeHeader = (value?: string) => (value || "").trim().toLowerCase();

const getRulesByTarget = (rules: ColumnMappingRule[]) => {
  const grouped = new Map<ColumnMappingTargetField, ColumnMappingRule[]>();

  rules
    .filter(rule => rule.enabled)
    .forEach(rule => {
      const items = grouped.get(rule.targetField) || [];
      items.push(rule);
      grouped.set(rule.targetField, items);
    });

  grouped.forEach((items, targetField) => {
    grouped.set(
      targetField,
      [...items].sort(
        (a, b) => (b.priority ?? 0) - (a.priority ?? 0) || a.id - b.id
      )
    );
  });

  return grouped;
};

export const matchHeaderByRule = (
  header: string,
  rule: ColumnMappingRule
): boolean => {
  const normalizedHeader = normalizeHeader(header);
  const normalizedPattern = normalizeHeader(rule.pattern);
  if (!normalizedHeader || !normalizedPattern) {
    return false;
  }

  switch (rule.matchMode) {
    case ColumnMappingMatchMode.Equals:
      return normalizedHeader === normalizedPattern;
    case ColumnMappingMatchMode.Regex:
      try {
        return new RegExp(rule.pattern, "i").test(header);
      } catch {
        return false;
      }
    case ColumnMappingMatchMode.Contains:
    default:
      return normalizedHeader.includes(normalizedPattern);
  }
};

const pickBestColumnIndex = (
  headers: string[],
  rules: ColumnMappingRule[],
  used: Set<number>
) => {
  let bestIndex: number | undefined;
  let bestScore = Number.NEGATIVE_INFINITY;

  headers.forEach((header, index) => {
    if (used.has(index)) {
      return;
    }

    rules.forEach(rule => {
      if (!matchHeaderByRule(header, rule)) {
        return;
      }

      const score =
        (rule.priority ?? 0) * 10000 + (rule.pattern?.length ?? 0) * 10 - index;

      if (score > bestScore) {
        bestScore = score;
        bestIndex = index;
      }
    });
  });

  return bestIndex;
};

const pickFallbackColumnIndex = (
  totalColumns: number,
  preferredIndex: number,
  used: Set<number>
) => {
  if (preferredIndex < totalColumns && !used.has(preferredIndex)) {
    return preferredIndex;
  }

  for (let index = 0; index < totalColumns; index += 1) {
    if (!used.has(index)) {
      return index;
    }
  }

  return undefined;
};

export const matchWordTableColumnsByRules = (
  headers: string[],
  rules: ColumnMappingRule[],
  options?: {
    fallbackToSequential?: boolean;
  }
): WordRuleResult => {
  const result: WordRuleResult = {};
  const groupedRules = getRulesByTarget(rules);
  const used = new Set<number>();
  const totalColumns = headers.length;

  const targetFields: Array<{
    field: WordRuleFieldKey;
    target: ColumnMappingTargetField;
    preferredIndex: number;
  }> = [
    {
      field: "projectColumnIndex",
      target: ColumnMappingTargetField.Project,
      preferredIndex: 0
    },
    {
      field: "specificationColumnIndex",
      target: ColumnMappingTargetField.Specification,
      preferredIndex: 1
    },
    {
      field: "acceptanceColumnIndex",
      target: ColumnMappingTargetField.Acceptance,
      preferredIndex: 2
    },
    {
      field: "remarkColumnIndex",
      target: ColumnMappingTargetField.Remark,
      preferredIndex: 3
    }
  ];

  targetFields.forEach(({ field, target }) => {
    const matchedIndex = pickBestColumnIndex(
      headers,
      groupedRules.get(target) || [],
      used
    );

    if (matchedIndex !== undefined) {
      result[field] = matchedIndex;
      used.add(matchedIndex);
    }
  });

  if (options?.fallbackToSequential) {
    targetFields.forEach(({ field, preferredIndex }) => {
      if (result[field] !== undefined) {
        return;
      }

      const fallbackIndex = pickFallbackColumnIndex(
        totalColumns,
        preferredIndex,
        used
      );
      if (fallbackIndex !== undefined) {
        result[field] = fallbackIndex;
        used.add(fallbackIndex);
      }
    });
  }

  return result;
};

export const applyWordRulesToWordMapping = (
  mapping: WordColumnMapping,
  headers: string[],
  rules: ColumnMappingRule[],
  overwrite = false
): WordColumnMapping => {
  const next: WordColumnMapping = {
    ...mapping
  };
  const groupedRules = getRulesByTarget(rules);
  const used = new Set<number>();

  const markUsed = (index?: number) => {
    if (index !== undefined) {
      used.add(index);
    }
  };

  if (!overwrite) {
    markUsed(next.projectColumn);
    markUsed(next.specificationColumn);
    markUsed(next.acceptanceColumn);
    markUsed(next.remarkColumn);
  }

  const tryApply = (
    field: keyof WordColumnMapping,
    target: ColumnMappingTargetField
  ) => {
    if (!overwrite && next[field] !== undefined) {
      return;
    }

    const matchedIndex = pickBestColumnIndex(
      headers,
      groupedRules.get(target) || [],
      used
    );

    if (matchedIndex !== undefined) {
      next[field] = matchedIndex;
      used.add(matchedIndex);
    }
  };

  tryApply("projectColumn", ColumnMappingTargetField.Project);
  tryApply("specificationColumn", ColumnMappingTargetField.Specification);
  tryApply("acceptanceColumn", ColumnMappingTargetField.Acceptance);
  tryApply("remarkColumn", ColumnMappingTargetField.Remark);

  return next;
};

export { ColumnMappingTargetField, ColumnMappingMatchMode };
