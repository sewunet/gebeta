import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import { can, type SalarySlipDto } from "../../lib/types";
import { Fields, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../ui/fiori";
import { historySection } from "../../app/history";

/** Object page of one salary slip: pay figures in the header, earnings, deductions and employer contributions below. */
export function SlipDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const { id } = useParams({ from: "/payroll/slips/$id" });
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<string | null>(null);
  const slip = useQuery({ queryKey: ["slip", id], queryFn: () => api<SalarySlipDto>(`/api/payroll/salary-slips/${id}`) });
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<SalarySlipDto>(`/api/payroll/salary-slips/${id}/${action}`),
    onSuccess: (updated) => {
      queryClient.setQueryData(["slip", id], updated);
      void queryClient.invalidateQueries({ queryKey: ["slips"] });
      setNotice(t(updated.status === "Cancelled" ? "slips.cancelled" : "slips.submitted", { number: updated.number }));
    },
  });

  if (slip.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (slip.isError) return <MessageStrip tone="negative">{slip.error.message}</MessageStrip>;
  const s = slip.data;
  const am = i18n.language === "am";
  const lines = (type: string) => s.lines.filter((l) => l.type === type);
  const table = (type: string, total?: number) => {
    const rows = lines(type);
    return rows.length ? (
      <table className="table">
        <tbody>{rows.map((l, i) => (
          <tr key={`${l.abbreviation}-${i}`}><td title={l.formula}>{l.component}</td><td className="mono muted small">{l.abbreviation}</td><td className="num">{formatMoney(l.amount)}</td></tr>
        ))}</tbody>
        {total !== undefined && <tfoot><tr><td colSpan={2}>{t("common.total")}</td><td className="num">{formatMoney(total)}</td></tr></tfoot>}
      </table>
    ) : <div className="empty">{t("common.empty")}</div>;
  };

  return (
    <ObjectPage
      breadcrumb={<Link to="/payroll/slips">{t("nav.salarySlips")}</Link>}
      title={s.employeeName}
      subtitle={<><span className="mono">{s.number}</span> · {s.ethiopianPeriod}</>}
      status={<ObjectStatus tone={statusTone(s.status)}>{t(`status.${s.status}`)}</ObjectStatus>}
      kpis={[
        { label: t("fields.gross"), value: formatMoney(s.grossPay) },
        { label: t("fields.deductions"), value: formatMoney(s.totalDeductions) },
        { label: t("fields.net"), value: formatMoney(s.netPay), tone: "positive" },
        { label: t("fields.employer"), value: formatMoney(s.employerContributions) },
      ]}
      actions={<>
        {s.status === "Draft" && can(me, "payroll.salary-slips.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {s.status === "Submitted" && !s.isHistory && can(me, "payroll.salary-slips.cancel") && <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("slips.cancelSlip")}</button>}
      </>}
      messages={<>
        {s.isHistory && <MessageStrip tone="information">{t("slips.history")}</MessageStrip>}
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
      </>}
      sections={[
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("fields.employee"), value: <>{s.employeeName} <span className="mono muted">{s.employeeNumber}</span></> },
            { label: t("fields.period"), value: `${formatDate(s.periodStart, me.calendar, am)} – ${formatDate(s.periodEnd, me.calendar, am)}` },
            { label: t("slips.structure"), value: s.structureName },
            { label: t("fields.paymentDays"), value: `${s.paymentDays} / ${s.totalWorkingDays}` },
            { label: t("fields.taxTable"), value: s.taxTable },
          ]} />
        ) },
        { id: "earnings", title: t("slips.earnings"), content: table("Earning", s.grossPay) },
        { id: "deductions", title: t("fields.deductions"), content: table("Deduction", s.totalDeductions) },
        { id: "employer", title: t("fields.employer"), content: table("EmployerContribution", s.employerContributions) },
        ...historySection(t, me, id, "SalarySlip")
      ]}
    />
  );
}
