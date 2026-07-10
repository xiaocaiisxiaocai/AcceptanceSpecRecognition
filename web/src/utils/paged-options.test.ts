import { describe, expect, it, vi } from "vitest";
import type { ApiResponse, PagedData } from "@/api/customer";
import { loadAllPagedItems } from "./paged-options";

type Item = {
  id: number;
  name: string;
};

function pageResponse(
  page: number,
  items: Item[],
  total: number,
  totalPages: number
): ApiResponse<PagedData<Item>> {
  return {
    code: 0,
    message: "ok",
    data: {
      items,
      total,
      page,
      pageSize: 200,
      totalPages,
      hasNext: page < totalPages,
      hasPrevious: page > 1
    }
  };
}

describe("loadAllPagedItems", () => {
  it("按 200 条分页加载 251 条数据并保持顺序", async () => {
    const items = Array.from({ length: 251 }, (_, index) => ({
      id: index + 1,
      name: `item-${index + 1}`
    }));
    const requestedPages: number[] = [];

    const result = await loadAllPagedItems(
      async (page, pageSize) => {
        requestedPages.push(page);
        const start = (page - 1) * pageSize;
        return pageResponse(page, items.slice(start, start + pageSize), 251, 2);
      },
      { getKey: item => item.id }
    );

    expect(requestedPages).toEqual([1, 2]);
    expect(result.map(item => item.id)).toEqual(
      Array.from({ length: 251 }, (_, index) => index + 1)
    );
  });

  it("重复 ID 保留第一次出现的位置和值", async () => {
    const result = await loadAllPagedItems(
      async page =>
        page === 1
          ? pageResponse(1, [{ id: 1, name: "first" }], 2, 2)
          : pageResponse(
              2,
              [
                { id: 1, name: "duplicate" },
                { id: 2, name: "second" }
              ],
              2,
              2
            ),
      { getKey: item => item.id }
    );

    expect(result).toEqual([
      { id: 1, name: "first" },
      { id: 2, name: "second" }
    ]);
  });

  it("接受后端合法空分页", async () => {
    const result = await loadAllPagedItems(
      async () => pageResponse(1, [], 0, 0),
      { getKey: item => item.id }
    );

    expect(result).toEqual([]);
  });

  it.each([
    {
      name: "非首页空页",
      fetchPage: async (page: number) =>
        page === 1
          ? pageResponse(1, [{ id: 1, name: "first" }], 2, 2)
          : pageResponse(2, [], 2, 2),
      message: "第 2 页为空"
    },
    {
      name: "响应页码不一致",
      fetchPage: async () => pageResponse(2, [{ id: 1, name: "first" }], 1, 1),
      message: "响应页码"
    },
    {
      name: "超过最大页数",
      fetchPage: async () =>
        pageResponse(1, [{ id: 1, name: "first" }], 1, 1001),
      message: "最大页数"
    }
  ])("拒绝异常分页：$name", async ({ fetchPage, message }) => {
    await expect(
      loadAllPagedItems(fetchPage, { getKey: item => item.id })
    ).rejects.toThrow(message);
  });

  it("业务失败时不返回部分数据", async () => {
    await expect(
      loadAllPagedItems(
        async () => ({ code: 500, message: "分页服务失败", data: null! }),
        { getKey: (item: Item) => item.id }
      )
    ).rejects.toThrow("分页服务失败");
  });

  it("信号已取消时不发起请求", async () => {
    const controller = new AbortController();
    const fetchPage = vi.fn();
    controller.abort();

    await expect(
      loadAllPagedItems(fetchPage, {
        getKey: (item: Item) => item.id,
        signal: controller.signal
      })
    ).rejects.toMatchObject({ name: "AbortError" });
    expect(fetchPage).not.toHaveBeenCalled();
  });
});
