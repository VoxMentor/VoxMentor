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
  const res = await fetch(url, {
    credentials: "include",
    headers: { "Content-Type": "application/json", ...options.headers },
    ...options,
  });

  if (res.status === 401) {
    throw new ApiError("Unauthorized", 401);
  }

  const body: ApiResponse<T> = await res.json();

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

// --- JD intelligence (#121): POST /v1/jd/upload, GET /v1/jd/{jdId} ---

export interface JdUploadResult {
  jdId: string;
  companyName: string;
  role: string;
  /** topic → weight, sums to 1.0 */
  technicalSkills: Record<string, number>;
  /** topic → weight, sums to 1.0 */
  hrSkills: Record<string, number>;
  difficulty: string;
  estimatedWeeks: number;
  createdAt: string;
}

export interface JdRoadmapWeek {
  week: number;
  focus: string;
  conceptIds: string[];
}

export interface JdDetail {
  jdId: string;
  companyName: string;
  role: string;
  technicalSkills: Record<string, number>;
  hrSkills: Record<string, number>;
  difficulty: string;
  readinessScore: number;
  gaps: SkillGap[];
  estimatedWeeks: number;
  roadmap: JdRoadmapWeek[];
  createdAt: string;
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

  // --- JD intelligence (#121) ---
  jdUpload: (rawText: string) =>
    request<JdUploadResult>("/v1/jd/upload", {
      method: "POST",
      body: JSON.stringify({ rawText }),
    }),

  jd: (id: string) => request<JdDetail>(`/v1/jd/${encodeURIComponent(id)}`),

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
};

export { ApiError };
