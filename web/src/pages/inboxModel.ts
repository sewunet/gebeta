import type { Tone } from "../ui/fiori";

/** The tone a notification's severity is shown in. */
export const severityTone = (severity: string): Tone =>
  severity === "Critical" ? "negative" : severity === "Warning" ? "critical" : severity === "Info" ? "information" : "neutral";

/** The bell shows at most this many; more reads "99+". */
export const unreadLabel = (count: number) => (count > 99 ? "99+" : String(count));
