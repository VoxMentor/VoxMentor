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

export interface SubmissionItem {
  submissionId: string;
  questionTitle: string;
  conceptName: string;
  isCorrect: boolean;
  masteryDelta: number | null;
  createdAt: string;
}

export interface NextQuestion {
  questionId: string;
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

  nextQuestion: (conceptId?: string) =>
    request<NextQuestion>(
      `/v1/student/next-question${conceptId ? `?conceptId=${encodeURIComponent(conceptId)}` : ""}`
    ),
};

export { ApiError };
