import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate } from "../../lib/ethiopian";
import type { LeaveApplicationDto, MyLeaveDto } from "../../lib/types";
import { can } from "../../lib/types";
import { Dialog, DynamicPage, MessageStrip, ObjectStatus, statusTone } from "../../ui/fiori";

const TYPES = ["Annual", "Sick", "Maternity", "Paternity", "Marriage", "Bereavement", "Unpaid"] as const;

/** My leave: balances, my applications, and applying for leave for myself (HR approves it). */
export function MyLeave() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const [applying, setApplying] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const leave = useQuery({ queryKey: ["my-leave"], queryFn: () => api<MyLeaveDto>("/api/hr/my/leave"), retry: false });
  const apply = useMutation({
    mutationFn: (body: Record<string, string>) => post<LeaveApplicationDto>("/api/hr/my/leave-applications", { ...body, reason: body.reason || null }),
    onSuccess: (a) => { setApplying(false); setMessage(t("me.leaveApplied", { days: a.days })); void queryClient.invalidateQueries({ queryKey: ["my-leave"] }); },
  });
  if (leave.isError) return <MessageStrip tone="information">{leave.error.message}</MessageStrip>;
  const am = i18n.language === "am";
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); apply.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  return (
    <DynamicPage
      title={t("nav.myLeave")}
      actions={can(me, "hr.self.apply") && <button onClick={() => { apply.reset(); setApplying(true); }}>{t("leave.apply")}</button>}
      header={leave.data && (
        <div className="kpis kpis-gap">
          {leave.data.balances.filter((b) => b.type === "Annual" || b.taken > 0).map((b) => (
            <div key={b.type} className="kpi">
              <div className="kpi-label">{t(`leave.types.${b.type}`)}</div>
              <div className="kpi-value">{b.type === "Annual" ? b.balance : b.taken}</div>
              <div className="muted small">{b.type === "Annual" ? t("me.daysLeft") : t("me.daysTaken")}</div>
            </div>
          ))}
        </div>
      )}
    >
      {message && <MessageStrip tone="positive" onClose={() => setMessage(null)}>{message}</MessageStrip>}
      {leave.data?.applications.length ? (
        <section className="table-card">
          <div className="table-toolbar"><h2>{t("leave.applications")}</h2></div>
          <table className="table responsive">
            <thead><tr><th>{t("leave.type")}</th><th>{t("leave.from")}</th><th>{t("leave.to")}</th><th className="num">{t("leave.days")}</th><th>{t("fields.status")}</th></tr></thead>
            <tbody>
              {leave.data.applications.map((a) => (
                <tr key={a.id}>
                  <td data-label={t("leave.type")}>{t(`leave.types.${a.type}`)}</td>
                  <td data-label={t("leave.from")}>{formatDate(a.fromDate, me.calendar, am)}</td>
                  <td data-label={t("leave.to")}>{formatDate(a.toDate, me.calendar, am)}</td>
                  <td data-label={t("leave.days")} className="num">{a.days}</td>
                  <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(a.status)}>{t(`status.${a.status}`, a.status)}</ObjectStatus></td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ) : <div className="empty">{leave.isPending ? t("common.loading") : t("me.noLeave")}</div>}

      <Dialog open={applying} title={t("leave.apply")} onClose={() => setApplying(false)}
        footer={<><button type="submit" form="my-leave-form" disabled={apply.isPending}>{t("leave.apply")}</button><button className="secondary" onClick={() => setApplying(false)}>{t("common.cancel")}</button></>}>
        <form id="my-leave-form" className="dialog-form" onSubmit={onSubmit}>
          <p className="muted small">{t("me.applyHint")}</p>
          <label>{t("leave.type")}<select name="type">{TYPES.map((x) => <option key={x} value={x}>{t(`leave.types.${x}`)}</option>)}</select></label>
          <label>{t("leave.from")}<span className="required" aria-hidden>*</span><input type="date" name="fromDate" required /></label>
          <label>{t("leave.to")}<span className="required" aria-hidden>*</span><input type="date" name="toDate" required /></label>
          <label>{t("leave.reason")}<input name="reason" /></label>
          {apply.isError && <MessageStrip tone="negative">{apply.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </DynamicPage>
  );
}
