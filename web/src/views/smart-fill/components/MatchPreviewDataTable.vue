<script setup lang="ts">
import type { MatchPreviewItem } from "@/api/matching";
import type { MatchPreviewSelection } from "./matchPreviewTable.types";
import type { SmartFillReviewStatus } from "./scoreDetail.formatters";
import MatchPreviewBestMatchCell from "./MatchPreviewBestMatchCell.vue";
import MatchPreviewReasonCell from "./MatchPreviewReasonCell.vue";
import MatchPreviewTextCell from "./MatchPreviewTextCell.vue";

defineProps<{
  items: MatchPreviewItem[];
  loading?: boolean;
  hasReasonColumn: boolean;
  currentPage: number;
  pageSize: number;
  pageSizeOptions: number[];
  total: number;
  ambiguityMargin: number;
  getConfidenceClass: (level: string) => string;
  getConfidenceText: (level: string) => string;
  shouldShowFillRecommendation: (item: MatchPreviewItem) => boolean;
  getFillRecommendationTagType: (
    item: MatchPreviewItem
  ) => "success" | "warning" | "danger" | "info";
  getFillRecommendationText: (item: MatchPreviewItem) => string;
  getReviewStatus: (item: MatchPreviewItem) => SmartFillReviewStatus;
  getReviewTagType: (
    item: MatchPreviewItem
  ) => "success" | "warning" | "danger" | "info";
  getReviewStatusText: (item: MatchPreviewItem) => string;
  getDisplayAcceptanceText: (item: MatchPreviewItem) => string;
  getDisplayRemarkText: (item: MatchPreviewItem) => string;
  hasAcceptanceOverride: (item: MatchPreviewItem) => boolean;
  hasRemarkOverride: (item: MatchPreviewItem) => boolean;
  getSelection: (rowIndex: number) => MatchPreviewSelection | null | undefined;
  canEditRow: (item: MatchPreviewItem) => boolean;
  canUseBestMatch: (item: MatchPreviewItem) => boolean;
  getConfirmBestMatchButtonText: (item: MatchPreviewItem) => string;
}>();

defineEmits<{
  (e: "showDetail", item: MatchPreviewItem): void;
  (e: "edit", item: MatchPreviewItem): void;
  (e: "selectBest", item: MatchPreviewItem): void;
  (e: "clearSelection", item: MatchPreviewItem): void;
  (e: "update:currentPage", value: number): void;
  (e: "update:pageSize", value: number): void;
  (e: "pageSizeChange", value: number): void;
}>();
</script>

<template>
  <el-table
    :data="items"
    v-loading="loading"
    stripe
    border
    max-height="500"
    row-key="rowIndex"
  >
    <!-- 行号 -->
    <el-table-column label="行" width="60" align="center">
      <template #default="{ row }">
        {{ row.rowIndex + 1 }}
      </template>
    </el-table-column>

    <!-- 源数据 -->
    <el-table-column label="源数据" min-width="200">
      <template #default="{ row }">
        <div class="source-data">
          <div class="source-project">{{ row.sourceProject }}</div>
          <div class="source-spec">{{ row.sourceSpecification }}</div>
        </div>
      </template>
    </el-table-column>

    <!-- 置信度 -->
    <el-table-column label="置信度" width="80" align="center">
      <template #default="{ row }">
        <el-tag
          :class="getConfidenceClass(row.confidenceLevel)"
          size="small"
          effect="dark"
        >
          {{ getConfidenceText(row.confidenceLevel) }}
        </el-tag>
      </template>
    </el-table-column>

    <!-- 填充建议 -->
    <el-table-column label="填充建议" width="120" align="center">
      <template #default="{ row }">
        <el-tag
          v-if="shouldShowFillRecommendation(row)"
          size="small"
          :type="getFillRecommendationTagType(row)"
        >
          {{ getFillRecommendationText(row) }}
        </el-tag>
        <span v-else class="reason-none">-</span>
      </template>
    </el-table-column>

    <!-- 最佳匹配 -->
    <el-table-column label="最佳匹配" min-width="260">
      <template #default="{ row }">
        <MatchPreviewBestMatchCell
          :item="row"
          :ambiguity-margin="ambiguityMargin"
        />
      </template>
    </el-table-column>

    <!-- 复核状态 -->
    <el-table-column label="复核状态" width="130" align="center">
      <template #default="{ row }">
        <div class="ai-status-cell">
          <el-tag
            v-if="getReviewStatus(row) !== 'none'"
            size="small"
            :type="getReviewTagType(row)"
            :class="{ 'ai-streaming': getReviewStatus(row) === 'streaming' }"
          >
            {{ getReviewStatusText(row) }}
          </el-tag>
          <span v-else class="reason-none">-</span>
        </div>
      </template>
    </el-table-column>

    <!-- 验收标准预览 -->
    <el-table-column label="验收标准" min-width="180">
      <template #default="{ row }">
        <MatchPreviewTextCell
          :text="getDisplayAcceptanceText(row)"
          :edited="hasAcceptanceOverride(row)"
          :selection="getSelection(row.rowIndex)"
        />
      </template>
    </el-table-column>

    <!-- 备注预览 -->
    <el-table-column label="备注" min-width="150">
      <template #default="{ row }">
        <MatchPreviewTextCell
          :text="getDisplayRemarkText(row)"
          :edited="hasRemarkOverride(row)"
          :selection="getSelection(row.rowIndex)"
        />
      </template>
    </el-table-column>

    <!-- 不匹配原因 / 复核说明 -->
    <el-table-column v-if="hasReasonColumn" label="异常/原因" min-width="220">
      <template #default="{ row }">
        <MatchPreviewReasonCell :item="row" />
      </template>
    </el-table-column>

    <!-- 操作 -->
    <el-table-column label="操作" width="140" align="center" fixed="right">
      <template #default="{ row }">
        <div class="action-buttons">
          <el-button
            v-if="row.bestMatch"
            type="primary"
            link
            size="small"
            @click="$emit('showDetail', row)"
          >
            详情
          </el-button>
          <el-button
            v-if="canEditRow(row)"
            link
            size="small"
            @click="$emit('edit', row)"
          >
            编辑
          </el-button>
          <el-button
            v-if="row.bestMatch && canUseBestMatch(row) && getSelection(row.rowIndex)?.type !== 'best'"
            size="small"
            @click="$emit('selectBest', row)"
          >
            {{ getConfirmBestMatchButtonText(row) }}
          </el-button>
          <el-button
            v-if="getSelection(row.rowIndex)"
            link
            size="small"
            @click="$emit('clearSelection', row)"
          >
            不填充
          </el-button>
        </div>
      </template>
    </el-table-column>
  </el-table>

  <div class="table-pagination">
    <el-pagination
      :current-page="currentPage"
      :page-size="pageSize"
      background
      small
      :page-sizes="pageSizeOptions"
      :total="total"
      layout="total, sizes, prev, pager, next"
      @update:current-page="$emit('update:currentPage', $event)"
      @update:page-size="$emit('update:pageSize', $event)"
      @size-change="$emit('pageSizeChange', $event)"
    />
  </div>
</template>
