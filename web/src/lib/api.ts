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
    throw new ApiError(body.message, res.status, body.errors);
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
};

export { ApiError };
