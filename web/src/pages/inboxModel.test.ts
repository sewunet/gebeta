import { describe, expect, it } from "vitest";
import { severityTone } from "./inboxModel";

describe("severityTone", () => {
  it("maps severities to tones, and anything unknown to neutral", () => {
    expect([severityTone("Info"), severityTone("Warning"), severityTone("Critical"), severityTone("x")]).toEqual(["information", "critical", "negative", "neutral"]);
  });
});
