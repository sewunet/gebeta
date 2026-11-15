import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { can, type AccountDto } from "../../lib/types";
import { FilterBar, FilterField, ListReport, MessageStrip } from "../../ui/fiori";

/** The chart of accounts as a tree, with search and a filter by type. */
export function Accounts() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState("");
  const [rootType, setRootType] = useState("");
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const setUp = useMutation({
    mutationFn: () => post(`/api/accounting/companies/${company!.id}/chart`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["accounts"] }),
  });
  const byId = useMemo(() => new Map(accounts.data?.map((a) => [a.id, a])), [accounts.data]);
  const rows = (accounts.data ?? []).filter((a) => (!rootType || a.rootType === rootType)
    && (!search.trim() || `${a.code} ${a.name}`.toLowerCase().includes(search.trim().toLowerCase())));
  if (!company) return <p>{t("common.noCompany")}</p>;
  const depth = (a: AccountDto): number => (a.parentId && byId.get(a.parentId) ? 1 + depth(byId.get(a.parentId)!) : 0);

  return (
    <ListReport
      title={t("nav.accounts")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("acc.accountSearchHint")} /></FilterField>
          <FilterField label={t("acc.rootType")}>
            <select value={rootType} onChange={(e) => setRootType(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Asset", "Liability", "Equity", "Income", "Expense"].map((r) => <option key={r} value={r}>{t(`acc.rootTypes.${r}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.accounts")}
      count={rows.length}
      messages={accounts.data && accounts.data.length === 0 && (
        <MessageStrip tone="information">
          {t("acc.noChart")}{" "}
          {can(me, "accounting.accounts.manage") && <button onClick={() => setUp.mutate()} disabled={setUp.isPending}>{t("acc.setUpChart")}</button>}
        </MessageStrip>
      )}
    >
      {rows.length ? (
        <table className="table">
          <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("acc.rootType")}</th></tr></thead>
          <tbody>
            {rows.map((a) => (
              <tr key={a.id} className={a.isGroup ? "strong" : ""}>
                <td className="mono">{a.code}</td>
                <td style={{ paddingLeft: 12 + (search || rootType ? 0 : depth(a) * 18) }}>{a.name}</td>
                <td>{t(`acc.rootTypes.${a.rootType}`)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{accounts.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}
