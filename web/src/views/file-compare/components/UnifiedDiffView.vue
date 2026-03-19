<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type {
  FileCompareDiffItem,
  FileCompareHunk,
  FileCompareHunkLine
} from "@/api/file-compare";

/**
 * 一行对照数据：左侧（文件A）和右侧（文件B）对齐
 * - context：两侧内容相同
 * - modified：左侧旧值、右侧新值（同一行对齐）
 * - removed：仅左侧有内容
 * - added：仅右侧有内容
 */
type DiffRow = {
  type: "context" | "modified" | "removed" | "added";
  left: FileCompareHunkLine | null;
  right: FileCompareHunkLine | null;
};

type RenderHunk = {
  hunkKey: string;
  rangeText?: string;
  startItemIndex: number;
  endItemIndex: number;
  rows: DiffRow[];
};

const props = defineProps<{
  hunks: FileCompareHunk[];
  items: FileCompareDiffItem[];
  onlyDiff: boolean;
}>();

const inlineDiffCache = new Map<string, { oldHtml: string; newHtml: string }>();

/** 记录哪些 hunk 间隙已展开，key = `gap-{hunkIdx}` */
const expandedGaps = ref<Record<string, boolean>>({});

watch(
  () => [props.hunks, props.items],
  () => {
    inlineDiffCache.clear();
    expandedGaps.value = {};
  }
);

/* ─── hunk 构建（后端无 hunks 时的 fallback） ─── */

const buildFallbackHunks = (items: FileCompareDiffItem[], contextLineCount = 2): FileCompareHunk[] => {
  const changedIndices = items
    .map((item, index) => ({ item, index }))
    .filter((x) => x.item.diffType !== "Unchanged")
    .map((x) => x.index);

  if (!changedIndices.length) return [];

  const ranges: Array<{ start: number; end: number }> = [];
  changedIndices.forEach((index) => {
    const start = Math.max(0, index - contextLineCount);
    const end = Math.min(items.length - 1, index + contextLineCount);
    const last = ranges[ranges.length - 1];
    if (!last || start > last.end + 1) {
      ranges.push({ start, end });
      return;
    }
    last.end = Math.max(last.end, end);
  });

  return ranges.map((range) => {
    const lines: FileCompareHunkLine[] = [];
    for (let i = range.start; i <= range.end; i += 1) {
      const item = items[i];
      if (item.diffType === "Modified") {
        const groupId = `m-${i + 1}`;
        lines.push({
          lineType: "Remove",
          itemIndex: i + 1,
          changeGroupId: groupId,
          displayLocation: item.displayLocation,
          originalText: item.originalText
        });
        lines.push({
          lineType: "Add",
          itemIndex: i + 1,
          changeGroupId: groupId,
          displayLocation: item.displayLocation,
          currentText: item.currentText
        });
        continue;
      }

      lines.push({
        lineType:
          item.diffType === "Added"
            ? "Add"
            : item.diffType === "Removed"
              ? "Remove"
              : "Context",
        itemIndex: i + 1,
        displayLocation: item.displayLocation,
        originalText: item.originalText,
        currentText: item.currentText
      });
    }

    const first = items[range.start]?.displayLocation || `第${range.start + 1}项`;
    const last = items[range.end]?.displayLocation || `第${range.end + 1}项`;

    return {
      startItemIndex: range.start + 1,
      endItemIndex: range.end + 1,
      rangeText: first === last ? first : `${first} ~ ${last}`,
      lines
    };
  });
};

const sourceHunks = computed(() =>
  props.hunks && props.hunks.length ? props.hunks : buildFallbackHunks(props.items)
);

/* ─── 将 hunk lines 转换为左右对齐的 DiffRow ─── */

