/** Only an explicit 401 from the refresh endpoint proves the browser session is invalid. */
export function isRefreshSessionInvalidError(error: unknown): boolean {
  if (typeof error !== "object" || error === null || !("response" in error)) {
    return false;
  }

  const response = error.response;
  return (
    typeof response === "object" &&
    response !== null &&
    "status" in response &&
    response.status === 401
  );
}
