const API_BASE = "/api";

interface ApiSuccessResponse<T> {
  success: true;
  data: T;
}

interface ApiErrorResponse {
  success: false;
  message: string;
  errors?: Record<string, string[]>;
}

type ApiResponse<T> = ApiSuccessResponse<T> | ApiErrorResponse;

class ApiError extends Error {
  status: number;
  errors?: Record<string, string[]>;

  constructor(message: string, status: number, errors?: Record<string, string[]>) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.errors = errors;
  }
}

async function request<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const url = `${API_BASE}${path}`;
  // FormData must keep the browser-generated multipart boundary: no JSON header.
  const isFormData = options.body instanceof FormData;
  const res = await fetch(url, {
    credentials: "include",
    ...options,
    headers: {
      ...(isFormData ? {} : { "Content-Type": "application/json" }),
      ...options.headers,
    },
  });

  if (res.status === 401) {
    throw new ApiError("Unauthorized", 401);
  }

  let body: ApiResponse<T>;
  try {
    body = await res.json();
  } catch {
    // Non-envelope failures (413 from the request-size limit, a proxy error page)
    // have no JSON to read; the status is all that can be reported.
    throw new ApiError(`Request failed with status ${res.status}`, res.status);
  }

  if (!body.success) {
    // body.message can be missing on non-envelope errors (e.g. ProblemDetails 400)
    throw new ApiError(
      body.message || `Request failed with status ${res.status}`,
      res.status,
      body.errors
    );
  }

  return body.data;
}

export interface LoginResponse {
  id: string;
  fullName: string;
  email: string;
  roles: string[];
}

export interface RegisterRequest {
  fullName: string;
  email: string;
  password: string;
}