const linesToRows = (lines: FileCompareHunkLine[]): DiffRow[] => {
  const rows: DiffRow[] = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];

    if (line.lineType === "Context") {
      rows.push({ type: "context", left: line, right: line });
      i += 1;
      continue;
    }

    /* 有 changeGroupId 的 Remove + Add 配对 → modified 行 */
    if (line.lineType === "Remove" && line.changeGroupId) {
      const addLine = lines[i + 1];
      if (addLine && addLine.lineType === "Add" && addLine.changeGroupId === line.changeGroupId) {
        rows.push({ type: "modified", left: line, right: addLine });
        i += 2;
        continue;
      }
    }

    /* 纯删除 */
    if (line.lineType === "Remove") {
      rows.push({ type: "removed", left: line, right: null });
      i += 1;
      continue;
    }

    /* 纯新增 */
    if (line.lineType === "Add") {
      rows.push({ type: "added", left: null, right: line });
      i += 1;
      continue;
    }

    i += 1;
  }
  return rows;
};

/* ─── 构建渲染用 hunks（含 onlyDiff 过滤） ─── */

const renderHunks = computed<RenderHunk[]>(() => {
  return sourceHunks.value
    .map((hunk, index) => {
      const hunkKey = `${hunk.startItemIndex}-${hunk.endItemIndex}-${index}`;
      const filteredLines = hunk.lines.filter((line) => {
        if (props.onlyDiff && line.lineType === "Context") return false;
        return true;
      });
      const rows = linesToRows(filteredLines);
      return {
        hunkKey,
        rangeText: hunk.rangeText,
        startItemIndex: hunk.startItemIndex,
        endItemIndex: hunk.endItemIndex,
        rows
      };
    })
    .filter((hunk) => hunk.rows.length > 0);
});

/* ─── 兼容模式（后端无 hunks 且 fallback 也无结果时） ─── */

const compatRows = computed<DiffRow[]>(() => {
  const lines: FileCompareHunkLine[] = [];
  props.items.forEach((item, index) => {
    if (props.onlyDiff && item.diffType === "Unchanged") return;

    if (item.diffType === "Modified") {
      const groupId = `compat-m-${index + 1}`;
      lines.push({
        lineType: "Remove",
        itemIndex: index + 1,
        changeGroupId: groupId,
        displayLocation: item.displayLocation,
        originalText: item.originalText
      });
      lines.push({
        lineType: "Add",
        itemIndex: index + 1,
        changeGroupId: groupId,
        displayLocation: item.displayLocation,
        currentText: item.currentText
      });
      return;
    }

    lines.push({
      lineType:
        item.diffType === "Added"
          ? "Add"
          : item.diffType === "Removed"
            ? "Remove"
            : "Context",
      itemIndex: index + 1,
      displayLocation: item.displayLocation,
      originalText: item.originalText,
      currentText: item.currentText
    });
  });
  return linesToRows(lines);
});

/** 计算两个相邻 hunk 之间省略的行数 */
const getSkippedLines = (currentHunk: RenderHunk, nextIndex: number): number => {
  if (nextIndex >= renderHunks.value.length) return 0;
  const nextHunk = renderHunks.value[nextIndex];
  const gap = nextHunk.startItemIndex - currentHunk.endItemIndex - 1;
  return gap > 0 ? gap : 0;
};

/* ─── 首尾间隙（第一个 hunk 之前 / 最后一个 hunk 之后） ─── */

/** 第一个 hunk 之前的省略行数 */
const leadingGapCount = computed(() => {
  if (!renderHunks.value.length || !props.items.length) return 0;
  return renderHunks.value[0].startItemIndex - 1;
});

/** 最后一个 hunk 之后的省略行数 */
const trailingGapCount = computed(() => {
  if (!renderHunks.value.length || !props.items.length) return 0;
  const lastHunk = renderHunks.value[renderHunks.value.length - 1];
  return props.items.length - lastHunk.endItemIndex;
});

/** 切换间隙的展开/折叠状态 */
const toggleGap = (key: string) => {
  expandedGaps.value[key] = !expandedGaps.value[key];
};

const isGapExpanded = (key: string) => expandedGaps.value[key] === true;

