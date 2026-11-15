import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api } from "../lib/api";
import { formatEthiopian, toEthiopian, todayIso } from "../lib/ethiopian";
import { can, type EmployeeDto } from "../lib/types";

export function Dashboard() {
  const { t, i18n } = useTranslation();
  const { me, modules, companies, company } = useSession();
  const today = todayIso();
  const e = toEthiopian(today);
  const fiscalYear = e.month >= 11 ? e.year + 1 : e.year; // Hamle–Sene
  const employees = useQuery({
    queryKey: ["employees", company?.id],
    queryFn: () => api<EmployeeDto[]>(`/api/core/employees?companyId=${company!.id}`),
    enabled: !!company && can(me, "core.employees.read"),
  });

  return (
    <>
      <h1>{t("dashboard.welcome", { name: me.fullName ?? me.username })}</h1>
      <div className="cards">
        <div className="card">
          <div className="card-label">{t("dashboard.today")}</div>
          <div className="card-value">{formatEthiopian(today, i18n.language === "am")}</div>
          <div className="card-sub">{new Date().toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" })}</div>
        </div>
        <div className="card">
          <div className="card-label">{t("dashboard.fiscalYear")}</div>
          <div className="card-value">{t("dashboard.fiscalYearValue", { year: fiscalYear })}</div>
          <div className="card-sub">Hamle 1 – Sene 30</div>
        </div>
        <div className="card">
          <div className="card-label">{t("dashboard.companies")}</div>
          <div className="card-value">{companies.length}</div>
          <div className="card-sub">{company?.name}</div>
        </div>
        {employees.data && (
          <div className="card">
            <div className="card-label">{t("dashboard.employees")}</div>
            <div className="card-value">{employees.data.length}</div>
            <div className="card-sub">{company?.abbreviation}</div>
          </div>
        )}
      </div>
      <h2>{t("dashboard.modules")}</h2>
      <div className="chips">
        {modules.map((m) => (
          <span key={m.id} className="chip">{m.name} {m.version}</span>
        ))}
      </div>
    </>
  );
}
