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

let signingIn: Promise<User | null> | undefined;

/**
 * Signs in once per page load. The authorization code from Keycloak can be exchanged only once, so this must not run again when React
 * runs an effect twice (development StrictMode mounts, unmounts and mounts again); a second exchange fails with "Code not valid".
 * Resolves with the user, or null while the browser is on its way to Keycloak.
 */
export function signInOnce(): Promise<User | null> {
  signingIn ??= (async () => {
    if (window.location.pathname === "/callback") {
      const signedIn = await userManager.signinRedirectCallback();
      const returnTo = typeof signedIn.state === "string" ? signedIn.state : "/";
      window.history.replaceState({}, "", returnTo);
      return signedIn;
    }

    const existing = await userManager.getUser();
    if (existing && !existing.expired) {
      return existing;
    }

    await userManager.signinRedirect({ state: window.location.pathname + window.location.search });
    return null;
  })();
  return signingIn;
}

const AuthContext = createContext<AuthState>({ user: null, signOut: async () => {} });

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    signInOnce().then(
      (signedIn) => { if (!cancelled && signedIn) setUser(signedIn); },
      (e: unknown) => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)); },
    );

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
        {/* A fresh sign-in, not the failed callback again: its code cannot be used twice. */}
        <button onClick={() => void userManager.signinRedirect({ state: "/" })}>Try again</button>
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
