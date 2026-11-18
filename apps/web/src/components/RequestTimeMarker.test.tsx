import { describe, expect, it, vi } from "vitest";

const { connection } = vi.hoisted(() => ({ connection: vi.fn(async () => undefined) }));
vi.mock("next/server", () => ({ connection }));

import { RequestTimeMarker } from "./RequestTimeMarker";

describe("RequestTimeMarker", () => {
  it("waits for the request and renders nothing", async () => {
    expect(await RequestTimeMarker()).toBeNull();
    expect(connection).toHaveBeenCalledOnce();
  });
});
