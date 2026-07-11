export type AuthSessionEventType =
  | "session-established"
  | "session-ended"
  | "authorization-changed"
  | "session-invalidated";

export type AuthSessionEvent = {
  type: AuthSessionEventType;
  source: string;
  at: number;
};

const channelName = "acceptance-spec-auth-session";
const storageKey = `${channelName}:event`;
const source =
  globalThis.crypto?.randomUUID?.() ??
  `${Date.now()}-${Math.random().toString(36).slice(2)}`;

let channel: BroadcastChannel | undefined;
const listeners = new Set<(event: AuthSessionEvent) => void>();

function isSessionEvent(value: unknown): value is AuthSessionEvent {
  if (!value || typeof value !== "object") return false;
  const event = value as Partial<AuthSessionEvent>;
  return (
    typeof event.source === "string" &&
    typeof event.at === "number" &&
    [
      "session-established",
      "session-ended",
      "authorization-changed",
      "session-invalidated"
    ].includes(String(event.type))
  );
}

function receive(value: unknown) {
  if (!isSessionEvent(value) || value.source === source) return;
  listeners.forEach(listener => listener(value));
}

if (typeof window !== "undefined") {
  if (typeof BroadcastChannel !== "undefined") {
    channel = new BroadcastChannel(channelName);
    channel.addEventListener("message", event => receive(event.data));
  }

  window.addEventListener("storage", event => {
    if (event.key !== storageKey || !event.newValue) return;
    try {
      receive(JSON.parse(event.newValue));
    } catch {
      // Ignore malformed events from unrelated/older clients.
    }
  });
}

export function publishAuthSessionEvent(type: AuthSessionEventType) {
  const event: AuthSessionEvent = { type, source, at: Date.now() };
  channel?.postMessage(event);

  // Storage is a compatibility transport only. The payload intentionally
  // contains no token or user identity and is removed immediately.
  if (typeof window !== "undefined") {
    try {
      window.localStorage.setItem(storageKey, JSON.stringify(event));
      window.localStorage.removeItem(storageKey);
    } catch {
      // BroadcastChannel remains available when storage is disabled.
    }
  }
}

export function onAuthSessionEvent(
  listener: (event: AuthSessionEvent) => void
) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
