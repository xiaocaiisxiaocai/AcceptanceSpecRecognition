import { createSSRApp } from "vue";
import { renderToString } from "@vue/server-renderer";
import { describe, expect, it } from "vitest";
import DashboardSparkline from "./DashboardSparkline.vue";

const renderSparkline = async (props: {
  values: number[];
  labels: string[];
  loading?: boolean;
}) => {
  const app = createSSRApp(DashboardSparkline, props);
  app.directive("loading", () => undefined);
  return renderToString(app);
};

describe("DashboardSparkline", () => {
  it("renders an accessible native SVG trend without a chart runtime", async () => {
    const html = await renderSparkline({
      values: [0, 2, 1],
      labels: ["7月19日", "7月20日", "7月21日"]
    });

    expect(html).toContain('role="img"');
    expect(html).toContain("7月20日：2");
    expect(html).toContain("<svg");
    expect(html).toContain("sparkline-line");
  });

  it("announces an empty state instead of rendering misleading points", async () => {
    const html = await renderSparkline({ values: [], labels: [] });

    expect(html).toContain("所选周期暂无趋势数据");
    expect(html).toContain("暂无数据");
    expect(html).not.toContain("<svg");
  });
});
