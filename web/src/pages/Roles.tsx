import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useRoles, useUsers } from "../lib/queries";
import { ListReport, MessageStrip, ObjectPage, ObjectStatus } from "../ui/fiori";

/** The roles and what each allows. System roles come with Gebeta; their permissions are kept up to date on every release. */
export function Roles() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const roles = useRoles();
  const users = useUsers();
  const holders = (name: string) => users.data?.filter((u) => u.roles.includes(name)).length;
  const rows = roles.data ?? [];

  return (
    <ListReport title={t("nav.roles")} tableTitle={t("nav.roles")} count={rows.length}>
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.name")}</th><th>{t("roles.description")}</th><th className="num">{t("nav.users")}</th><th>{t("fields.type")}</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="navigable" onClick={() => void navigate({ to: "/roles/$id", params: { id: r.id } })}>
                <td data-label={t("fields.name")}><span className="strong-link">{r.name}</span></td>
                <td data-label={t("roles.description")} className="small">{r.description ?? "—"}</td>
                <td data-label={t("nav.users")} className="num">{holders(r.name) ?? "—"}</td>
                <td data-label={t("fields.type")}>{r.isSystem ? <ObjectStatus tone="information">{t("roles.system")}</ObjectStatus> : t("roles.custom")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{roles.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

/** One role: its permissions (a trailing * covers everything in that area) and who holds it. */
export function RoleDetail() {
  const { t } = useTranslation();
  const { id } = useParams({ from: "/roles/$id" });
  const roles = useRoles();
  const users = useUsers();
  if (roles.isPending) return <p className="muted">{t("common.loading")}</p>;
  const r = roles.data?.find((x) => x.id === id);
  if (!r) return <MessageStrip tone="negative">{t("roles.notFound")}</MessageStrip>;
  const holders = users.data?.filter((u) => u.roles.includes(r.name)) ?? [];

  return (
    <ObjectPage
      breadcrumb={<Link to="/roles">{t("nav.roles")}</Link>}
      title={r.name}
      subtitle={r.description}
      status={r.isSystem ? <ObjectStatus tone="information">{t("roles.system")}</ObjectStatus> : undefined}
      kpis={[{ label: t("roles.permissions"), value: String(r.permissions.length) }, { label: t("nav.users"), value: String(holders.length) }]}
      sections={[
        { id: "permissions", title: t("roles.permissions"), content: (
          <>
            <p className="muted small">{t("roles.permissionsHint")}</p>
            <ul className="plain-list mono small">{[...r.permissions].sort().map((p) => <li key={p}>{p}</li>)}</ul>
          </>
        ) },
        { id: "users", title: t("nav.users"), content: holders.length ? (
          <ul className="plain-list">{holders.map((u) => <li key={u.id}><Link to="/users/$id" params={{ id: u.id }}>{u.fullName ?? u.username}</Link> <span className="muted small mono">{u.username}</span></li>)}</ul>
        ) : <p className="muted">{t("common.empty")}</p> },
      ]}
    />
  );
}
