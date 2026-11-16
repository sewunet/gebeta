import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api, post, put } from "../lib/api";
import { formatDate } from "../lib/ethiopian";
import { useEmployees } from "../lib/hooks";
import { can, type EmployeeDto } from "../lib/types";
import { Dialog, EditFooter, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../ui/fiori";

/** List report of employees with search and filters; "Create" opens a dialog, a row opens the employee. */
export function Employees() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [adding, setAdding] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("Active");
  const [gender, setGender] = useState("");
  const employees = useEmployees();
  const add = useMutation({
    mutationFn: (body: Record<string, string>) => post<EmployeeDto>("/api/core/employees", { ...body, companyId: company!.id, tin: body.tin || null, grandfatherName: body.grandfatherName || null }),
    onSuccess: (e) => { setMessage(t("employees.added", { number: e.number })); setAdding(false); void queryClient.invalidateQueries({ queryKey: ["employees", company?.id] }); },
  });
  const rows = useMemo(() => (employees.data ?? []).filter((e) => (!status || e.status === status) && (!gender || e.gender === gender)
    && (!search.trim() || [e.number, e.fullName, e.tin].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [employees.data, status, gender, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onSubmit = (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); add.mutate(Object.fromEntries(new FormData(event.currentTarget)) as Record<string, string>); };

  return (
    <ListReport
      title={t("nav.employees")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("employees.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Active", "Suspended", "Left"].map((s) => <option key={s} value={s}>{t(`status.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.gender")}>
            <select value={gender} onChange={(e) => setGender(e.target.value)}>
              <option value="">{t("ui.all")}</option><option value="Female">{t("fields.female")}</option><option value="Male">{t("fields.male")}</option>
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.employees")}
      count={rows.length}
      messages={message && <MessageStrip tone="positive" onClose={() => setMessage(null)}>{message}</MessageStrip>}
      toolbar={can(me, "core.employees.manage") && <button onClick={() => setAdding(true)}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("fields.name")}</th><th>{t("fields.gender")}</th><th>{t("fields.joined")}</th><th>{t("fields.tin")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((e) => (
              <tr key={e.id} className="navigable" onClick={() => void navigate({ to: "/employees/$id", params: { id: e.id } })}>
                <td data-label={t("fields.number")} className="mono">{e.number}</td>
                <td data-label={t("fields.name")}><span className="strong-link">{e.fullName}</span></td>
                <td data-label={t("fields.gender")}>{t(`fields.${e.gender.toLowerCase()}`)}</td>
                <td data-label={t("fields.joined")}>{formatDate(e.dateOfJoining, me.calendar, i18n.language === "am")}</td>
                <td data-label={t("fields.tin")} className="mono">{e.tin ?? "—"}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(e.status)}>{t(`status.${e.status}`)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{employees.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={adding} title={t("employees.add")} onClose={() => setAdding(false)}
        footer={<><button type="submit" form="employee-form" disabled={add.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setAdding(false)}>{t("common.cancel")}</button></>}>
        <form id="employee-form" className="dialog-form" onSubmit={onSubmit}>
          <label>{t("fields.firstName")}<span className="required" aria-hidden>*</span><input name="firstName" required /></label>
          <label>{t("fields.fatherName")}<span className="required" aria-hidden>*</span><input name="fatherName" required /></label>
          <label>{t("fields.grandfatherName")}<input name="grandfatherName" /></label>
          <label>{t("fields.gender")}<select name="gender"><option value="Female">{t("fields.female")}</option><option value="Male">{t("fields.male")}</option></select></label>
          <label>{t("fields.joined")}<span className="required" aria-hidden>*</span><input name="dateOfJoining" type="date" required /></label>
          <label>{t("fields.tin")}<input name="tin" inputMode="numeric" maxLength={12} /></label>
          {add.isError && <MessageStrip tone="negative">{add.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}

/** Object page of one employee: edit mode for names, gender, joining date and TIN; record or take back a leaving. */
export function EmployeeDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/employees/$id" });
  const [editing, setEditing] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const query = useQuery({ queryKey: ["employee", id], queryFn: () => api<EmployeeDto>(`/api/core/employees/${id}`) });
  const done = (e: EmployeeDto, message: string) => {
    queryClient.setQueryData(["employee", id], e);
    void queryClient.invalidateQueries({ queryKey: ["employees"] });
    setEditing(false); setLeaving(false); setNotice(message);
  };
  const save = useMutation({
    mutationFn: (b: Record<string, string>) => put<EmployeeDto>(`/api/core/employees/${id}`, { ...b, grandfatherName: b.grandfatherName || null, tin: b.tin || null }),
    onSuccess: (e) => done(e, t("ui.saved")),
  });
  const setLeavingDate = useMutation({
    mutationFn: (dateOfLeaving: string | null) => post<EmployeeDto>(`/api/core/employees/${id}/leaving`, { dateOfLeaving }),
    onSuccess: (e) => done(e, e.status === "Left" ? t("employees.leftSaved", { name: e.fullName }) : t("employees.rejoined", { name: e.fullName })),
  });
  if (query.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (query.isError) return <MessageStrip tone="negative">{t("employees.notFound")}</MessageStrip>;
  const e = query.data;
  const am = i18n.language === "am";
  const manage = can(me, "core.employees.manage");
  const onSave = (ev: FormEvent<HTMLFormElement>) => { ev.preventDefault(); save.mutate(Object.fromEntries(new FormData(ev.currentTarget)) as Record<string, string>); };

  return (
    <>
      <ObjectPage
        breadcrumb={<Link to="/employees">{t("nav.employees")}</Link>}
        title={e.fullName}
        subtitle={<span className="mono">{e.number}</span>}
        status={<ObjectStatus tone={statusTone(e.status)}>{t(`status.${e.status}`)}</ObjectStatus>}
        kpis={[
          { label: t("fields.joined"), value: formatDate(e.dateOfJoining, me.calendar, am) },
          ...(e.dateOfLeaving ? [{ label: t("employees.left"), value: formatDate(e.dateOfLeaving, me.calendar, am), tone: "negative" as const }] : []),
          { label: t("fields.gender"), value: t(`fields.${e.gender.toLowerCase()}`) },
          { label: t("fields.tin"), value: e.tin ? <span className="mono">{e.tin}</span> : "—" },
        ]}
        actions={manage && !editing && <>
          <button onClick={() => { setNotice(null); setEditing(true); }}>{t("ui.edit")}</button>
          {e.status === "Left"
            ? <button className="secondary" onClick={() => setLeavingDate.mutate(null)} disabled={setLeavingDate.isPending}>{t("employees.takeBack")}</button>
            : <button className="secondary" onClick={() => setLeaving(true)}>{t("employees.recordLeaving")}</button>}
        </>}
        footer={editing && <EditFooter form="employee-edit" saving={save.isPending} error={save.error?.message} onCancel={() => setEditing(false)}
          saveLabel={t("common.save")} cancelLabel={t("common.cancel")} />}
        messages={<>
          {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
          {setLeavingDate.isError && !leaving && <MessageStrip tone="negative">{setLeavingDate.error.message}</MessageStrip>}
        </>}
        sections={[{ id: "general", title: t("ui.general"), content: editing ? (
          <form id="employee-edit" className="form-columns" onSubmit={onSave}>
            <label>{t("fields.firstName")}<span className="required" aria-hidden>*</span><input name="firstName" defaultValue={e.firstName} required /></label>
            <label>{t("fields.fatherName")}<span className="required" aria-hidden>*</span><input name="fatherName" defaultValue={e.fatherName} required /></label>
            <label>{t("fields.grandfatherName")}<input name="grandfatherName" defaultValue={e.grandfatherName ?? ""} /></label>
            <label>{t("fields.gender")}<select name="gender" defaultValue={e.gender}><option value="Female">{t("fields.female")}</option><option value="Male">{t("fields.male")}</option></select></label>
            <label>{t("fields.joined")}<span className="required" aria-hidden>*</span><input name="dateOfJoining" type="date" defaultValue={e.dateOfJoining} required /></label>
            <label>{t("fields.tin")}<input name="tin" inputMode="numeric" maxLength={12} defaultValue={e.tin ?? ""} /></label>
          </form>
        ) : (
          <Fields items={[
            { label: t("fields.firstName"), value: e.firstName },
            { label: t("fields.fatherName"), value: e.fatherName },
            { label: t("fields.grandfatherName"), value: e.grandfatherName },
            { label: t("fields.joined"), value: formatDate(e.dateOfJoining, me.calendar, am) },
            { label: t("employees.left"), value: e.dateOfLeaving && formatDate(e.dateOfLeaving, me.calendar, am), hidden: !e.dateOfLeaving },
            { label: t("fields.tin"), value: e.tin && <span className="mono">{e.tin}</span> },
          ]} />
        ) }]}
      />
      <Dialog open={leaving} title={t("employees.recordLeaving")} onClose={() => setLeaving(false)}
        footer={<><button type="submit" form="employee-leaving" className="danger" disabled={setLeavingDate.isPending}>{t("common.save")}</button>
          <button className="secondary" onClick={() => setLeaving(false)}>{t("common.cancel")}</button></>}>
        <form id="employee-leaving" className="dialog-form" onSubmit={(ev) => { ev.preventDefault(); setLeavingDate.mutate(String(new FormData(ev.currentTarget).get("date"))); }}>
          <p className="muted small">{t("employees.leavingHint")}</p>
          <label>{t("employees.left")}<input type="date" name="date" min={e.dateOfJoining} required /></label>
          {setLeavingDate.isError && <MessageStrip tone="negative">{setLeavingDate.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </>
  );
}
