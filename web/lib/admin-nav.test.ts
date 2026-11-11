// Run with: npm test  (Node's built-in test runner - no test dependencies).
// Covers the admin role gates: hasAdminRole (single-role check) and
// visibleNavItems (which admin sections a user's roles unlock).
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  ADMIN_NAV,
  ADMIN_ROLES,
  hasAdminRole,
  visibleNavItems,
} from "./admin-nav.ts";

test("hasAdminRole accepts every admin role and rejects others", () => {
  for (const role of ADMIN_ROLES) {
    assert.equal(hasAdminRole([role]), true);
  }
  assert.equal(hasAdminRole(["Learner"]), false);
});

test("hasAdminRole is false when no roles are supplied", () => {
  assert.equal(hasAdminRole(), false);
  assert.equal(hasAdminRole([]), false);
});

test("hasAdminRole checks any role in the list", () => {
  assert.equal(hasAdminRole(["Learner", "ContentAdmin"]), true);
  assert.equal(hasAdminRole(["Learner", "Teacher"]), false);
});

test("visibleNavItems shows every section to a super admin", () => {
  const hrefs = visibleNavItems(["SuperAdmin"]).map((item) => item.href);
  assert.deepEqual(hrefs, ADMIN_NAV.map((item) => item.href));
});

test("visibleNavItems limits content admins to the content sections", () => {
  const hrefs = visibleNavItems(["ContentAdmin"]).map((item) => item.href);
  assert.deepEqual(hrefs, ["/admin", "/admin/questions", "/admin/corpus"]);
});

test("visibleNavItems limits platform admins to the platform sections", () => {
  const hrefs = visibleNavItems(["PlatformAdmin"]).map((item) => item.href);
  assert.deepEqual(hrefs, [
    "/admin",
    "/admin/audit",
    "/admin/plagiarism",
    "/admin/tutor-sessions",
  ]);
});

test("visibleNavItems returns nothing for a non-admin", () => {
  assert.deepEqual(visibleNavItems(["Learner"]), []);
  assert.deepEqual(visibleNavItems(), []);
});