/** 从 props.items 中按 1-based [from, to] 区间取 context DiffRow[] */
const buildGapRows = (from1: number, to1: number): DiffRow[] => {
  if (to1 < from1) return [];
  const rows: DiffRow[] = [];
  for (let idx = from1; idx <= to1; idx += 1) {
    const item = props.items[idx - 1];
    if (!item) continue;
    const line: FileCompareHunkLine = {
      lineType: "Context",
      itemIndex: idx,
      displayLocation: item.displayLocation,
      originalText: item.originalText,
      currentText: item.currentText
    };
    rows.push({ type: "context", left: line, right: line });
  }
  return rows;
};

/** 第一个 hunk 之前的间隙行 */
const getLeadingGapRows = (): DiffRow[] => {
  if (!renderHunks.value.length) return [];
  return buildGapRows(1, renderHunks.value[0].startItemIndex - 1);
};

/** 最后一个 hunk 之后的间隙行 */
const getTrailingGapRows = (): DiffRow[] => {
  if (!renderHunks.value.length) return [];
  const lastHunk = renderHunks.value[renderHunks.value.length - 1];
  return buildGapRows(lastHunk.endItemIndex + 1, props.items.length);
};

/** 两个相邻 hunk 之间的间隙行 */
const getGapRows = (currentHunk: RenderHunk, nextIndex: number): DiffRow[] => {
  if (nextIndex >= renderHunks.value.length) return [];
  const nextHunk = renderHunks.value[nextIndex];
  return buildGapRows(currentHunk.endItemIndex + 1, nextHunk.startItemIndex - 1);
};

/* ─── HTML 转义 & inline diff ─── */

const escapeHtml = (text: string) =>
  text
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");

const truncateMiddle = (text: string, keep = 240) => {
  if (text.length <= keep) return text;
  const side = Math.floor((keep - 1) / 2);
  return `${text.slice(0, side)}…${text.slice(text.length - side)}`;
};

