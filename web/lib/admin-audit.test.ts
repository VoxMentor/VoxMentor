// Run with: npm test  (Node's built-in test runner — no test dependencies).
// Covers the adminAuditLogs API helper: export verification.
import { test } from "node:test";
import assert from "node:assert/strict";
import { api } from "./api.ts";

test("adminAuditLogs is exported on api object", () => {
  assert.equal(typeof api.adminAuditLogs, "function");
  // Function signature verified; actual fetch tested via integration tests.
});