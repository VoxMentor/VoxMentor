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
import {
  canSwitchView,
  landingPath,
  loadView,
  resolveView,
  saveView,
  type ViewMode,
} from "./view";

interface AuthContextType {
  user: LoginResponse | null;
  loading: boolean;
  view: ViewMode;
  switchView: (view: ViewMode) => void;
  setUser: (user: LoginResponse | null) => void;
  logout: () => Promise<void>;
}

let authCheckVersion = 0;

const AuthContext = createContext<AuthContextType>({
  user: null,
  loading: true,
  view: "student",
  switchView: () => {},
  setUser: () => {},
  logout: async () => {},
});

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<LoginResponse | null>(null);
  const [view, setView] = useState<ViewMode>("student");
  const [viewKeyLoaded, setViewKeyLoaded] = useState<string | null>(null);
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

  // The view is derived from the signed-in user. Recomputing during render
  // (React's documented "adjust state when a prop changes" pattern) keeps a new
  // login or a role change from flashing the wrong surface first.
  const viewKey = user ? `${user.id}:${user.roles.join(",")}` : null;
  if (user && viewKey !== viewKeyLoaded) {
    setViewKeyLoaded(viewKey);
    setView(resolveView(loadView(user.id), user.roles));
  }

  useEffect(() => {
    if (loading) return;

    const publicPages = ["/", "/login", "/register"];
    const isPublic = publicPages.includes(pathname);

    if (!user && !isPublic) {
      router.replace("/login");
    } else if (user && isPublic) {
      // Resolve from storage directly: this effect can run before the view
      // state above has settled, so an admin-only user must not be sent to
      // /dashboard first.
      router.replace(landingPath(resolveView(loadView(user.id), user.roles)));
    }
  }, [user, loading, pathname, router]);

  const switchView = useCallback(
    (next: ViewMode) => {
      if (!user || !canSwitchView(user.roles) || next === view) return;
      saveView(user.id, next);
      setView(next);
      router.replace(landingPath(next));
    },
    [user, view, router]
  );

  const logout = useCallback(async () => {
    ++authCheckVersion;
    saveView(user?.id, null);
    await api
      .logout()
      .catch(() => {})
      .finally(() => {
        setUser(null);
        setViewKeyLoaded(null);
        setView("student");
        router.replace("/login");
      });
  }, [router, user?.id]);

  return (
    <AuthContext.Provider
      value={{ user, loading, view, switchView, setUser, logout }}
    >
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
