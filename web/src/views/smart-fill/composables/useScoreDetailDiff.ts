import { computed, ref, watch, type ComputedRef } from "vue";
import type { MatchCandidateOption, MatchPreviewItem } from "@/api/matching";

export type ScoreDetailDiffViewMode = "field" | "raw";

export type ScoreDetailDiffRow = {
  key: string;
  label: string;
  leftHtml: string;
  rightHtml: string;
  isSame: boolean;
  isRiskRelevant: boolean;
};

type InlineDiffCache = Map<
  string,
  { leftHtml: string; rightHtml: string; isSame: boolean }
>;

type UseScoreDetailDiffOptions = {
  item: ComputedRef<MatchPreviewItem | null>;
  topCandidates: ComputedRef<MatchCandidateOption[]>;
  inlineDiffCache: InlineDiffCache;
};

const escapeHtml = (text: string) =>
  text
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");

const formatHtmlFragment = (text: string) =>
  escapeHtml(text).replaceAll("\n", "<br />");

const renderHtmlText = (text?: string) => {
  if (!text || text.length === 0) {
    return `<span class="placeholder-text">（空）</span>`;
  }
  return formatHtmlFragment(text);
};

export const normalizeHintOnlyDiffText = (text?: string) =>
  (text ?? "")
    .replaceAll("\u00A0", " ")
    .replaceAll("\u200B", "")
    .replaceAll("\uFEFF", "")
    .trim()
    .toLowerCase()
    .replace(/[，,。；;：:、\s\r\n\t\-—–_·.]+/g, "")
    .replaceAll("（", "(")
    .replaceAll("）", ")");

export const isHintOnlyTextDifference = (
  leftText?: string,
  rightText?: string
) => {
  const left = normalizeHintOnlyDiffText(leftText);
  const right = normalizeHintOnlyDiffText(rightText);
  return (
    left.length > 0 && left === right && (leftText ?? "") !== (rightText ?? "")
  );
};

const buildInlineDiffHtml = (
  inlineDiffCache: InlineDiffCache,
  leftText?: string,
  rightText?: string
) => {
  const oldText = leftText ?? "";
  const newText = rightText ?? "";
  const cacheKey = `${oldText}\u0000${newText}`;
  const cached = inlineDiffCache.get(cacheKey);
  if (cached) return cached;

  if (oldText === newText) {
    const same = {
      leftHtml: renderHtmlText(oldText),
      rightHtml: renderHtmlText(newText),
      isSame: true
    };
    inlineDiffCache.set(cacheKey, same);
    return same;
  }

  const oldChars = Array.from(oldText);
  const newChars = Array.from(newText);
  const minLength = Math.min(oldChars.length, newChars.length);

  let prefix = 0;
  while (prefix < minLength && oldChars[prefix] === newChars[prefix]) {
    prefix += 1;
  }

  let oldSuffix = oldChars.length - 1;
  let newSuffix = newChars.length - 1;
  while (
    oldSuffix >= prefix &&
    newSuffix >= prefix &&
    oldChars[oldSuffix] === newChars[newSuffix]
  ) {
    oldSuffix -= 1;
    newSuffix -= 1;
  }

  const oldPrefixText = oldChars.slice(0, prefix).join("");
  const oldMiddleText = oldChars.slice(prefix, oldSuffix + 1).join("");
  const oldSuffixText = oldChars.slice(oldSuffix + 1).join("");
  const newPrefixText = newChars.slice(0, prefix).join("");
  const newMiddleText = newChars.slice(prefix, newSuffix + 1).join("");
  const newSuffixText = newChars.slice(newSuffix + 1).join("");

  const result = {
    leftHtml:
      `${formatHtmlFragment(oldPrefixText)}` +
      `${oldMiddleText ? `<span class="inline-mark inline-mark-old">${formatHtmlFragment(oldMiddleText)}</span>` : ""}` +
      `${oldSuffixText ? formatHtmlFragment(oldSuffixText) : ""}`,
    rightHtml:
      `${formatHtmlFragment(newPrefixText)}` +
      `${newMiddleText ? `<span class="inline-mark inline-mark-new">${formatHtmlFragment(newMiddleText)}</span>` : ""}` +
      `${newSuffixText ? formatHtmlFragment(newSuffixText) : ""}`,
    isSame: false
  };

  if (!result.leftHtml) {
    result.leftHtml = `<span class="placeholder-text">（空）</span>`;
  }
  if (!result.rightHtml) {
    result.rightHtml = `<span class="placeholder-text">（空）</span>`;
  }

  inlineDiffCache.set(cacheKey, result);
  return result;
};

