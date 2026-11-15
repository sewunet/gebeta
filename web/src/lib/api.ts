import { userManager } from "./auth";

/** An error answer from the API (RFC 9457 Problem Details). */
export class ApiError extends Error {
  constructor(public readonly status: number, message: string, public readonly code?: string) {
    super(message);
  }
}

/** Calls the Gebeta API with the signed-in user's access token. */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const user = await userManager.getUser();
  const headers = new Headers(init.headers);
  if (user?.access_token) headers.set("Authorization", `Bearer ${user.access_token}`);
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

  const response = await fetch(path, { ...init, headers });
  if (response.status === 401) {
    await userManager.signinRedirect({ state: window.location.pathname + window.location.search });
    throw new ApiError(401, "Signing in again…");
  }

  if (!response.ok) {
    let message = response.statusText;
    let code: string | undefined;
    try {
      const problem = await response.json();
      message = problem.title ?? message;
      code = problem.code;
    } catch {
      // not JSON
    }
    throw new ApiError(response.status, response.status === 403 ? "You do not have permission for this." : message, code);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const post = <T,>(path: string, body?: unknown) =>
  api<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) });

export const put = <T,>(path: string, body: unknown) => api<T>(path, { method: "PUT", body: JSON.stringify(body) });

/** Downloads a file (CSV report) with the access token and saves it. */
export async function download(path: string, fileName: string) {
  const user = await userManager.getUser();
  const response = await fetch(path, { headers: { Authorization: `Bearer ${user?.access_token ?? ""}` } });
  if (!response.ok) throw new ApiError(response.status, response.statusText);
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}
