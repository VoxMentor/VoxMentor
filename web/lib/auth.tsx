"use client";

import {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  type ReactNode,
} from "react";
import { useRouter, usePathname } from "next/navigation";
import { api, type LoginResponse } from "./api";

interface AuthContextType {
  user: LoginResponse | null;
  loading: boolean;
  setUser: (user: LoginResponse | null) => void;
  logout: () => Promise<void>;
}

let authCheckVersion = 0;

const AuthContext = createContext<AuthContextType>({
  user: null,
  loading: true,
  setUser: () => {},
  logout: async () => {},
});

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<LoginResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const router = useRouter();
  const pathname = usePathname();
  const checkAuthStartVersion = useRef(0);

  useEffect(() => {
    let cancelled = false;
    const thisCheckVersion = ++authCheckVersion;
    checkAuthStartVersion.current = thisCheckVersion;

    async function checkAuth() {
      try {
        const me = await api.me();
        if (
          !cancelled &&
          checkAuthStartVersion.current === authCheckVersion
        ) {
          setUser(me);
        }
      } catch {
        if (
          !cancelled &&
          checkAuthStartVersion.current === authCheckVersion
        ) {
          setUser(null);
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    checkAuth();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (loading) return;

    const publicPages = ["/", "/login", "/register"];
    const isPublic = publicPages.includes(pathname);

    if (!user && !isPublic) {
      router.replace("/login");
    } else if (user && isPublic) {
      router.replace("/dashboard");
    }
  }, [user, loading, pathname, router]);

  const logout = useCallback(async () => {
    ++authCheckVersion;
    await api
      .logout()
      .catch(() => {})
      .finally(() => {
        setUser(null);
        router.replace("/login");
      });
  }, [router]);

  return (
    <AuthContext.Provider value={{ user, loading, setUser, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}

export function invalidateAuthCheck() {
  ++authCheckVersion;
}
