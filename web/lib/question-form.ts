// Pure logic behind the admin question form (#103). Lives outside the page so it
// can be run by Node's test runner — see web/lib/question-form.test.ts.
//
// The validators mirror the server (CreateQuestionValidator, TestCasePayload.Parse,
// RubricPayload.Parse). The server remains the authority; these only save a round
// trip and surface an error next to the field that caused it. Messages are 1-based
// so they line up with the lines in the textarea; the server's own messages number
// from 0.

export const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isGuid(value: string): boolean {
  return GUID_PATTERN.test(value);
}

// 20 questions per page * 100_000 pages is far past any real corpus, and it keeps
// `page` inside the int32 the API binds — a larger value answers 400 and would
// leave the page stuck on a retry of the same bad URL.
const MAX_PAGE = 100_000;

export function parsePageParam(raw: string | null): number {
  const n = Number.parseInt(raw ?? "1", 10);
  return Number.isInteger(n) && n > 1 && n <= MAX_PAGE ? n : 1;
}

// One definition for "how many pages there are" — used by both the pagination
// display and the out-of-range redirect guard in admin/questions page.
export function computeTotalPages(totalCount: number, pageSize: number): number {
  return Math.max(1, Math.ceil(totalCount / pageSize));
}

// Number(), not parseInt(): parseInt("7.5") is 7, which would silently create the
// question at a difficulty the admin never picked.
export function parseDifficulty(raw: string): number | null {
  const n = Number(raw);
  return Number.isInteger(n) && n >= 1 && n <= 10 ? n : null;
}

/**
 * One JSON entry per line, with two conveniences: blank lines are ignored and a
 * whole-array paste (`[{"input":...}, ...]`, objects or strings) is accepted and
 * normalized to the string entries the backend expects.
 */
export function splitEntries(text: string): string[] {
  const trimmed = text.trim();
  if (!trimmed) return [];
  if (trimmed.startsWith("[")) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(trimmed);
    } catch {
      parsed = null;
    }
    if (Array.isArray(parsed)) {
      return parsed.map((entry) => (typeof entry === "string" ? entry : JSON.stringify(entry)));
    }
  }
  return trimmed
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

// Mirrors TestCasePayload.Parse: required string input/expected, optional bool
// hidden trailing behind every visible case.
export function validateTestCaseLines(entries: string[]): string[] {
  const errors: string[] = [];
  let seenHidden = false;
  entries.forEach((raw, i) => {
    const n = i + 1;
    let obj: unknown;
    try {
      obj = JSON.parse(raw);
    } catch {
      errors.push(`Test case ${n} is not valid JSON.`);
      return;
    }
    if (typeof obj !== "object" || obj === null || Array.isArray(obj)) {
      errors.push(`Test case ${n} must be a JSON object.`);
      return;
    }
    const o = obj as Record<string, unknown>;
    if (typeof o.input !== "string") {
      errors.push(`Test case ${n} is missing a string "input" field.`);
    }
    if (typeof o.expected !== "string") {
      errors.push(`Test case ${n} is missing a string "expected" field.`);
    }
    if (o.hidden !== undefined) {
      if (typeof o.hidden !== "boolean") {
        errors.push(`Test case ${n} "hidden" must be a boolean.`);
      } else if (o.hidden) {
        seenHidden = true;
      }
    }
    if (!(o.hidden === true) && seenHidden) {
      errors.push(
        `Test case ${n} is not hidden but follows a hidden case; hidden cases must be trailing.`
      );
    }
  });
  return errors;
}

// Server error keys the create form renders next to a field. Everything else
// the server can put in `errors` (validator-only keys like QuestionType or
// VariantSeed, ProblemDetails JSON paths like "$.difficulty", envelope-less
// keys like "command", or camelCase variants under another JSON naming policy)
// has no rendered field — those must surface in the banner or the admin
// submits blind.
const FORM_FIELD_KEYS = [
  "Title",
  "ConceptId",
  "Description",
  "Difficulty",
  "TestCases",
  "Rubric",
] as const;

// ponytail: canonical lookup by lowercase key; one synonym table for the
// PascalCase name this API emits (verified live) plus camelCase variants.
const CANONICAL_FIELD_KEY = new Map(
  FORM_FIELD_KEYS.map((key) => [key.toLowerCase(), key])
);

/**
 * Splits the server's `errors` map (PascalCase property names, ProblemDetails
 * paths, or anything else) into field-scoped errors the form can render and a
 * single banner message for every key no form field can display. Messages for
 * one key are joined with " ".
 */
export function splitServerFieldErrors(errors: Record<string, string[]>): {
  fieldErrors: Record<string, string>;
  unmatchedMessage: string | null;
} {
  const fieldErrors: Record<string, string> = {};
  const unmatched: string[] = [];
  for (const [rawKey, messages] of Object.entries(errors)) {
    const canonicalKey = CANONICAL_FIELD_KEY.get(rawKey.toLowerCase());
    if (canonicalKey) {
      fieldErrors[canonicalKey] = messages.join(" ");
    } else {
      unmatched.push(...messages);
    }
  }
  return { fieldErrors, unmatchedMessage: unmatched.length > 0 ? unmatched.join(" ") : null };
}

// Mirrors RubricPayload.Parse: nonempty string criterion + numeric points.
export function validateRubricLines(entries: string[]): string[] {
  const errors: string[] = [];
  entries.forEach((raw, i) => {
    const n = i + 1;
    let obj: unknown;
    try {
      obj = JSON.parse(raw);
    } catch {
      errors.push(`Rubric entry ${n} is not valid JSON.`);
      return;
    }
    if (typeof obj !== "object" || obj === null || Array.isArray(obj)) {
      errors.push(`Rubric entry ${n} must be a JSON object.`);
      return;
    }
    const o = obj as Record<string, unknown>;
    if (typeof o.criterion !== "string" || o.criterion.trim().length === 0) {
      errors.push(`Rubric entry ${n} is missing a nonempty string "criterion" field.`);
    }
    if (typeof o.points !== "number") {
      errors.push(`Rubric entry ${n} is missing a numeric "points" field.`);
    }
  });
  return errors;
}
