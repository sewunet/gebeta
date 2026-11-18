import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api, post, put } from "../lib/api";
import { useEmployees } from "../lib/hooks";
import { seesAllCompanies, useRoles, useUsers } from "../lib/queries";
import { can, type UserDto } from "../lib/types";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../ui/fiori";
import { userStatus } from "./userStatus";

/** List report of users with search and status; "Invite" sets someone up before their first sign-in. */
export function Users() {
  const { t } = useTranslation();
  const { me, companies } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const users = useUsers();
  const roles = useRoles();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [inviting, setInviting] = useState(false);
  const [picked, setPicked] = useState<{ roles: string[]; companies: string[] }>({ roles: [], companies: [] });
  const [message, setMessage] = useState<string | null>(null);
  const invite = useMutation({
    mutationFn: (body: Record<string, string>) => post<UserDto>("/api/core/users", {
      username: body.username, email: body.email, fullName: body.fullName || null, roles: picked.roles, companyIds: picked.companies,
    }),
    onSuccess: (u) => { setInviting(false); setMessage(t("users.invited", { username: u.username })); void queryClient.invalidateQueries({ queryKey: ["users"] }); },
  });
  const rows = useMemo(() => (users.data ?? []).filter((u) => (!status || userStatus(u).key === status)
    && (!search.trim() || [u.username, u.fullName, u.email].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [users.data, status, search]);
  const toggle = (list: "roles" | "companies", value: string) =>
    setPicked((p) => ({ ...p, [list]: p[list].includes(value) ? p[list].filter((v) => v !== value) : [...p[list], value] }));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); invite.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  return (
    <ListReport
      title={t("nav.users")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("users.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {(["Active", "Invited", "Inactive"] as const).map((s) => <option key={s} value={s}>{t(`users.status.${s}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.users")}
      count={rows.length}
      messages={message && <MessageStrip tone="positive" onClose={() => setMessage(null)}>{message}</MessageStrip>}
      toolbar={can(me, "core.users.manage") && <button onClick={() => { invite.reset(); setPicked({ roles: [], companies: [] }); setInviting(true); }}>{t("users.invite")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("users.username")}</th><th>{t("fields.name")}</th><th>{t("users.roles")}</th><th>{t("nav.companies")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((u) => {
              const s = userStatus(u);
              return (
                <tr key={u.id} className="navigable" onClick={() => void navigate({ to: "/users/$id", params: { id: u.id } })}>
                  <td data-label={t("users.username")} className="mono"><span className="strong-link">{u.username}</span></td>
                  <td data-label={t("fields.name")}>{u.fullName ?? "—"}</td>
                  <td data-label={t("users.roles")} className="small">{u.roles.length ? u.roles.join(", ") : <span className="muted">{t("users.noRoles")}</span>}</td>
                  <td data-label={t("nav.companies")}>{u.companyIds.length}</td>
                  <td data-label={t("fields.status")}><ObjectStatus tone={s.tone}>{t(`users.status.${s.key}`)}</ObjectStatus></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      ) : <div className="empty">{users.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={inviting} title={t("users.invite")} onClose={() => setInviting(false)}
        footer={<><button type="submit" form="invite-form" disabled={invite.isPending}>{t("users.invite")}</button><button className="secondary" onClick={() => setInviting(false)}>{t("common.cancel")}</button></>}>
        <form id="invite-form" className="dialog-form" onSubmit={onSubmit}>
          <p className="muted small">{t("users.inviteHint")}</p>
          <label>{t("users.username")}<span className="required" aria-hidden>*</span><input name="username" required autoComplete="off" /></label>
          <label>{t("users.email")}<span className="required" aria-hidden>*</span><input name="email" type="email" required /></label>
          <label>{t("fields.name")}<input name="fullName" /></label>
          <fieldset className="check-list">
            <legend>{t("users.roles")}</legend>
            {roles.data?.map((r) => <label key={r.id} className="check"><input type="checkbox" checked={picked.roles.includes(r.name)} onChange={() => toggle("roles", r.name)} /> {r.name}</label>)}
          </fieldset>
          <fieldset className="check-list">
            <legend>{t("nav.companies")}</legend>
            {companies.map((c) => <label key={c.id} className="check"><input type="checkbox" checked={picked.companies.includes(c.id)} onChange={() => toggle("companies", c.id)} /> {c.name}</label>)}
          </fieldset>
          {invite.isError && <MessageStrip tone="negative">{invite.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}

/** Object page of one user: roles and companies to add or take away, and access to stop or restore. */
export function UserDetail() {
  const { t } = useTranslation();
  const { me, companies } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/users/$id" });
  const users = useUsers();
  const roles = useRoles();
  const [role, setRole] = useState("");
  const [company, setCompany] = useState("");
  const [employee, setEmployee] = useState("");
  const employees = useEmployees();
  const link = useMutation({
    mutationFn: (employeeId: string | null) => put<UserDto>(`/api/core/users/${id}/employee`, { employeeId }),
    onSuccess: (u) => { queryClient.setQueryData<UserDto[]>(["users"], (all) => all?.map((x) => (x.id === u.id ? u : x))); setEmployee(""); },
  });
  const change = useMutation({
    mutationFn: ({ path, method }: { path: string; method: "PUT" | "DELETE" | "POST" }) => api<UserDto>(`/api/core/users/${id}/${path}`, { method }),
    onSuccess: (u) => { queryClient.setQueryData<UserDto[]>(["users"], (all) => all?.map((x) => (x.id === u.id ? u : x))); setRole(""); setCompany(""); },
  });
  if (users.isPending) return <p className="muted">{t("common.loading")}</p>;
  const u = users.data?.find((x) => x.id === id);
  if (!u) return <MessageStrip tone="negative">{t("users.notFound")}</MessageStrip>;
  const manage = can(me, "core.users.manage");
  const s = userStatus(u);
  const roleByName = new Map((roles.data ?? []).map((r) => [r.name, r]));
  const allCompanies = u.roles.some((r) => seesAllCompanies(roleByName.get(r)));
  const companyName = (cid: string) => companies.find((c) => c.id === cid)?.name ?? cid;
  const remove = (label: string, path: string) => manage && (
    <button type="button" className="link-button" aria-label={t("ui.remove") + " " + label} disabled={change.isPending} onClick={() => change.mutate({ path, method: "DELETE" })}>×</button>
  );

  return (
    <ObjectPage
      breadcrumb={<Link to="/users">{t("nav.users")}</Link>}
      title={u.fullName ?? u.username}
      subtitle={<span className="mono">{u.username}</span>}
      status={<ObjectStatus tone={s.tone}>{t(`users.status.${s.key}`)}</ObjectStatus>}
      kpis={[
        { label: t("users.email"), value: u.email ?? "—" },
        { label: t("users.roles"), value: String(u.roles.length) },
        { label: t("nav.companies"), value: allCompanies ? t("users.allCompanies") : String(u.companyIds.length) },
      ]}
      actions={manage && (u.isActive
        ? <button className="secondary" disabled={change.isPending || u.id === me.id} onClick={() => change.mutate({ path: "deactivate", method: "POST" })}>{t("users.deactivate")}</button>
        : <button disabled={change.isPending} onClick={() => change.mutate({ path: "activate", method: "POST" })}>{t("users.activate")}</button>)}
      messages={<>
        {u.isInvited && <MessageStrip tone="information">{t("users.invitedHint")}</MessageStrip>}
        {change.isError && <MessageStrip tone="negative">{change.error.message}</MessageStrip>}
        {link.isError && <MessageStrip tone="negative">{link.error.message}</MessageStrip>}
      </>}
      sections={[
        { id: "roles", title: t("users.roles"), content: (
          <>
            {u.roles.length ? (
              <ul className="plain-list">
                {u.roles.map((name) => {
                  const r = roleByName.get(name);
                  return <li key={name}><Link to="/roles/$id" params={{ id: r?.id ?? "" }}>{name}</Link> <span className="muted small">{r?.description}</span> {r && remove(name, `roles/${r.id}`)}</li>;
                })}
              </ul>
            ) : <p className="muted">{t("users.noRoles")}</p>}
            {manage && (
              <div className="inline-form">
                <select aria-label={t("users.addRole")} value={role} onChange={(e) => setRole(e.target.value)}>
                  <option value="">{t("users.addRole")}</option>
                  {roles.data?.filter((r) => !u.roles.includes(r.name)).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                </select>
                <button type="button" className="secondary" disabled={!role || change.isPending} onClick={() => change.mutate({ path: `roles/${role}`, method: "PUT" })}>{t("ui.add")}</button>
              </div>
            )}
          </>
        ) },
        { id: "employee", title: t("users.employee"), content: (
          <>
            <p className="muted small">{t("users.employeeHint")}</p>
            {u.employeeId ? (
              <ul className="plain-list">
                <li>
                  <Link to="/employees/$id" params={{ id: u.employeeId }}>{employees.data?.find((e) => e.id === u.employeeId)?.fullName ?? t("users.otherCompanyEmployee")}</Link>{" "}
                  {manage && <button type="button" className="link-button" disabled={link.isPending} onClick={() => link.mutate(null)}>{t("users.unlink")}</button>}
                </li>
              </ul>
            ) : manage && (
              <div className="inline-form">
                <select aria-label={t("users.employee")} value={employee} onChange={(e) => setEmployee(e.target.value)}>
                  <option value="">{t("users.pickEmployee")}</option>
                  {employees.data?.filter((e) => e.status !== "Left").map((e) => <option key={e.id} value={e.id}>{e.fullName} · {e.number}</option>)}
                </select>
                <button type="button" className="secondary" disabled={!employee || link.isPending} onClick={() => link.mutate(employee)}>{t("users.link")}</button>
              </div>
            )}
          </>
        ) },
        { id: "companies", title: t("nav.companies"), content: (
          <>
            {allCompanies && <p className="muted small">{t("users.allCompaniesHint")}</p>}
            {u.companyIds.length ? (
              <ul className="plain-list">{u.companyIds.map((cid) => <li key={cid}>{companyName(cid)} {remove(companyName(cid), `companies/${cid}`)}</li>)}</ul>
            ) : <p className="muted">{t("users.noCompanies")}</p>}
            {manage && (
              <div className="inline-form">
                <select aria-label={t("users.addCompany")} value={company} onChange={(e) => setCompany(e.target.value)}>
                  <option value="">{t("users.addCompany")}</option>
                  {companies.filter((c) => !u.companyIds.includes(c.id)).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                </select>
                <button type="button" className="secondary" disabled={!company || change.isPending} onClick={() => change.mutate({ path: `companies/${company}`, method: "PUT" })}>{t("ui.add")}</button>
              </div>
            )}
          </>
        ) },
      ]}
    />
  );
}
