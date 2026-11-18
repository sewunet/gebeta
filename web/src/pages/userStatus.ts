import type { UserDto } from "../lib/types";
import type { Tone } from "../ui/fiori";

/** Invited (set up ahead, not signed in yet), Active or Inactive. */
export const userStatus = (u: UserDto): { key: "Invited" | "Active" | "Inactive"; tone: Tone } =>
  !u.isActive ? { key: "Inactive", tone: "negative" } : u.isInvited ? { key: "Invited", tone: "critical" } : { key: "Active", tone: "positive" };
