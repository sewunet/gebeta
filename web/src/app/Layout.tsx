import { useEffect, useRef, useState } from "react";
import { Link, Outlet, useRouterState } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useAuth } from "../lib/auth";
import { can } from "../lib/types";
import { APPS, GROUPS, type Group } from "./apps";
import { useSession } from "./context";

const FOLDED_KEY = "gebeta.menu.folded";

/** The menu groups the user folded, remembered in this browser (a convenience: everything works without it). */
function useFolded() {
  const [folded, setFolded] = useState<Group[]>(() => {
    try { return JSON.parse(localStorage.getItem(FOLDED_KEY) ?? "[]") as Group[]; } catch { return []; }
  });
  const toggle = (g: Group) => setFolded((f) => {
    const next = f.includes(g) ? f.filter((x) => x !== g) : [...f, g];
    try { localStorage.setItem(FOLDED_KEY, JSON.stringify(next)); } catch { /* storage unavailable */ }
    return next;
  });
  return [folded, toggle] as const;
}

/**
 * Fiori-style shell: a shell bar across the top (menu, Gebeta, the current app, company, user menu) and a
 * navigation menu that opens over the page. The home page is the launchpad of tiles.
 */
export function Layout() {
  const { t } = useTranslation();
  const { signOut } = useAuth();
  const { me, modules, companies, company, setCompanyId, savePreferences } = useSession();
  const [menu, setMenu] = useState(false);
  const [userMenu, setUserMenu] = useState(false);
  const [folded, toggleGroup] = useFolded();
  const path = useRouterState({ select: (s) => s.location.pathname });
  const userRef = useRef<HTMLDivElement>(null);
  const installed = (m?: string) => !m || modules.some((x) => x.id === m);
  const apps = APPS.filter((a) => installed(a.module) && can(me, a.permission));
  const current = apps.filter((a) => path.startsWith(a.path)).sort((a, b) => b.path.length - a.path.length)[0];
  const initials = (me.fullName ?? me.username).split(/\s+/).map((w) => w[0]).slice(0, 2).join("").toUpperCase();

  useEffect(() => { setMenu(false); setUserMenu(false); }, [path]);
  useEffect(() => {
    const close = (e: MouseEvent) => { if (userRef.current && !userRef.current.contains(e.target as Node)) setUserMenu(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, []);

  return (
    <div className="shell">
      <header className="shell-bar">
        <button className="shell-icon" aria-label={t("shell.menu")} aria-expanded={menu} onClick={() => setMenu((m) => !m)}>☰</button>
        <Link to="/" className="shell-brand">
          <img src="/gebeta-mark.svg" alt="" width={26} height={26} />
          <span>{t("app.name")}</span>
        </Link>
        {current && <span className="shell-app">{t(current.title)}</span>}
        <div className="shell-spacer" />
        <select className="shell-company" aria-label={t("common.company")} value={company?.id ?? ""} onChange={(e) => setCompanyId(e.target.value)}>
          {companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <div className="shell-user" ref={userRef}>
          <button className="avatar" aria-label={me.fullName ?? me.username} aria-expanded={userMenu} onClick={() => setUserMenu((u) => !u)}>{initials}</button>
          {userMenu && (
            <div className="popover" role="menu">
              <div className="popover-head"><strong>{me.fullName ?? me.username}</strong><span className="muted small">{me.username}</span></div>
              <label className="popover-row">{t("common.language")}
                <div className="segmented">
                  <button className={me.language === "en" ? "on" : ""} onClick={() => savePreferences({ language: "en" })}>English</button>
                  <button className={me.language === "am" ? "on" : ""} onClick={() => savePreferences({ language: "am" })}>አማርኛ</button>
                </div>
              </label>
              <label className="popover-row">{t("common.calendar")}
                <select value={me.calendar} onChange={(e) => savePreferences({ calendar: e.target.value as typeof me.calendar })}>
                  {(["Ethiopian", "Gregorian", "Both"] as const).map((c) => <option key={c} value={c}>{t(`calendar.${c}`)}</option>)}
                </select>
              </label>
              <button className="secondary" onClick={() => void signOut()}>{t("common.signOut")}</button>
            </div>
          )}
        </div>
      </header>

      {menu && <div className="nav-scrim" onClick={() => setMenu(false)} />}
      <nav className={`nav-menu ${menu ? "open" : ""}`} aria-label={t("shell.menu")} aria-hidden={!menu}>
        <Link to="/" className="nav-link" activeOptions={{ exact: true }}>{t("nav.home")}</Link>
        {GROUPS.map((g) => {
          const items = apps.filter((a) => a.group === g);
          // the group of the page you are on always shows
          const open = !folded.includes(g) || current?.group === g;
          return items.length ? (
            <div key={g}>
              <button type="button" className="nav-group" aria-expanded={open} onClick={() => toggleGroup(g)}>
                {t(`nav.${g}`)}<span className="chevron" aria-hidden>▾</span>
              </button>
              {open && <div className="nav-items">{items.map((a) => <Link key={a.id} to={a.path} className="nav-link">{t(a.title)}</Link>)}</div>}
            </div>
          ) : null;
        })}
      </nav>

      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