export interface RegisterResponse {
  message: string;
  email: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface MasteryConcept {
  conceptId: string;
  name: string;
  category: string;
  difficultyLevel: number;
  masteryProbability: number | null;
  correctAttempts: number;
  incorrectAttempts: number;
  isMastered: boolean;
  lastPracticedAt: string | null;
}

export interface MasteryProfile {
  concepts: MasteryConcept[];
  masteredCount: number;
  totalConcepts: number;
  overallReadiness: number;
}

export interface SkillBreakdown {
  topic: string;
  mastery: number;
  jdWeight: number;
  contribution: number;
}

export interface SkillGap {
  topic: string;
  severity: number;
  recommendation: string;
}

export interface ReadinessProfile {
  score: number;
  maxScore: number;
  breakdown: SkillBreakdown[];
  gaps: SkillGap[];
  estimatedWeeksToReady: number;
}

export interface SubmissionItem {
  submissionId: string;
  questionTitle: string;
  conceptName: string;
  isCorrect: boolean;
  masteryDelta: number | null;
  createdAt: string;
}

export interface StudentQuestion {
  id: string;
  conceptId: string;
  conceptName: string;
  title: string;
  description: string;
  questionType: string;
  difficulty: number;
  exampleCount: number;
  totalTestCases: number;
  hiddenTestCaseCount: number;
}

export interface QuestionListResult {
  questions: StudentQuestion[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface QuestionDetail {
  id: string;
  conceptId: string;
  conceptName: string;
  title: string;
  description: string;
  questionType: string;
  difficulty: number;
  exampleInputs: string[];
  exampleOutputs: string[];
  starterCode: string[];
  visibleTestCases: string[];
  rubric: string[];
  totalTestCases: number;
  hiddenTestCaseCount: number;
}

export interface AdminUser {
  id: string;
  email: string | null;
  fullName: string;
  roles: string[];
}

// --- Admin question bank (#103): GET/POST /v1/admin/questions, ManageContent policy ---

export interface AdminQuestion {
  id: string;
  conceptId: string;
  title: string;
  description: string;
  difficulty: number;
  testCaseCount: number;
  createdAt: string;
}

export interface AdminQuestionListResult {
  questions: AdminQuestion[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CreateAdminQuestionInput {
  conceptId: string;
  title: string;
  description: string;
  difficulty: number;
  questionType: string;
  /** Each entry is a JSON string: {"input": string, "expected": string, "hidden"?: bool} — hidden must be trailing. */
  testCases: string[];
  /** Each entry is a JSON string: {"criterion": string, "points": number}. */
  rubric?: string[];
}

export interface AdminQuestionCreated {
  questionId: string;
  conceptId: string;
  title: string;
  difficulty: number;
}

// --- Admin textbook corpus (#104): upload + status polling, ManageContent policy ---

export type TextbookJobStatus = "Pending" | "Processing" | "Completed" | "Failed";

export interface TextbookJobAccepted {
  jobId: string;
  message: string;
}

export interface TextbookJob {
  jobId: string;
  fileName: string;
  status: TextbookJobStatus;
  totalChunks: number;
  processedChunks: number;
  /** Only set once status is Failed. */
  error: string | null;
  createdAt: string;
  completedAt: string | null;
}

export const api = {
  register: (data: RegisterRequest) =>
    request<RegisterResponse>("/v1/auth/register", {
      method: "POST",
      body: JSON.stringify(data),
    }),

  login: (data: LoginRequest) =>
    request<LoginResponse>("/v1/auth/login", {
      method: "POST",
      body: JSON.stringify(data),
    }),

  me: () => request<LoginResponse>("/v1/auth/me"),

  logout: () =>
    request<object>("/v1/auth/logout", { method: "POST" }),

  mastery: () => request<MasteryProfile>("/v1/student/mastery"),

  readiness: () => request<ReadinessProfile>("/v1/student/readiness"),

  submissions: (limit = 10) =>
    request<SubmissionItem[]>(`/v1/student/submissions?limit=${limit}`),

  questions: (params: {
    conceptId?: string;
    difficulty?: number;
    page?: number;
    pageSize?: number;
  } = {}) => {
    const q = new URLSearchParams();
    if (params.conceptId) q.set("conceptId", params.conceptId);
    if (params.difficulty != null) q.set("difficulty", String(params.difficulty));
    if (params.page) q.set("page", String(params.page));
    if (params.pageSize) q.set("pageSize", String(params.pageSize));
    const qs = q.toString();
    return request<QuestionListResult>(`/v1/questions${qs ? `?${qs}` : ""}`);
  },

  question: (id: string) => request<QuestionDetail>(`/v1/questions/${encodeURIComponent(id)}`),

  // --- admin (#105): SuperAdmin-only via ManageRoles policy; 401/403 enforced server-side ---
  adminUsers: (page = 1, pageSize = 50) =>
    request<AdminUser[]>(`/v1/admin/users?page=${page}&pageSize=${pageSize}`),

  adminRoles: () => request<string[]>("/v1/admin/roles"),

  adminAssignRole: (userId: string, role: string) =>
    request<AdminUser>(`/v1/admin/users/${encodeURIComponent(userId)}/roles`, {
      method: "POST",
      body: JSON.stringify({ role }),
    }),

  adminRemoveRole: (userId: string, role: string) =>
    request<AdminUser>(
      `/v1/admin/users/${encodeURIComponent(userId)}/roles/${encodeURIComponent(role)}`,
      { method: "DELETE" }
    ),

  // --- admin questions (#103): ContentAdmin + SuperAdmin via ManageContent policy ---
  adminQuestions: (page = 1, pageSize = 20) =>
    request<AdminQuestionListResult>(`/v1/admin/questions?page=${page}&pageSize=${pageSize}`),

  adminCreateQuestion: (input: CreateAdminQuestionInput) =>
    request<AdminQuestionCreated>("/v1/admin/questions", {
      method: "POST",
      body: JSON.stringify(input),
    }),

  // --- admin corpus (#104): ContentAdmin + SuperAdmin via ManageContent policy ---
  adminUploadTextbook: (file: File, conceptId?: string) => {
    const body = new FormData();
    body.append("file", file);
    // Omit rather than send "": an empty string does not bind to Guid? server-side.
    if (conceptId) body.append("conceptId", conceptId);
    return request<TextbookJobAccepted>("/v1/admin/textbook/upload", { method: "POST", body });
  },

  adminTextbookJob: (jobId: string) =>
    request<TextbookJob>(`/v1/admin/textbook/status/${encodeURIComponent(jobId)}`),
};

export { ApiError };
