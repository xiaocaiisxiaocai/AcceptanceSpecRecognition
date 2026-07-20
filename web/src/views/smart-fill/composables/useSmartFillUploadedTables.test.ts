import { computed, ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { FileUploadResponse, TableInfo } from "@/api/document";

const apiMocks = vi.hoisted(() => ({
  getFileTables: vi.fn(),
  getEffectiveColumnMappingRules: vi.fn()
}));

vi.mock("@/api/document", () => ({ getFileTables: apiMocks.getFileTables }));
vi.mock("@/api/column-mapping-rules", () => ({
  getEffectiveColumnMappingRules: apiMocks.getEffectiveColumnMappingRules
}));
vi.mock("element-plus", () => ({
  ElMessage: { warning: vi.fn() }
}));

import { useSmartFillUploadedTables } from "./useSmartFillUploadedTables";

const file = (fileId: number, fileType = 1): FileUploadResponse => ({
  fileId,
  fileName: `${fileId}.${fileType === 1 ? "xlsx" : "docx"}`,
  fileType,
  fileHash: `hash-${fileId}`,
  isDuplicate: false,
  tableCount: 1,
  tableCountReady: true
});

const table = (index: number, name: string): TableInfo => ({
  index,
  name,
  rowCount: 10,
  columnCount: 4,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注"],
  hasMergedCells: false
});

const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => {
    resolve = done;
  });
  return { promise, resolve };
};

describe("useSmartFillUploadedTables", () => {
  beforeEach(() => {
    apiMocks.getFileTables.mockReset();
    apiMocks.getEffectiveColumnMappingRules.mockReset();
  });

  it("旧文件请求返回时不会提前解锁或覆盖新文件元数据", async () => {
    const first = deferred<any>();
    const second = deferred<any>();
    apiMocks.getFileTables.mockImplementation((fileId: number) =>
      fileId === 1 ? first.promise : second.promise
    );
    const uploadedFile = ref<FileUploadResponse | null>(file(1));
    const allTables = ref<TableInfo[]>([table(9, "旧缓存")]);
    const batchTableConfigs = ref<any[]>([]);
    const loading = ref(false);
    const state = useSmartFillUploadedTables({
      uploadedFile,
      isExcelFile: computed(() => uploadedFile.value?.fileType === 1),
      allTables,
      batchTableConfigs,
      wordColumnMappingRules: ref([]),
      loadingUploadedFileTables: loading,
      selectedCustomerId: ref(undefined)
    });

    const firstLoad = state.loadUploadedFileTables(file(1));
    expect(allTables.value).toEqual([]);
    uploadedFile.value = file(2);
    const secondLoad = state.loadUploadedFileTables(file(2));

    first.resolve({ code: 0, data: [table(0, "A")] });
    await firstLoad;
    expect(loading.value).toBe(true);
    expect(allTables.value).toEqual([]);

    second.resolve({ code: 0, data: [table(0, "B")] });
    await secondLoad;
    expect(loading.value).toBe(false);
    expect(allTables.value.map(item => item.name)).toEqual(["B"]);
  });

  it("识别失败后仍可从表元数据恢复手动配置", () => {
    const uploadedFile = ref<FileUploadResponse | null>(file(1));
    const allTables = ref<TableInfo[]>([table(0, "Sheet1")]);
    const batchTableConfigs = ref<any[]>([]);
    const state = useSmartFillUploadedTables({
      uploadedFile,
      isExcelFile: computed(() => true),
      allTables,
      batchTableConfigs,
      wordColumnMappingRules: ref([]),
      loadingUploadedFileTables: ref(false),
      selectedCustomerId: ref(1)
    });

    expect(state.ensureManualTableConfigs()).toBe(true);
    expect(batchTableConfigs.value).toEqual([
      expect.objectContaining({ tableIndex: 0, selected: true })
    ]);
  });

  it("快速切客户时忽略较晚返回的旧客户 Word 规则", async () => {
    const customerA = deferred<any>();
    const customerB = deferred<any>();
    apiMocks.getEffectiveColumnMappingRules.mockImplementation(
      (customerId?: number) =>
        customerId === 1 ? customerA.promise : customerB.promise
    );
    const uploadedFile = ref<FileUploadResponse | null>(file(1, 2));
    const selectedCustomerId = ref<number | undefined>(1);
    const rules = ref<any[]>([]);
    const state = useSmartFillUploadedTables({
      uploadedFile,
      isExcelFile: computed(() => false),
      allTables: ref([table(0, "Word")]),
      batchTableConfigs: ref([]),
      wordColumnMappingRules: rules,
      loadingUploadedFileTables: ref(false),
      selectedCustomerId
    });

    const firstLoad = state.reloadWordColumnMappingRulesForCustomer();
    selectedCustomerId.value = 2;
    const secondLoad = state.reloadWordColumnMappingRulesForCustomer();
    customerB.resolve({ code: 0, data: [{ id: 2, pattern: "B" }] });
    await secondLoad;
    customerA.resolve({ code: 0, data: [{ id: 1, pattern: "A" }] });
    await firstLoad;

    expect(rules.value).toEqual([{ id: 2, pattern: "B" }]);
  });
});
