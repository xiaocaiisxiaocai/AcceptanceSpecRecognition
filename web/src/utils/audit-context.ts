const auditClientIdKey = "audit-client-id";

function createRandomId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID().replace(/-/g, "");
  }
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;
}

export function getAuditClientId() {
  if (typeof window === "undefined") return "";
  const stored = window.localStorage.getItem(auditClientIdKey);
  if (stored) return stored;
  const generated = createRandomId();
  window.localStorage.setItem(auditClientIdKey, generated);
  return generated;
}

export function createAuditTraceId() {
  return createRandomId();
}

export function getCurrentFrontendRoute() {
  if (typeof window === "undefined") return "";
  const hashPath = window.location.hash.replace(/^#/, "");
  if (hashPath.startsWith("/")) return hashPath;
  return `${window.location.pathname}${window.location.search}`;
}
