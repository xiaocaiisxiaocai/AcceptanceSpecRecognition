import type { ApiResponse, PagedData } from "@/api/customer";

type LoadAllPagedItemsOptions<T, TKey> = {
  getKey: (item: T) => TKey;
  signal?: AbortSignal;
  pageSize?: number;
  maxPages?: number;
};

export async function loadAllPagedItems<T, TKey>(
  fetchPage: (
    page: number,
    pageSize: number,
    signal?: AbortSignal
  ) => Promise<ApiResponse<PagedData<T>>>,
  options: LoadAllPagedItemsOptions<T, TKey>
): Promise<T[]> {
  const pageSize = options.pageSize ?? 200;
  const maxPages = options.maxPages ?? 1000;
  const itemsByKey = new Map<TKey, T>();

  for (let page = 1; page <= maxPages; page += 1) {
    options.signal?.throwIfAborted();

    const response = await fetchPage(page, pageSize, options.signal);
    if (response.code !== 0) {
      throw new Error(response.message || "分页数据加载失败");
    }

    const data = response.data;
    if (!data || data.page !== page) {
      throw new Error(`分页响应页码与请求页码不一致：请求 ${page}`);
    }

    if (data.total === 0 && data.totalPages === 0 && data.items.length === 0) {
      return [];
    }

    if (data.totalPages < 1) {
      throw new Error(`分页总页数无效：${data.totalPages}`);
    }
    if (data.totalPages > maxPages) {
      throw new Error(`分页总页数超过最大页数 ${maxPages}`);
    }
    if (data.items.length === 0) {
      throw new Error(`分页第 ${page} 页为空，无法保证数据完整`);
    }

    for (const item of data.items) {
      const key = options.getKey(item);
      if (!itemsByKey.has(key)) {
        itemsByKey.set(key, item);
      }
    }

    if (page >= data.totalPages) {
      return [...itemsByKey.values()];
    }
  }

  throw new Error(`分页加载超过最大页数 ${maxPages}`);
}
