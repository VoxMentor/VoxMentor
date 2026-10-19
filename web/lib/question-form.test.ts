// Run with: npm test  (Node's built-in test runner — no test dependencies).
// Covers the pure logic behind the admin question form: the three parsers, the
// difficulty parser and the page-param parser. Everything else is React/DOM.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  isGuid,
  parseDifficulty,
  parsePageParam,
  splitEntries,
  validateRubricLines,
  validateTestCaseLines,
} from "./question-form.ts";

test("isGuid accepts only the hyphenated form the API binds", () => {
  assert.equal(isGuid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), true);
  assert.equal(isGuid("3F2504E0-4F89-11D3-9A0C-0305E82C3301"), true);
  assert.equal(isGuid("3f2504e04f8911d39a0c0305e82c3301"), false);
  assert.equal(isGuid(""), false);
  assert.equal(isGuid("not-a-guid"), false);
});

test("parseDifficulty accepts integers in range, ignoring surrounding space", () => {
  assert.equal(parseDifficulty("1"), 1);
  assert.equal(parseDifficulty("5"), 5);
  assert.equal(parseDifficulty(" 10 "), 10);
});

test("parseDifficulty rejects what the API would reject anyway", () => {
  assert.equal(parseDifficulty(""), null);
  assert.equal(parseDifficulty("abc"), null);
  assert.equal(parseDifficulty("0"), null);
  assert.equal(parseDifficulty("-1"), null);
  assert.equal(parseDifficulty("11"), null);
});

test("parseDifficulty never silently truncates what was typed", () => {
  // parseInt("7.5") is 7: the field says 7.5, the API would store 7.
  assert.equal(parseDifficulty("7.5"), null);
  // "1e1" is a legal value for <input type="number"> and means 10, not 1.
  assert.equal(parseDifficulty("1e1"), 10);
});

test("parsePageParam degrades to page 1 for anything the API cannot bind", () => {
  assert.equal(parsePageParam(null), 1);
  assert.equal(parsePageParam("2"), 2);
  assert.equal(parsePageParam("abc"), 1);
  assert.equal(parsePageParam("0"), 1);
  assert.equal(parsePageParam("-1"), 1);
  // Overflows the API's int32 binding, which answers 400 and strands the page.
  assert.equal(parsePageParam("3000000000"), 1);
  assert.equal(parsePageParam("999999999999999999999"), 1);
});

test("splitEntries drops blank lines and keeps one JSON entry per line", () => {
  assert.deepEqual(
    splitEntries('  \n {"input":"1","expected":"2"} \n\n'),
    ['{"input":"1","expected":"2"}']
  );
  assert.deepEqual(splitEntries(""), []);
  assert.deepEqual(splitEntries("   \n  "), []);
});

test("splitEntries accepts a pasted array, of objects or of strings", () => {
  assert.deepEqual(
    splitEntries('[{"input":"1","expected":"2"}]'),
    ['{"input":"1","expected":"2"}']
  );
  assert.deepEqual(splitEntries('["a", "b"]'), ["a", "b"]);
  // Not a JSON array: fall back to line splitting so validation can report it.
  assert.deepEqual(splitEntries('[{"input":"1"}'), ['[{"input":"1"}']);
});

test("validateTestCaseLines mirrors TestCasePayload.Parse", () => {
  assert.deepEqual(validateTestCaseLines(['{"input":"1","expected":"2"}']), []);
  assert.match(validateTestCaseLines(["nope"])[0], /not valid JSON/);
  assert.match(validateTestCaseLines(["42"])[0], /must be a JSON object/);
  assert.match(validateTestCaseLines(['{"input":1,"expected":"2"}'])[0], /string "input"/);
  assert.match(validateTestCaseLines(['{"input":"1"}'])[0], /string "expected"/);
  assert.match(
    validateTestCaseLines(['{"input":"1","expected":"2","hidden":"yes"}'])[0],
    /must be a boolean/
  );
});

test("validateTestCaseLines keeps hidden cases trailing", () => {
  const errors = validateTestCaseLines([
    '{"input":"1","expected":"2","hidden":true}',
    '{"input":"3","expected":"4"}',
  ]);
  assert.equal(errors.length, 1);
  assert.match(errors[0], /must be trailing/);
  assert.deepEqual(
    validateTestCaseLines([
      '{"input":"1","expected":"2"}',
      '{"input":"3","expected":"4","hidden":true}',
    ]),
    []
  );
});

test("validateRubricLines requires a nonempty criterion and numeric points", () => {
  assert.deepEqual(validateRubricLines(['{"criterion":"Handles overflow","points":2}']), []);
  assert.match(validateRubricLines(['{"criterion":"   ","points":2}'])[0], /criterion/);
  assert.match(validateRubricLines(['{"criterion":"ok","points":"2"}'])[0], /points/);
  assert.match(validateRubricLines(["]"])[0], /not valid JSON/);
});
