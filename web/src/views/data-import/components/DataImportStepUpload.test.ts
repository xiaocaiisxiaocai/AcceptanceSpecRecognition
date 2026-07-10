import { createSSRApp } from "vue";
import { renderToString } from "@vue/server-renderer";
import ElementPlus from "element-plus";
import { describe, expect, it, vi } from "vitest";
import DataImportStepUpload from "./DataImportStepUpload.vue";

vi.mock("./FileUpload.vue", () => ({
  default: { template: '<div class="file-upload-stub" />' }
}));
const bannerControl = vi.hoisted(() => ({
  clickRetry: undefined as (() => void) | undefined
}));
vi.mock("@/views/shared/SmartStructureSummaryBanner.vue", async () => {
  const { defineComponent, h } = await import("vue");
  return {
    default: defineComponent({
      props: { error: String },
      emits: ["retry"],
      setup(props, { emit }) {
        bannerControl.clickRetry = () => emit("retry");
        return () =>
          h(
            "button",
            { onClick: bannerControl.clickRetry },
            `智能结构识别失败 ${props.error} 重新识别`
          );
      }
    })
  };
});

describe("DataImportStepUpload", () => {
  it("智能识别失败并停留上传步骤时展示持久错误和重试入口", async () => {
    const app = createSSRApp(DataImportStepUpload, {
      canUploadSourceFile: false,
      canImportAny: false,
      uploadAccept: ".docx,.xlsx",
      uploadBlockedMessage: "无上传权限",
      modelValue: null,
      smartRecognitionError: "服务暂时不可用，请稍后重试"
    });
    app.use(ElementPlus);

    const html = await renderToString(app);

    expect(html).toContain("智能结构识别失败");
    expect(html).toContain("服务暂时不可用，请稍后重试");
    expect(html).toContain("重新识别");
  });

  it("点击重新识别按钮时向父组件触发 retry", async () => {
    const retry = vi.fn();
    const app = createSSRApp(DataImportStepUpload, {
      canUploadSourceFile: false,
      canImportAny: false,
      uploadAccept: ".docx,.xlsx",
      uploadBlockedMessage: "无上传权限",
      modelValue: null,
      smartRecognitionError: "识别失败",
      onRetry: retry
    });
    app.use(ElementPlus);
    await renderToString(app);

    expect(bannerControl.clickRetry).toBeTypeOf("function");
    bannerControl.clickRetry?.();
    expect(retry).toHaveBeenCalledOnce();
  });
});
