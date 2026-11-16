// Run with: npm test
// The 401 -> refresh -> replay path in lib/api.ts. Refreshing rotates the
// refresh token, so these tests mostly guard against firing two at once.
import { test } from "node:test";
import assert from "node:assert/strict";
import { api, ApiError } from "./api.ts";

const ok = (data: unknown) =>
  new Response(JSON.stringify({ success: true, data }), { status: 200 });

const unauthorized = () =>
  new Response(JSON.stringify({ success: false, message: "Unauthorized" }), {
    status: 401,
  });

function stubFetch(
  handler: (url: string) => Response | Promise<Response>
) {
  const calls: string[] = [];
  const original = globalThis.fetch;
  globalThis.fetch = (async (input: RequestInfo | URL) => {
    const url = String(input);
    calls.push(url);
    return handler(url);
  }) as typeof fetch;
  return {
    calls,
    restore: () => {
      globalThis.fetch = original;
    },
  };
}

test("an expired access token refreshes once and replays the request", async () => {
  let attempts = 0;
  const { calls, restore } = stubFetch((url) => {
    if (url.endsWith("/v1/auth/refresh")) return ok({});
    attempts += 1;
    return attempts === 1 ? unauthorized() : ok({ id: "u1" });
  });

  try {
    const me = await api.me();
    assert.equal(me.id, "u1");
    assert.equal(calls.filter((c) => c.endsWith("/v1/auth/refresh")).length, 1);
    assert.equal(calls.filter((c) => c.endsWith("/v1/auth/me")).length, 2);
  } finally {
    restore();
  }
});

test("concurrent 401s share one refresh instead of revoking each other", async () => {
  let attempts = 0;
  const { calls, restore } = stubFetch((url) => {
    if (url.endsWith("/v1/auth/refresh")) return ok({});
    attempts += 1;
    return attempts <= 3 ? unauthorized() : ok({ id: "u2" });
  });

  try {
    const results = await Promise.all([api.me(), api.me(), api.me()]);
    assert.deepEqual(
      results.map((r) => r.id),
      ["u2", "u2", "u2"]
    );
    assert.equal(calls.filter((c) => c.endsWith("/v1/auth/refresh")).length, 1);
  } finally {
    restore();
  }
});

test("a dead session replays once, then reports unauthorized", async () => {
  const { calls, restore } = stubFetch((url) =>
    url.endsWith("/v1/auth/refresh") ? unauthorized() : unauthorized()
  );

  try {
    await assert.rejects(
      () => api.me(),
      (error: unknown) => error instanceof ApiError && error.status === 401
    );
    assert.equal(calls.filter((c) => c.endsWith("/v1/auth/me")).length, 2);
  } finally {
    restore();
  }
});

test("a rejected login is reported without trying to refresh", async () => {
  const { calls, restore } = stubFetch(() => unauthorized());

  try {
    await assert.rejects(
      () => api.login({ email: "a@b.c", password: "wrong" }),
      (error: unknown) => error instanceof ApiError && error.status === 401
    );
    assert.equal(calls.filter((c) => c.endsWith("/v1/auth/refresh")).length, 0);
    assert.equal(calls.length, 1);
  } finally {
    restore();
  }
});
