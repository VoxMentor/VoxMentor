// Run with: npm test  (Node's built-in test runner — no test dependencies).
// Covers the pure logic behind the admin question form: the three parsers, the
// difficulty parser and the page-param parser. Everything else is React/DOM.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  computeTotalPages,
  isGuid,
  parseDifficulty,
  parsePageParam,
  splitServerFieldErrors,
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

test("computeTotalPages floors at 1 and the redirect guard hits exactly past the last page", () => {
  assert.equal(computeTotalPages(0, 20), 1);
  assert.equal(computeTotalPages(20, 20), 1); // full page 1: page 2 is out of range
  assert.equal(computeTotalPages(21, 20), 2);
});

test("page past the last page is out of range; the last page and page 1 are not", () => {
  assert.equal(2 > computeTotalPages(20, 20), true);
  assert.equal(2 > computeTotalPages(21, 20), false);
  assert.equal(1 > computeTotalPages(0, 20), false); // page 1 must never self-redirect
});

test("splitServerFieldErrors routes known fields and drops nothing into silence", () => {
  // every known key maps to its field error, messages joined
  const result = splitServerFieldErrors({
    Title: ["Title is required."],
    ConceptId: ["not a valid GUID", "does not exist"],
  });
  assert.deepEqual(
    result.fieldErrors,
    { Title: "Title is required.", ConceptId: "not a valid GUID does not exist" }
  );
  assert.equal(result.unmatchedMessage, null);
});

test("splitServerFieldErrors surfaces server keys no form field can render", () => {
  // validator keys (QuestionType, VariantSeed), ProblemDetails JSON paths
  // ("$.difficulty"), envelope-less keys ("command") — none has a rendered
  // field; they must land in the banner or the admin submits blind.
  const result = splitServerFieldErrors({
    QuestionType: ["Question type is not supported."],
    "$.difficulty": ["The difficulty value is invalid."],
    command: ["Unable to process the request."],
  });
  assert.deepEqual(result.fieldErrors, {});
  assert.equal(
    result.unmatchedMessage,
    "Question type is not supported. The difficulty value is invalid. Unable to process the request."
  );
});

test("splitServerFieldErrors matches camelCase server keys to their canonical field", () => {
  const result = splitServerFieldErrors({ title: ["Title is required."], description: ["Too long."] });
  assert.deepEqual(
    result.fieldErrors,
    { Title: "Title is required.", Description: "Too long." }
  );
  assert.equal(result.unmatchedMessage, null);
});

test("splitServerFieldErrors splits a mixed response between fields and banner", () => {
  const result = splitServerFieldErrors({
    TestCases: ["Test case is not valid JSON."],
    VariantSeed: ["Seed is out of range."],
  });
  assert.deepEqual(result.fieldErrors, { TestCases: "Test case is not valid JSON." });
  assert.equal(result.unmatchedMessage, "Seed is out of range.");
});

test("splitServerFieldErrors degrades an empty error map to no output at all", () => {
  const result = splitServerFieldErrors({});
  assert.deepEqual(result.fieldErrors, {});
  assert.equal(result.unmatchedMessage, null);
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
