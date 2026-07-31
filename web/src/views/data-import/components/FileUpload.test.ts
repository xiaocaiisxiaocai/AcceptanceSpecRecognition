import { createSSRApp, defineComponent, h } from "vue";
import { renderToString } from "@vue/server-renderer";
import { describe, expect, it, vi } from "vitest";

const apiMocks = vi.hoisted(() => ({
  uploadFile: vi.fn(),
  deleteFile: vi.fn()
}));
const orgApiMocks = vi.hoisted(() => ({
  getBusinessOrgContext: vi.fn()
}));

vi.mock("@/api/document", () => apiMocks);
vi.mock("@/api/org-unit", () => orgApiMocks);
vi.mock("element-plus", () => ({
  ElMessage: {
    error: vi.fn(),
    success: vi.fn()
  }
}));

import FileUpload from "./FileUpload.vue";

describe("FileUpload", () => {
  it("已上传文件使用移出当前流程文案", async () => {
    const { html } = await mountUploadedFile();

    expect(html).toContain("移出当前流程");
    expect(html).not.toContain(">删除<");
  });

  it("点击移出当前流程只清空当前模型且不删除服务端文件", async () => {
    const updates: unknown[] = [];
    const mounted = await mountUploadedFile(value => updates.push(value));

    expect(mounted.clickAction).toBeTypeOf("function");
    mounted.clickAction?.();

    expect(updates).toEqual([null]);
    expect(apiMocks.deleteFile).not.toHaveBeenCalled();
  });
});

async function mountUploadedFile(onUpdate?: (value: unknown) => void) {
  let clickAction: (() => void) | undefined;
  const ButtonStub = defineComponent({
    setup(_, { attrs, slots }) {
      clickAction = attrs.onClick as (() => void) | undefined;
      return () => h("button", slots.default?.());
    }
  });
  const passthrough = defineComponent({
    setup(_, { slots }) {
      return () => h("div", slots.default?.());
    }
  });
  const app = createSSRApp(FileUpload, {
    modelValue: {
      fileId: 7,
      fileName: "验收规格.xlsx",
      fileType: 1,
      fileHash: "hash",
      isDuplicate: false,
      tableCount: 1,
      tableCountReady: true,
      ownerOrgUnitId: 8,
      ownerOrgUnitName: "质量部",
      tableMetadataStatus: "ready"
    },
    "onUpdate:modelValue": onUpdate
  });
  app.component("el-button", ButtonStub);
  app.component("el-card", passthrough);
  app.component("el-icon", passthrough);

  const html = await renderToString(app);
  return { html, clickAction };
}