export function useScoreDetailDiff({
  item,
  topCandidates,
  inlineDiffCache
}: UseScoreDetailDiffOptions) {
  const comparisonRank = ref<number | null>(null);
  const diffViewMode = ref<ScoreDetailDiffViewMode>("raw");
  const rawOnlyDiff = ref(true);

  const comparisonOptions = computed(() =>
    topCandidates.value
      .filter(candidate => candidate.rank > 1)
      .map(candidate => ({
        label: `Top${candidate.rank}`,
        value: candidate.rank
      }))
  );

  const comparisonCandidate = computed(
    () =>
      topCandidates.value.find(
        candidate => candidate.rank === comparisonRank.value
      ) ??
      topCandidates.value[1] ??
      null
  );

  watch(
    () => topCandidates.value.map(candidate => candidate.rank).join(","),
    () => {
      const firstComparable = topCandidates.value.find(
        candidate => candidate.rank > 1
      );
      if (!firstComparable) {
        comparisonRank.value = null;
        return;
      }

      const exists = topCandidates.value.some(
        candidate =>
          candidate.rank === comparisonRank.value && candidate.rank > 1
      );
      comparisonRank.value = exists
        ? comparisonRank.value
        : firstComparable.rank;
    },
    { immediate: true }
  );

  const comparisonBaseRows = computed<ScoreDetailDiffRow[]>(() => {
    const first = topCandidates.value[0];
    const candidate = comparisonCandidate.value;
    if (!first || !candidate) return [];

    return [
      {
        key: "project",
        label: "项目",
        left: first.project,
        right: candidate.project
      },
      {
        key: "specification",
        label: "规格",
        left: first.specification,
        right: candidate.specification
      },
      {
        key: "acceptance",
        label: "验收标准",
        left: first.acceptance ?? "",
        right: candidate.acceptance ?? ""
      },
      {
        key: "remark",
        label: "备注",
        left: first.remark ?? "",
        right: candidate.remark ?? ""
      }
    ]
      .filter(row => row.left.length > 0 || row.right.length > 0)
      .map(row => {
        const diff = buildInlineDiffHtml(inlineDiffCache, row.left, row.right);
        return {
          key: row.key,
          label: row.label,
          leftHtml: diff.leftHtml,
          rightHtml: diff.rightHtml,
          isSame: diff.isSame,
          isRiskRelevant:
            !diff.isSame && !isHintOnlyTextDifference(row.left, row.right)
        };
      });
  });

  const comparisonRows = computed(() => comparisonBaseRows.value);

  const rawComparisonRows = computed(() => {
    if (!rawOnlyDiff.value) return comparisonBaseRows.value;
    return comparisonBaseRows.value.filter(row => !row.isSame);
  });

  const sourceBestRows = computed<ScoreDetailDiffRow[]>(() => {
    const currentItem = item.value;
    const bestMatch = currentItem?.bestMatch;
    if (!currentItem || !bestMatch) return [];

    return [
      {
        key: "project",
        label: "项目",
        left: currentItem.sourceProject,
        right: bestMatch.project
      },
      {
        key: "specification",
        label: "规格",
        left: currentItem.sourceSpecification,
        right: bestMatch.specification
      }
    ]
      .map(row => {
        const diff = buildInlineDiffHtml(inlineDiffCache, row.left, row.right);
        return {
          key: row.key,
          label: row.label,
          leftHtml: diff.leftHtml,
          rightHtml: diff.rightHtml,
          isSame: diff.isSame,
          isRiskRelevant:
            !diff.isSame && !isHintOnlyTextDifference(row.left, row.right)
        };
      })
      .filter(row => !row.isSame);
  });

  const isComparedCandidate = (candidate: MatchCandidateOption) =>
    candidate.rank > 1 && candidate.rank === comparisonCandidate.value?.rank;

  const handleSelectComparisonCandidate = (candidate: MatchCandidateOption) => {
    if (candidate.rank <= 1) return;
    comparisonRank.value = candidate.rank;
  };

  const isCandidateExpanded = (candidate: MatchCandidateOption) =>
    candidate.rank === 1 || isComparedCandidate(candidate);

  return {
    comparisonRank,
    diffViewMode,
    rawOnlyDiff,
    comparisonOptions,
    comparisonCandidate,
    comparisonRows,
    rawComparisonRows,
    sourceBestRows,
    isComparedCandidate,
    handleSelectComparisonCandidate,
    isCandidateExpanded
  };
}
