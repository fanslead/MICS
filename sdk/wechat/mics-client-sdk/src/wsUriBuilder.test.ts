import { describe, expect, it } from "vitest";

import { buildWsUrl } from "./wsUriBuilder";

describe("buildWsUrl", () => {
  it("appends required query params", () => {
    const url = buildWsUrl("ws://localhost:8080/ws", "t1", "tok", "dev1");
    expect(url).toContain("tenantId=t1");
    expect(url).toContain("token=tok");
    expect(url).toContain("deviceId=dev1");
  });

  it("preserves existing query params", () => {
    const url = buildWsUrl("ws://localhost:8080/ws?x=1", "t1", "tok", "dev1");
    expect(url).toContain("x=1");
  });
});

