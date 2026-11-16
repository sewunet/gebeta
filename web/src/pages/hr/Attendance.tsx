import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, put } from "../../lib/api";
import { formatDate, todayIso } from "../../lib/ethiopian";
import { formData, useEmployees } from "../../lib/hooks";
import { can, type AttendanceDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus, type Tone } from "../../ui/fiori";

export function Attendance() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [month, setMonth] = useState(currentMonth());
  const [employeeId, setEmployeeId] = useState("");
  const [status, setStatus] = useState("");
  const [marking, setMarking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const { start, end } = monthRange(month);
  const records = useQuery({
    queryKey: ["attendance", company?.id, month],
    queryFn: () => api<AttendanceDto[]>(`/api/hr/attendance?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const mark = useMutation({
    mutationFn: (body: Record<string, string>) => put<AttendanceDto>("/api/hr/attendance", body),
    onSuccess: () => { setMarking(false); setNotice(t("attendance.saved")); void queryClient.invalidateQueries({ queryKey: ["attendance"] }); },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const names = new Map(employees.data?.map((x) => [x.id, `${x.number} · ${x.fullName}`]));
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    mark.mutate(formData(event.currentTarget));
  };

  const rows = (records.data ?? []).filter((r) => (!employeeId || r.employeeId === employeeId) && (!status || r.status === status));
  const tone: Record<string, Tone> = { Present: "positive", HalfDay: "critical", Absent: "negative" };

  return (
    <ListReport
      title={t("nav.attendance")}
      filters={
        <FilterBar>
          <FilterField label={t("fields.employee")}>
            <select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Absent", "HalfDay", "Present"].map((s) => <option key={s} value={s}>{t(`attendance.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("common.month")}><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("attendance.records")}
      count={rows.length}
      messages={<>
        <MessageStrip tone="information">{t("attendance.explain")}</MessageStrip>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      </>}
      toolbar={can(me, "hr.attendance.manage") && <button onClick={() => setMarking(true)}>{t("attendance.mark")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("attendance.date")}</th><th>{t("fields.employee")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={`${r.employeeId}-${r.date}`}>
                <td data-label={t("attendance.date")}>{formatDate(r.date, me.calendar, i18n.language === "am")}</td>
                <td data-label={t("fields.employee")}>{names.get(r.employeeId) ?? r.employeeId}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={tone[r.status] ?? "neutral"}>{t(`attendance.${r.status}`)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{records.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={marking} title={t("attendance.mark")} onClose={() => setMarking(false)}
        footer={<><button type="submit" form="attendance-form" disabled={mark.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setMarking(false)}>{t("common.cancel")}</button></>}>
        <form id="attendance-form" className="dialog-form" onSubmit={onSubmit}>
          <label>{t("fields.employee")}
            <select name="employeeId" required defaultValue={employeeId}>{employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}</select>
          </label>
          <label>{t("attendance.date")}<input type="date" name="date" defaultValue={todayIso()} required /></label>
          <label>{t("fields.status")}
            <select name="status">{["Absent", "HalfDay", "Present"].map((s) => <option key={s} value={s}>{t(`attendance.${s}`)}</option>)}</select>
          </label>
          {mark.isError && <MessageStrip tone="negative">{mark.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
