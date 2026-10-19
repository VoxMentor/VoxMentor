// Pure logic behind the admin corpus upload page (#104). Lives outside the page so
// Node's test runner covers it — see web/lib/corpus-job.test.ts.
//
// The file rules mirror UploadTextbookValidator (.pdf/.txt, 20 MiB). The server stays
// the authority; this only fails before the bytes go over the wire and lands the
// message next to the file field instead of in a banner.

import type { TextbookJobStatus } from "./api";

/** Mirrors UploadTextbookValidator.MaxFileSizeBytes — the API also caps the request at 21 MiB. */
export const CORPUS_MAX_BYTES = 20 * 1024 * 1024;

/** Poll cadence for the job panel. */
export const POLL_INTERVAL_MS = 1500;

const ALLOWED_EXTENSION = /\.(pdf|txt)$/i;

export function validateCorpusFile(file: { name: string; size: number }): string | null {
  if (!ALLOWED_EXTENSION.test(file.name.trim())) {
    return "Only .pdf and .txt files are supported.";
  }
  if (file.size <= 0) return "File must not be empty.";
  if (file.size > CORPUS_MAX_BYTES) {
    return `File must be ${CORPUS_MAX_BYTES / (1024 * 1024)} MB or smaller.`;
  }
  return null;
}

/** The job panel stops polling once the server reports one of these. */
export function isTerminalStatus(status: TextbookJobStatus): boolean {
  return status === "Completed" || status === "Failed";
}

/** 0–100 while chunks are being embedded; 0 until the worker knows how many there are. */
export function progressPercent(totalChunks: number, processedChunks: number): number {
  if (!Number.isFinite(totalChunks) || totalChunks <= 0) return 0;
  const percent = (processedChunks / totalChunks) * 100;
  return Math.min(100, Math.max(0, Math.round(percent)));
}

// Server error keys the upload form can render next to a field. Everything else the
// server can put in `errors` (validator keys with no rendered field, ProblemDetails
// paths, envelope-less keys) must land in the banner or the admin submits blind.
const FILE_KEYS = ["filename", "file", "length", "content"];
const CONCEPT_KEYS = ["conceptid", "concept"];

function join(existing: string | null, message: string): string {
  return existing ? `${existing} ${message}` : message;
}

/**
 * Splits the upload endpoint's `errors` map (PascalCase property names from
 * FluentValidation, camelCase/ProblemDetails binding keys) into the two errors the
 * form can render and one banner message for everything it cannot.
 */
export function splitUploadErrors(errors: Record<string, string[]>): {
  fileError: string | null;
  conceptError: string | null;
  banner: string | null;
} {
  let fileError: string | null = null;
  let conceptError: string | null = null;
  const banner: string[] = [];

  for (const [rawKey, messages] of Object.entries(errors)) {
    const key = rawKey.replace(/^\$\./, "").replace(/^command\./i, "").toLowerCase();
    const message = messages.join(" ");
    if (FILE_KEYS.includes(key)) {
      fileError = join(fileError, message);
    } else if (CONCEPT_KEYS.includes(key)) {
      conceptError = join(conceptError, message);
    } else {
      banner.push(...messages);
    }
  }

  return { fileError, conceptError, banner: banner.length > 0 ? banner.join(" ") : null };
}
