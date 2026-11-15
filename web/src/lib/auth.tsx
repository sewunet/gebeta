import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { InMemoryWebStorage, UserManager, WebStorageStateStore, type User } from "oidc-client-ts";

/**
 * Sign-in with Keycloak (realm "gebeta", public client "gebeta-web") using the authorization code
 * flow with PKCE. Tokens are kept in memory only (docs/frontend.md); after a reload the Keycloak
 * session signs the user in again without asking for the password.
 */
export const userManager = new UserManager({
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? "http://localhost:8180/realms/gebeta",
  client_id: import.meta.env.VITE_OIDC_CLIENT_ID ?? "gebeta-web",
  redirect_uri: `${window.location.origin}/callback`,
  post_logout_redirect_uri: `${window.location.origin}/`,
  response_type: "code",
  scope: "openid profile email",
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  automaticSilentRenew: true,
});

type AuthState = { user: User | null; signOut: () => Promise<void> };

const AuthContext = createContext<AuthState>({ user: null, signOut: async () => {} });

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        if (window.location.pathname === "/callback") {
          const signedIn = await userManager.signinRedirectCallback();
          const returnTo = typeof signedIn.state === "string" ? signedIn.state : "/";
          window.history.replaceState({}, "", returnTo);
          if (!cancelled) setUser(signedIn);
          return;
        }

        const existing = await userManager.getUser();
        if (existing && !existing.expired) {
          if (!cancelled) setUser(existing);
          return;
        }

        await userManager.signinRedirect({ state: window.location.pathname + window.location.search });
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e));
      }
    })();

    const onLoaded = (u: User) => setUser(u);
    userManager.events.addUserLoaded(onLoaded);
    return () => {
      cancelled = true;
      userManager.events.removeUserLoaded(onLoaded);
    };
  }, []);

  if (error) {
    return (
      <div className="center-screen">
        <p>Sign-in failed: {error}</p>
        <button onClick={() => userManager.signinRedirect()}>Try again</button>
      </div>
    );
  }

  if (!user) {
    return <div className="center-screen">Signing in…</div>;
  }

  return (
    <AuthContext.Provider value={{ user, signOut: () => userManager.signoutRedirect() }}>{children}</AuthContext.Provider>
  );
}

export const useAuth = () => useContext(AuthContext);
