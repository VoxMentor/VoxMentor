// Run with: npm test  (Node's built-in test runner — no test dependencies).
// Covers the pure logic behind the admin corpus upload page: the file rules, the
// status/progress mapping and the server-error splitter. Everything else is React/DOM.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  CORPUS_MAX_BYTES,
  isTerminalStatus,
  progressPercent,
  splitUploadErrors,
  validateCorpusFile,
} from "./corpus-job.ts";

test("validateCorpusFile mirrors the server's extension rule", () => {
  assert.equal(validateCorpusFile({ name: "ctci.pdf", size: 10 }), null);
  assert.equal(validateCorpusFile({ name: "notes.TXT", size: 10 }), null);
  assert.match(String(validateCorpusFile({ name: "slides.pptx", size: 10 })), /pdf and .txt/);
  // no extension at all — Path.GetExtension would be "" server-side
  assert.match(String(validateCorpusFile({ name: "notes", size: 10 })), /pdf and .txt/);
  // a double extension must not slip past on the trailing .pdf
  assert.match(String(validateCorpusFile({ name: "book.pdf.exe", size: 10 })), /pdf and .txt/);
});

test("validateCorpusFile accepts the limit and rejects past it", () => {
  assert.equal(validateCorpusFile({ name: "book.pdf", size: CORPUS_MAX_BYTES }), null);
  assert.match(
    String(validateCorpusFile({ name: "book.pdf", size: CORPUS_MAX_BYTES + 1 })),
    /20 MB/
  );
  assert.match(String(validateCorpusFile({ name: "book.pdf", size: 0 })), /empty/);
});

test("only Completed and Failed stop the poll", () => {
  assert.equal(isTerminalStatus("Pending"), false);
  assert.equal(isTerminalStatus("Processing"), false);
  assert.equal(isTerminalStatus("Completed"), true);
  assert.equal(isTerminalStatus("Failed"), true);
});

test("progressPercent floors at 0 while the total is unknown and never exceeds 100", () => {
  assert.equal(progressPercent(0, 0), 0); // Pending: the worker has not counted yet
  assert.equal(progressPercent(0, 12), 0);
  assert.equal(progressPercent(240, 0), 0);
  assert.equal(progressPercent(240, 118), 49);
  assert.equal(progressPercent(240, 240), 100);
  assert.equal(progressPercent(10, 12), 100); // a raced read must not render 120%
  assert.equal(progressPercent(10, -3), 0);
});

test("splitUploadErrors keeps rendered fields out of the banner", () => {
  assert.deepEqual(
    splitUploadErrors({ FileName: ["Only .pdf and .txt files are supported."] }),
    {
      fileError: "Only .pdf and .txt files are supported.",
      conceptError: null,
      banner: null,
    }
  );
  // binding keys (file/conceptId) and ProblemDetails paths hit the same fields
  assert.equal(
    splitUploadErrors({ file: ["The file field is required."] }).fileError,
    "The file field is required."
  );
  assert.equal(
    splitUploadErrors({ "$.conceptId": ["The value is not valid."] }).conceptError,
    "The value is not valid."
  );
  // messages for one key are joined, not dropped
  assert.equal(
    splitUploadErrors({ Length: ["Too big.", "And empty."] }).fileError,
    "Too big. And empty."
  );
});

test("splitUploadErrors never drops a key the form cannot render", () => {
  const { fileError, conceptError, banner } = splitUploadErrors({
    Length: ["File must be 20971520 bytes or smaller."],
    command: ["Something else broke."],
  });
  assert.equal(fileError, "File must be 20971520 bytes or smaller.");
  assert.equal(conceptError, null);
  assert.equal(banner, "Something else broke.");
});
