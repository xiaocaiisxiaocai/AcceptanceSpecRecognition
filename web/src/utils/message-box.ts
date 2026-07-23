export function isMessageBoxCancel(error: unknown): boolean {
  return error === "cancel" || error === "close";
}
