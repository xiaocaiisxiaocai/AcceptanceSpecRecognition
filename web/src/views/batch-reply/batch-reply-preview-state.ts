type PreviewTrackedConfig = {
  tableIndex: number;
  sourceTableIndex?: number;
  projectColumnIndex?: number;
  specificationColumnIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  headerRowStart?: number;
  headerRowCount?: number;
  dataStartRow?: number;
  filterEmptySourceRows?: boolean;
  duplicateResolutions?: unknown[];
  selected?: boolean;
};

const toComparableConfig = (config: PreviewTrackedConfig | undefined) => {
  if (!config) {
    return null;
  }

  return {
    tableIndex: config.tableIndex,
    sourceTableIndex: config.sourceTableIndex,
    projectColumnIndex: config.projectColumnIndex,
    specificationColumnIndex: config.specificationColumnIndex,
    acceptanceColumnIndex: config.acceptanceColumnIndex,
    remarkColumnIndex: config.remarkColumnIndex,
    headerRowStart: config.headerRowStart,
    headerRowCount: config.headerRowCount,
    dataStartRow: config.dataStartRow,
    filterEmptySourceRows: config.filterEmptySourceRows,
    duplicateResolutions: config.duplicateResolutions ?? [],
    selected: config.selected
  };
};

const toComparableSnapshot = (config: PreviewTrackedConfig | undefined) =>
  JSON.stringify(toComparableConfig(config));

export const buildBatchReplyPreviewFingerprint = (
  sessionId: string,
  targetId: string,
  config: PreviewTrackedConfig | undefined
) =>
  JSON.stringify({
    sessionId,
    targetId,
    config: toComparableConfig(config)
  });

export const prunePreviewResultsForConfigChange = <T>(
  previousResults: Record<number, T>,
  previousConfigs: PreviewTrackedConfig[],
  nextConfigs: PreviewTrackedConfig[]
) => {
  const previousMap = new Map(
    previousConfigs.map(config => [config.tableIndex, config])
  );
  const nextMap = new Map(
    nextConfigs.map(config => [config.tableIndex, config])
  );
  const nextResults: Record<number, T> = {};

  Object.entries(previousResults).forEach(([tableIndexText, result]) => {
    const tableIndex = Number(tableIndexText);
    const previousSnapshot = toComparableSnapshot(previousMap.get(tableIndex));
    const nextSnapshot = toComparableSnapshot(nextMap.get(tableIndex));

    if (previousSnapshot && previousSnapshot === nextSnapshot) {
      nextResults[tableIndex] = result;
    }
  });

  return nextResults;
};

export const createTargetPreviewLoaderResolver = <TLoader>(
  factory: (targetId: string) => TLoader
) => {
  const cache = new Map<string, TLoader>();

  return (targetId: string) => {
    const cached = cache.get(targetId);
    if (cached) {
      return cached;
    }

    const created = factory(targetId);
    cache.set(targetId, created);
    return created;
  };
};
