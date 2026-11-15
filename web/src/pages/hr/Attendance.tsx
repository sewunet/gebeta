import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, put } from "../../lib/api";
import { formatDate, todayIso } from "../../lib/ethiopian";
import { formData, useEmployees } from "../../lib/hooks";
import { can, type AttendanceDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

export function Attendance() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [month, setMonth] = useState(currentMonth());
  const { start, end } = monthRange(month);
  const records = useQuery({
    queryKey: ["attendance", company?.id, month],
    queryFn: () => api<AttendanceDto[]>(`/api/hr/attendance?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const mark = useMutation({
    mutationFn: (body: Record<string, string>) => put<AttendanceDto>("/api/hr/attendance", body),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["attendance"] }),
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const names = new Map(employees.data?.map((x) => [x.id, `${x.number} · ${x.fullName}`]));
  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    mark.mutate(formData(event.currentTarget));
  };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.attendance")}</h1>
        <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(e) => setMonth(e.target.value)} /></label>
      </div>
      <p className="muted">{t("attendance.explain")}</p>
      {can(me, "hr.attendance.manage") && (
        <form className="form-grid" onSubmit={onSubmit}>
          <label>{t("fields.employee")}
            <select name="employeeId" required>{employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}</select>
          </label>
          <label>{t("attendance.date")}<input type="date" name="date" defaultValue={todayIso()} required /></label>
          <label>{t("fields.status")}
            <select name="status">{["Absent", "HalfDay", "Present"].map((s) => <option key={s} value={s}>{t(`attendance.${s}`)}</option>)}</select>
          </label>
          <div className="form-actions">
            <button type="submit" disabled={mark.isPending}>{t("attendance.mark")}</button>
            {mark.isError && <span className="error">{mark.error.message}</span>}
          </div>
        </form>
      )}
      {records.data?.length ? (
        <table className="table narrow">
          <thead><tr><th>{t("attendance.date")}</th><th>{t("fields.employee")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {records.data.map((r) => (
              <tr key={`${r.employeeId}-${r.date}`}>
                <td>{formatDate(r.date, me.calendar, i18n.language === "am")}</td><td>{names.get(r.employeeId) ?? r.employeeId}</td>
                <td>{t(`attendance.${r.status}`)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
