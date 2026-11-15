import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { APPS, GROUPS, type AppEntry } from "../app/apps";
import { useSession } from "../app/context";
import { api } from "../lib/api";
import { formatEthiopian, toEthiopian, todayIso } from "../lib/ethiopian";
import { can, type AgeingReportDto, type BankAccountDto, type EmployeeDto, type PeriodClosingsDto, type VatReportDto } from "../lib/types";
import { Tile, type Tone } from "../ui/fiori";
import { monthRange, currentMonth } from "./payroll/month";

const compact = (n: number) => new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 }).format(n);

/** Launchpad: one group of tiles per area, each tile opening its app; key tiles show a live figure. */
export function Home() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { me, modules, company } = useSession();
  const am = i18n.language === "am";
  const today = todayIso();
  const e = toEthiopian(today);
  const fiscalYear = e.month >= 11 ? e.year + 1 : e.year;
  const month = monthRange(currentMonth());
  const id = company?.id;
  const on = (permission: string, module?: string) => !!id && can(me, permission) && (!module || modules.some((m) => m.id === module));

  const receivables = useQuery({ queryKey: ["invoices", "ageing", id, "Sales", today], enabled: on("accounting.reports.read", "accounting"),
    queryFn: () => api<AgeingReportDto>(`/api/accounting/reports/ageing?companyId=${id}&kind=Sales&asOf=${today}`) });
  const payables = useQuery({ queryKey: ["invoices", "ageing", id, "Purchase", today], enabled: on("accounting.reports.read", "accounting"),
    queryFn: () => api<AgeingReportDto>(`/api/accounting/reports/ageing?companyId=${id}&kind=Purchase&asOf=${today}`) });
  const vat = useQuery({ queryKey: ["tax-reports", "vat", id, currentMonth()], enabled: on("accounting.reports.read", "accounting"),
    queryFn: () => api<VatReportDto>(`/api/accounting/reports/vat?companyId=${id}&from=${month.start}&to=${month.end}`) });
  const periods = useQuery({ queryKey: ["periods", id], enabled: on("accounting.reports.read", "accounting"),
    queryFn: () => api<PeriodClosingsDto>(`/api/accounting/periods?companyId=${id}`) });
  const banks = useQuery({ queryKey: ["bank-accounts", id], enabled: on("accounting.banking.read", "accounting"),
    queryFn: () => api<BankAccountDto[]>(`/api/accounting/bank-accounts?companyId=${id}`) });
  const employees = useQuery({ queryKey: ["employees", id], enabled: on("core.employees.read"),
    queryFn: () => api<EmployeeDto[]>(`/api/core/employees?companyId=${id}`) });

  const overdue = (r?: AgeingReportDto) => r ? r.totals.days1To30 + r.totals.days31To60 + r.totals.days61To90 + r.totals.over90 : 0;
  const kpi: Record<string, { value?: string; unit?: string; footer?: string; tone?: Tone }> = {
    invoices: receivables.data ? { value: compact(receivables.data.totals.total), unit: "ETB", footer: t("tiles.owedToUs"), tone: overdue(receivables.data) > 0 ? "critical" : undefined } : {},
    ageing: payables.data ? { value: compact(payables.data.totals.total), unit: "ETB", footer: t("tiles.weOwe") } : {},
    tax: vat.data ? { value: compact(Math.abs(vat.data.netPayable)), unit: "ETB", footer: t(vat.data.netPayable >= 0 ? "inv.vatPayable" : "inv.vatCredit") } : {},
    periods: periods.data ? (periods.data.lockDate
      ? { value: formatEthiopian(periods.data.lockDate, am).split(" ").slice(0, 2).join(" "), footer: t("tiles.closedUpTo") }
      : { value: t("tiles.allOpen"), footer: t("tiles.noMonthClosed") }) : {},
    banking: banks.data ? { value: String(banks.data.length), footer: t("tiles.bankAccounts") } : {},
    employees: employees.data ? { value: String(employees.data.length), footer: company?.abbreviation } : {},
  };
  const installed = (a: AppEntry) => (!a.module || modules.some((m) => m.id === a.module)) && can(me, a.permission);

  return (
    <div className="launchpad">
      <div className="launchpad-head">
        <h1>{t("dashboard.welcome", { name: me.fullName ?? me.username })}</h1>
        <p className="muted">
          {formatEthiopian(today, am)} · {t("dashboard.fiscalYearValue", { year: fiscalYear })}{company ? ` · ${company.name}` : ""}
        </p>
      </div>
      {GROUPS.map((g) => {
        const apps = APPS.filter((a) => a.group === g && installed(a));
        return apps.length ? (
          <section key={g} className="tile-group">
            <h2>{t(`nav.${g}`)}</h2>
            <div className="tiles">
              {apps.map((a) => (
                <Tile key={a.id} title={t(a.title)} subtitle={a.subtitle ? t(a.subtitle) : undefined} {...kpi[a.id]} onOpen={() => void navigate({ to: a.path })} />
              ))}
            </div>
          </section>
        ) : null;
      })}
    </div>
  );
}
