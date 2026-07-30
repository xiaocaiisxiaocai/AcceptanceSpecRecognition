export const formatCustomerScope = (
  customerId: number | null | undefined,
  customerNameById: ReadonlyMap<number, string>
) => {
  if (customerId == null) return "全局";

  const customerName = customerNameById.get(customerId);
  return customerName
    ? `${customerName}（ID: ${customerId}）`
    : `未知客户（ID: ${customerId}）`;
};
