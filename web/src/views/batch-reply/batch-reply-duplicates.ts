import type {
  BatchReplyDuplicateGroup,
  BatchReplyDuplicateResolution,
  BatchReplyDuplicateStrategy
} from "@/api/matching";
import type { BatchReplyTableConfigItem } from "./batch-reply-table-config";

export type BatchReplyDuplicateDialogState = {
  targetId: string;
  tableIndex: number;
  sourceTableIndex: number;
  groups: BatchReplyDuplicateGroup[];
  strategies: Record<string, BatchReplyDuplicateStrategy>;
};

type TargetConfigState = {
  targetId: string;
  configs: BatchReplyTableConfigItem[];
};

export const getDuplicateSourceLabel = (
  duplicateSource: BatchReplyDuplicateGroup["duplicateSource"]
) => (duplicateSource === "source" ? "来源表重复" : "目标表重复");

const getDuplicateResolutionFromConfig = (
  resolutions: BatchReplyDuplicateResolution[] | undefined,
  groupId: string
) => resolutions?.find(item => item.groupId === groupId)?.strategy;

const upsertDuplicateResolutions = (
  resolutions: BatchReplyDuplicateResolution[] | undefined,
  nextResolution: BatchReplyDuplicateResolution
) => {
  const nextList = [...(resolutions ?? [])];
  const existingIndex = nextList.findIndex(
    item => item.groupId === nextResolution.groupId
  );
  if (existingIndex >= 0) {
    nextList[existingIndex] = nextResolution;
  } else {
    nextList.push(nextResolution);
  }

  return nextList;
};

export const buildDuplicateDialogState = (params: {
  targetId: string;
  item: BatchReplyTableConfigItem;
  groups: BatchReplyDuplicateGroup[];
  sourceConfigs: BatchReplyTableConfigItem[];
  targetConfigs: BatchReplyTableConfigItem[] | undefined;
}): BatchReplyDuplicateDialogState => {
  const { targetId, item, groups, sourceConfigs, targetConfigs } = params;
  const strategies = Object.fromEntries(
    groups.map(group => {
      const config =
        group.duplicateSource === "source"
          ? sourceConfigs.find(source => source.tableIndex === group.tableIndex)
          : targetConfigs?.find(
              configItem => configItem.tableIndex === group.tableIndex
            );
      return [
        group.groupId,
        getDuplicateResolutionFromConfig(
          config?.duplicateResolutions,
          group.groupId
        ) ?? "keepFirst"
      ];
    })
  ) as Record<string, BatchReplyDuplicateStrategy>;

  return {
    targetId,
    tableIndex: item.tableIndex,
    sourceTableIndex: item.sourceTableIndex ?? item.tableIndex,
    groups,
    strategies
  };
};

export const updateDuplicateDialogStrategyState = (
  dialog: BatchReplyDuplicateDialogState | null,
  groupId: string,
  strategy: BatchReplyDuplicateStrategy
) => {
  if (!dialog) {
    return null;
  }

  return {
    ...dialog,
    strategies: {
      ...dialog.strategies,
      [groupId]: strategy
    }
  };
};

export const applyDuplicateResolutionState = <
  TTarget extends TargetConfigState
>(params: {
  dialog: BatchReplyDuplicateDialogState;
  sourceConfigs: BatchReplyTableConfigItem[];
  targetFiles: TTarget[];
}) => {
  const { dialog, sourceConfigs, targetFiles } = params;
  const groupedResolutions = dialog.groups.reduce(
    (acc, group) => {
      const resolution: BatchReplyDuplicateResolution = {
        groupId: group.groupId,
        strategy: dialog.strategies[group.groupId] ?? "keepFirst"
      };

      if (group.duplicateSource === "source") {
        acc.source[group.tableIndex] = upsertDuplicateResolutions(
          acc.source[group.tableIndex],
          resolution
        );
      } else {
        acc.target[group.tableIndex] = upsertDuplicateResolutions(
          acc.target[group.tableIndex],
          resolution
        );
      }

      return acc;
    },
    {
      source: {} as Record<number, BatchReplyDuplicateResolution[]>,
      target: {} as Record<number, BatchReplyDuplicateResolution[]>
    }
  );

  return {
    sourceConfigs: sourceConfigs.map(config =>
      groupedResolutions.source[config.tableIndex]
        ? {
            ...config,
            duplicateResolutions: groupedResolutions.source[config.tableIndex]
          }
        : config
    ),
    targetFiles: targetFiles.map(file =>
      file.targetId !== dialog.targetId
        ? file
        : {
            ...file,
            configs: file.configs.map(config =>
              groupedResolutions.target[config.tableIndex]
                ? {
                    ...config,
                    duplicateResolutions:
                      groupedResolutions.target[config.tableIndex]
                  }
                : config
            )
          }
    )
  };
};