/** 基于前后缀锚点的线性 inline diff（避免 O(n*m)） */
const buildInlineDiffHtml = (oldText: string, newText: string) => {
  const cacheKey = `${oldText}\u0000${newText}`;
  const cached = inlineDiffCache.get(cacheKey);
  if (cached) return cached;

  if (oldText === newText) {
    const same = { oldHtml: escapeHtml(oldText), newHtml: escapeHtml(newText) };
    inlineDiffCache.set(cacheKey, same);
    return same;
  }

  const oldChars = Array.from(oldText);
  const newChars = Array.from(newText);
  const minLen = Math.min(oldChars.length, newChars.length);
  let prefix = 0;
  while (prefix < minLen && oldChars[prefix] === newChars[prefix]) {
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

  const oldMarked = truncateMiddle(oldMiddleText);
  const newMarked = truncateMiddle(newMiddleText);
  const result = {
    oldHtml:
      `${escapeHtml(oldPrefixText)}` +
      `${oldMarked ? `<span class="inline-mark">${escapeHtml(oldMarked)}</span>` : ""}` +
      `${escapeHtml(oldSuffixText)}`,
    newHtml:
      `${escapeHtml(newPrefixText)}` +
      `${newMarked ? `<span class="inline-mark">${escapeHtml(newMarked)}</span>` : ""}` +
      `${escapeHtml(newSuffixText)}`
  };
  inlineDiffCache.set(cacheKey, result);
  return result;
};

/* ─── 渲染单元格内容 ─── */

const renderPlainText = (text: string | undefined) => {
  if (!text || text.trim().length === 0) {
    return `<span class="placeholder-text">（空白内容）</span>`;
  }
  return escapeHtml(text.replaceAll("\t", "    "));
};

/**
 * 渲染 modified 行的单侧内容（带 inline diff 高亮）
 */
const renderModifiedSide = (row: DiffRow, side: "left" | "right") => {
  const oldText = row.left?.originalText ?? "";
  const newText = row.right?.currentText ?? "";

  if (side === "left" && oldText.trim().length === 0) {
    return `<span class="placeholder-text">（空白内容，含不可见字符）</span>`;
  }
  if (side === "right" && newText.trim().length === 0) {
    return `<span class="placeholder-text">（空白内容，含不可见字符）</span>`;
  }

  const pair = buildInlineDiffHtml(oldText, newText);
  return side === "left" ? pair.oldHtml : pair.newHtml;
};

/**
 * 渲染左侧单元格 HTML
 */
const renderLeftHtml = (row: DiffRow) => {
  if (row.type === "context") {
    return renderPlainText(row.left?.originalText ?? row.left?.currentText);
  }
  if (row.type === "modified") {
    return renderModifiedSide(row, "left");
  }
  if (row.type === "removed") {
    return renderPlainText(row.left?.originalText);
  }
  /* added → 左侧空 */
  return "";
};

/**
 * 渲染右侧单元格 HTML
 */
const renderRightHtml = (row: DiffRow) => {
  if (row.type === "context") {
    return renderPlainText(row.right?.currentText ?? row.right?.originalText);
  }
  if (row.type === "modified") {
    return renderModifiedSide(row, "right");
  }
  if (row.type === "added") {
    return renderPlainText(row.right?.currentText);
  }
  /* removed → 右侧空 */
  return "";
};

const getLeftLocation = (row: DiffRow) => row.left?.displayLocation || "";
const getRightLocation = (row: DiffRow) => row.right?.displayLocation || "";

const getLeftCellClass = (row: DiffRow) => {
  if (row.type === "removed") return "cell cell-removed";
  if (row.type === "modified") return "cell cell-modified-old";
  if (row.type === "added") return "cell cell-empty";
  return "cell";
};

const getRightCellClass = (row: DiffRow) => {
  if (row.type === "added") return "cell cell-added";
  if (row.type === "modified") return "cell cell-modified-new";
  if (row.type === "removed") return "cell cell-empty";
  return "cell";
};
</script>

<template>
  <div class="side-by-side-diff">
    <el-empty
      v-if="!renderHunks.length && !compatRows.length"
      description="当前条件下无可显示差异"
    />

    <template v-else-if="renderHunks.length">
      <!-- 表头 -->
      <div class="diff-table-header">
        <div class="header-left">文件 A（原文件）</div>
        <div class="header-right">文件 B（新文件）</div>
      </div>

      <div class="diff-scroll-body">
        <!-- 首部间隙：第一个 hunk 之前的省略行 -->
        <template v-if="leadingGapCount > 0">
          <template v-if="isGapExpanded('gap-leading')">
            <div
              class="hunk-separator hunk-separator-clickable"
              @click="toggleGap('gap-leading')"
            >
              ▾ 收起前 {{ leadingGapCount }} 行上下文
            </div>
            <div
              v-for="(row, gapIdx) in getLeadingGapRows()"
              :key="`gap-leading-${gapIdx}`"
              class="diff-row"
            >
              <div :class="getLeftCellClass(row)">
                <span v-if="getLeftLocation(row)" class="cell-location">{{ getLeftLocation(row) }}</span>
                <span class="cell-content" v-html="renderLeftHtml(row)" />
              </div>
              <div :class="getRightCellClass(row)">
                <span v-if="getRightLocation(row)" class="cell-location">{{ getRightLocation(row) }}</span>
                <span class="cell-content" v-html="renderRightHtml(row)" />
              </div>
            </div>
          </template>
          <div
            v-else
            class="hunk-separator hunk-separator-clickable"
            @click="toggleGap('gap-leading')"
          >
            ▸ 展开前 {{ leadingGapCount }} 行上下文 ···
          </div>
        </template>

        <template v-for="(hunk, hunkIdx) in renderHunks" :key="hunk.hunkKey">
          <!-- hunk header -->
          <div class="hunk-header">
            @@ {{ hunk.rangeText || "差异块" }} @@
          </div>

          <!-- 对齐的行 -->
          <div
            v-for="(row, rowIdx) in hunk.rows"
            :key="`${hunk.hunkKey}-${rowIdx}`"
            class="diff-row"
          >
            <!-- 左侧（文件A） -->
            <div :class="getLeftCellClass(row)">
              <span v-if="getLeftLocation(row)" class="cell-location">{{ getLeftLocation(row) }}</span>
              <span class="cell-content" v-html="renderLeftHtml(row)" />
            </div>
            <!-- 右侧（文件B） -->
            <div :class="getRightCellClass(row)">
              <span v-if="getRightLocation(row)" class="cell-location">{{ getRightLocation(row) }}</span>
              <span class="cell-content" v-html="renderRightHtml(row)" />
            </div>
          </div>

          <!-- hunk 间省略分隔（可展开/折叠） -->
          <template v-if="hunkIdx < renderHunks.length - 1 && getSkippedLines(hunk, hunkIdx + 1) > 0">
            <!-- 展开后的间隙行 -->
            <template v-if="isGapExpanded(`gap-${hunkIdx}`)">
              <div
                class="hunk-separator hunk-separator-clickable"
                @click="toggleGap(`gap-${hunkIdx}`)"
              >
                ▾ 收起 {{ getSkippedLines(hunk, hunkIdx + 1) }} 行上下文
              </div>
              <div
                v-for="(row, gapIdx) in getGapRows(hunk, hunkIdx + 1)"
                :key="`gap-${hunkIdx}-${gapIdx}`"
                class="diff-row"
              >
                <div :class="getLeftCellClass(row)">
                  <span v-if="getLeftLocation(row)" class="cell-location">{{ getLeftLocation(row) }}</span>
                  <span class="cell-content" v-html="renderLeftHtml(row)" />
                </div>
                <div :class="getRightCellClass(row)">
                  <span v-if="getRightLocation(row)" class="cell-location">{{ getRightLocation(row) }}</span>
                  <span class="cell-content" v-html="renderRightHtml(row)" />
                </div>
              </div>
            </template>
            <!-- 折叠状态 -->
            <div
              v-else
              class="hunk-separator hunk-separator-clickable"
              @click="toggleGap(`gap-${hunkIdx}`)"
            >
              ▸ 展开 {{ getSkippedLines(hunk, hunkIdx + 1) }} 行上下文 ···
            </div>
          </template>
        </template>

        <!-- 尾部间隙：最后一个 hunk 之后的省略行 -->
        <template v-if="trailingGapCount > 0">
          <template v-if="isGapExpanded('gap-trailing')">
            <div
              class="hunk-separator hunk-separator-clickable"
              @click="toggleGap('gap-trailing')"
            >
              ▾ 收起后 {{ trailingGapCount }} 行上下文
            </div>
            <div
              v-for="(row, gapIdx) in getTrailingGapRows()"
              :key="`gap-trailing-${gapIdx}`"
              class="diff-row"
            >
              <div :class="getLeftCellClass(row)">
                <span v-if="getLeftLocation(row)" class="cell-location">{{ getLeftLocation(row) }}</span>
                <span class="cell-content" v-html="renderLeftHtml(row)" />
              </div>
              <div :class="getRightCellClass(row)">
                <span v-if="getRightLocation(row)" class="cell-location">{{ getRightLocation(row) }}</span>
                <span class="cell-content" v-html="renderRightHtml(row)" />
              </div>
            </div>
          </template>
          <div
            v-else
            class="hunk-separator hunk-separator-clickable"
            @click="toggleGap('gap-trailing')"
          >
            ▸ 展开后 {{ trailingGapCount }} 行上下文 ···
          </div>
        </template>
      </div>
    </template>

    <!-- 兼容模式 -->
    <template v-else>
      <div class="diff-table-header">
        <div class="header-left">文件 A（原文件）</div>
        <div class="header-right">文件 B（新文件）</div>
      </div>

      <div class="diff-scroll-body">
        <div class="hunk-header">@@ 差异列表 @@</div>
        <div
          v-for="(row, rowIdx) in compatRows"
          :key="`compat-${rowIdx}`"
          class="diff-row"
        >
          <div :class="getLeftCellClass(row)">
            <span v-if="getLeftLocation(row)" class="cell-location">{{ getLeftLocation(row) }}</span>
            <span class="cell-content" v-html="renderLeftHtml(row)" />
          </div>
          <div :class="getRightCellClass(row)">
            <span v-if="getRightLocation(row)" class="cell-location">{{ getRightLocation(row) }}</span>
            <span class="cell-content" v-html="renderRightHtml(row)" />
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ─── 外层容器 ─── */
.side-by-side-diff {
  border: 1px solid #d1d5db;
  border-radius: 6px;
  overflow: hidden;
  background: #fff;
  font-size: 13px;
  line-height: 1.6;
}

/* ─── 表头：左文件A / 右文件B ─── */
.diff-table-header {
  display: grid;
  grid-template-columns: 1fr 1fr;
  border-bottom: 2px solid #d1d5db;
  background: #f6f8fa;
  font-weight: 600;
  color: #24292e;
  user-select: none;
}

.header-left,
.header-right {
  padding: 8px 12px;
}

.header-left {
  border-right: 1px solid #d1d5db;
}

/* ─── 滚动区域 ─── */
.diff-scroll-body {
  max-height: 560px;
  overflow: auto;
}

/* ─── Hunk header ─── */
.hunk-header {
  background: #f1f8ff;
  border-bottom: 1px solid #d8e1e8;
  border-top: 1px solid #d8e1e8;
  color: #0366d6;
  font-weight: 600;
  font-family: Consolas, "Courier New", monospace;
  padding: 3px 12px;
  user-select: none;
}

.hunk-header:first-child {
  border-top: none;
}

/* ─── Hunk 间省略分隔 ─── */
.hunk-separator {
  background: #f6f8fa;
  border-bottom: 1px solid #d8e1e8;
  border-top: 1px solid #d8e1e8;
  color: #6a737d;
  font-size: 12px;
  padding: 2px 12px;
  text-align: center;
  user-select: none;
}

.hunk-separator-clickable {
  cursor: pointer;
  transition: background 0.15s;
}

.hunk-separator-clickable:hover {
  background: #e1e8f0;
  color: #0366d6;
}

/* ─── 每一行：左右两栏等宽 ─── */
.diff-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  border-bottom: 1px solid #eaecef;
}

