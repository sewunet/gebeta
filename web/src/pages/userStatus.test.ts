import { describe, expect, it } from "vitest";
import type { UserDto } from "../lib/types";
import { userStatus } from "./userStatus";

const user = (patch: Partial<UserDto>): UserDto => ({ id: "1", username: "u", isActive: true, roles: [], companyIds: [], ...patch });

describe("user status", () => {
  it("is invited until the first sign-in, and inactive wins over everything", () => {
    expect(userStatus(user({ isInvited: true })).key).toBe("Invited");
    expect(userStatus(user({})).key).toBe("Active");
    expect(userStatus(user({ isActive: false, isInvited: true })).key).toBe("Inactive");
  });
});
