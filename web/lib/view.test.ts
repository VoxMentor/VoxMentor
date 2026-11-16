// Run with: npm test
// Which surface a dual-role account gets, and that the preference is per user.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  canSwitchView,
  defaultView,
  landingPath,
  loadView,
  resolveView,
  saveView,
} from "./view.ts";

function withStorage(run: (store: Map<string, string>) => void) {
  const store = new Map<string, string>();
  const original = (globalThis as { window?: unknown }).window;
  (globalThis as { window?: unknown }).window = {
    localStorage: {
      getItem: (key: string) => store.get(key) ?? null,
      setItem: (key: string, value: string) => void store.set(key, value),
      removeItem: (key: string) => void store.delete(key),
    },
  };
  try {
    run(store);
  } finally {
    (globalThis as { window?: unknown }).window = original;
  }
}

test("admin-only accounts are forced into admin view", () => {
  assert.equal(defaultView(["SuperAdmin"]), "admin");
  assert.equal(defaultView(["Student", "SuperAdmin"]), "student");
  assert.equal(defaultView(["Student"]), "student");
  assert.equal(defaultView(undefined), "student");
});

test("only a student who is also an admin gets the switch", () => {
  assert.equal(canSwitchView(["Student", "ContentAdmin"]), true);
  assert.equal(canSwitchView(["Student"]), false);
  assert.equal(canSwitchView(["PlatformAdmin"]), false);
  assert.equal(canSwitchView(undefined), false);
});

test("a saved view applies only while the account still has a choice", () => {
  assert.equal(resolveView("admin", ["Student", "SuperAdmin"]), "admin");
  assert.equal(resolveView("student", ["Student", "SuperAdmin"]), "student");
  assert.equal(resolveView(null, ["Student", "SuperAdmin"]), "student");
  assert.equal(resolveView("student", ["SuperAdmin"]), "admin");
  assert.equal(resolveView("admin", ["Student"]), "student");
});

test("each view has one landing route", () => {
  assert.equal(landingPath("student"), "/dashboard");
  assert.equal(landingPath("admin"), "/admin");
});

test("saved views are stored per user and can be cleared", () => {
  withStorage((store) => {
    saveView("u1", "admin");
    assert.equal(loadView("u1"), "admin");
    assert.equal(loadView("u2"), null);

    saveView("u1", null);
    assert.equal(loadView("u1"), null);
    assert.equal(store.size, 0);
  });
});

test("junk in storage or missing storage falls back to no preference", () => {
  withStorage((store) => {
    store.set("voxmentor:view:u1", "teacher");
    assert.equal(loadView("u1"), null);
    assert.equal(loadView(null), null);
  });

  assert.equal(loadView("u1"), null);
  saveView("u1", "admin");
  assert.equal(loadView("u1"), null);
});
