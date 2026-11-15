import { Link, Outlet } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useAuth } from "../lib/auth";
import { can } from "../lib/types";
import { useSession } from "./context";

/** Sidebar built from the installed modules and the user's permissions; top bar with company, language and calendar. */
export function Layout() {
  const { t } = useTranslation();
  const { signOut } = useAuth();
  const { me, modules, companies, company, setCompanyId, savePreferences } = useSession();
  const hasPayroll = modules.some((m) => m.id === "payroll");

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <img src="/gebeta-mark.svg" alt="" width={36} height={36} />
          <div>
            <div className="brand-name">{t("app.name")}</div>
            <div className="brand-by">{t("app.by")}</div>
          </div>
        </div>
        <nav>
          <Link to="/" className="nav-link" activeOptions={{ exact: true }}>{t("nav.dashboard")}</Link>
          <div className="nav-group">{t("nav.core")}</div>
          {can(me, "core.companies.read") && <Link to="/companies" className="nav-link">{t("nav.companies")}</Link>}
          {can(me, "core.employees.read") && <Link to="/employees" className="nav-link">{t("nav.employees")}</Link>}
          {hasPayroll && (
            <>
              <div className="nav-group">{t("nav.payroll")}</div>
              {can(me, "payroll.salary-slips.read") && <Link to="/payroll/slips" className="nav-link">{t("nav.salarySlips")}</Link>}
              {can(me, "payroll.runs.read") && <Link to="/payroll/run" className="nav-link">{t("nav.payrollRun")}</Link>}
              {can(me, "payroll.calculator.use") && <Link to="/payroll/calculator" className="nav-link">{t("nav.calculator")}</Link>}
            </>
          )}
        </nav>
      </aside>
      <div className="main">
        <header className="topbar">
          <label className="field-inline">
            <span>{t("common.company")}</span>
            <select value={company?.id ?? ""} onChange={(e) => setCompanyId(e.target.value)}>
              {companies.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </label>
          <div className="topbar-right">
            <label className="field-inline">
              <span>{t("common.calendar")}</span>
              <select value={me.calendar} onChange={(e) => savePreferences({ calendar: e.target.value as typeof me.calendar })}>
                {(["Ethiopian", "Gregorian", "Both"] as const).map((c) => (
                  <option key={c} value={c}>{t(`calendar.${c}`)}</option>
                ))}
              </select>
            </label>
            <div className="segmented" role="group" aria-label={t("common.language")}>
              <button className={me.language === "en" ? "on" : ""} onClick={() => savePreferences({ language: "en" })}>English</button>
              <button className={me.language === "am" ? "on" : ""} onClick={() => savePreferences({ language: "am" })}>አማርኛ</button>
            </div>
            <span className="user">{me.fullName ?? me.username}</span>
            <button className="link-button" onClick={() => void signOut()}>{t("common.signOut")}</button>
          </div>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
