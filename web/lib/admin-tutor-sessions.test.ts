// Run with: npm test
import { test } from "node:test";
import assert from "node:assert/strict";
import { api } from "./api.ts";

test("api.adminTutorSessions is a function", () => {
  assert.strictEqual(typeof api.adminTutorSessions, "function");
});

test("api.adminTutorSession is a function", () => {
  assert.strictEqual(typeof api.adminTutorSession, "function");
});

test("api.adminTutorSessions returns a Promise when called", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = () =>
    Promise.resolve(new Response(JSON.stringify({ success: true, data: [] })));
  try {
    const result = api.adminTutorSessions();
    assert.ok(result instanceof Promise);
    await result;
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("api.adminTutorSession returns a Promise when called", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = () =>
    Promise.resolve(new Response(JSON.stringify({ success: true, data: {} })));
  try {
    const result = api.adminTutorSession(
      "00000000-0000-0000-0000-000000000000"
    );
    assert.ok(result instanceof Promise);
    await result;
  } finally {
    globalThis.fetch = originalFetch;
  }
});