.diff-row:last-child {
  border-bottom: none;
}

/* ─── 单元格基础样式 ─── */
.cell {
  padding: 2px 10px;
  min-height: 24px;
  white-space: pre-wrap;
  word-break: break-all;
  font-family: "Microsoft YaHei", "PingFang SC", sans-serif;
}

/* 左侧单元格加右边框作为中线分隔 */
.diff-row > .cell:first-child {
  border-right: 1px solid #d1d5db;
}

/* ─── 单元格状态色 ─── */
.cell-removed {
  background: #ffeef0;
  color: #cb2431;
}

.cell-added {
  background: #e6ffec;
  color: #22863a;
}

.cell-modified-old {
  background: #ffeef0;
  color: #cb2431;
}

.cell-modified-new {
  background: #e6ffec;
  color: #22863a;
}

.cell-empty {
  background: #fafbfc;
  color: #959da5;
}

/* ─── 位置标签 ─── */
.cell-location {
  display: inline-block;
  font-size: 11px;
  color: #6a737d;
  background: rgba(27, 31, 35, 0.05);
  border-radius: 3px;
  padding: 0 4px;
  margin-right: 6px;
  vertical-align: baseline;
  font-family: Consolas, "Courier New", monospace;
}

/* ─── 内容区 ─── */
.cell-content {
  white-space: pre-wrap;
  word-break: break-all;
}

/* ─── inline diff 字符级高亮 ─── */
.cell-modified-old .cell-content :deep(.inline-mark) {
  background: #fdb8c0;
  border-radius: 2px;
  padding: 0 1px;
}

.cell-modified-new .cell-content :deep(.inline-mark) {
  background: #acf2bd;
  border-radius: 2px;
  padding: 0 1px;
}

.cell-content :deep(.inline-mark) {
  background: rgba(245, 158, 11, 0.25);
  border-radius: 2px;
  padding: 0 1px;
}

.cell-content :deep(.placeholder-text) {
  color: #959da5;
  font-style: italic;
}
</style>
