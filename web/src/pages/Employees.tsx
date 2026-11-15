import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api, post } from "../lib/api";
import { formatDate } from "../lib/ethiopian";
import { can, type EmployeeDto } from "../lib/types";

export function Employees() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [showForm, setShowForm] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const employees = useQuery({
    queryKey: ["employees", company?.id],
    queryFn: () => api<EmployeeDto[]>(`/api/core/employees?companyId=${company!.id}`),
    enabled: !!company,
  });

  const add = useMutation({
    mutationFn: (body: Record<string, string>) => post<EmployeeDto>("/api/core/employees", { ...body, companyId: company!.id }),
    onSuccess: (e) => {
      setMessage(t("employees.added", { number: e.number }));
      setShowForm(false);
      void queryClient.invalidateQueries({ queryKey: ["employees", company?.id] });
    },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const data = Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>;
    add.mutate(data);
  };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.employees")}</h1>
        {can(me, "core.employees.manage") && <button onClick={() => setShowForm((s) => !s)}>{t("employees.add")}</button>}
      </div>
      {message && <div className="notice">{message}</div>}
      {showForm && (
        <form className="form-grid" onSubmit={onSubmit}>
          <label>{t("fields.firstName")}<input name="firstName" required /></label>
          <label>{t("fields.fatherName")}<input name="fatherName" required /></label>
          <label>{t("fields.grandfatherName")}<input name="grandfatherName" /></label>
          <label>{t("fields.gender")}
            <select name="gender"><option value="Female">{t("fields.female")}</option><option value="Male">{t("fields.male")}</option></select>
          </label>
          <label>{t("fields.joined")}<input name="dateOfJoining" type="date" required /></label>
          <label>{t("fields.tin")}<input name="tin" inputMode="numeric" maxLength={12} /></label>
          <div className="form-actions">
            <button type="submit" disabled={add.isPending}>{t("common.save")}</button>
            <button type="button" className="secondary" onClick={() => setShowForm(false)}>{t("common.cancel")}</button>
            {add.isError && <span className="error">{add.error.message}</span>}
          </div>
        </form>
      )}
      {employees.isPending ? <p>{t("common.loading")}</p> : employees.data?.length ? (
        <table className="table">
          <thead>
            <tr><th>{t("fields.number")}</th><th>{t("fields.name")}</th><th>{t("fields.gender")}</th><th>{t("fields.joined")}</th><th>{t("fields.tin")}</th><th>{t("fields.status")}</th></tr>
          </thead>
          <tbody>
            {employees.data.map((e) => (
              <tr key={e.id}>
                <td className="mono">{e.number}</td><td>{e.fullName}</td>
                <td>{t(`fields.${e.gender.toLowerCase()}`)}</td>
                <td>{formatDate(e.dateOfJoining, me.calendar, i18n.language === "am")}</td>
                <td className="mono">{e.tin ?? "—"}</td>
                <td><span className={`badge ${e.status}`}>{t(`status.${e.status}`)}</span></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}
    </>
  );
}
