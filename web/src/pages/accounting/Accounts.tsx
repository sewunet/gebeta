import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { can, type AccountDto } from "../../lib/types";

export function Accounts() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const accounts = useQuery({
    queryKey: ["accounts", company?.id],
    queryFn: () => api<AccountDto[]>(`/api/accounting/accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
  const setUp = useMutation({
    mutationFn: () => post(`/api/accounting/companies/${company!.id}/chart`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["accounts"] }),
  });
  if (!company) return <p>{t("common.noCompany")}</p>;

  // Indent by depth in the tree.
  const byId = new Map(accounts.data?.map((a) => [a.id, a]));
  const depth = (a: AccountDto): number => (a.parentId && byId.get(a.parentId) ? 1 + depth(byId.get(a.parentId)!) : 0);

  return (
    <>
      <h1>{t("nav.accounts")}</h1>
      {accounts.data && accounts.data.length === 0 && (
        <div className="notice">
          {t("acc.noChart")}{" "}
          {can(me, "accounting.accounts.manage") && <button onClick={() => setUp.mutate()} disabled={setUp.isPending}>{t("acc.setUpChart")}</button>}
        </div>
      )}
      {!!accounts.data?.length && (
        <table className="table narrow">
          <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("acc.rootType")}</th></tr></thead>
          <tbody>
            {accounts.data.map((a) => (
              <tr key={a.id} className={a.isGroup ? "strong" : ""}>
                <td className="mono">{a.code}</td>
                <td style={{ paddingLeft: 12 + depth(a) * 18 }}>{a.name}</td>
                <td>{a.rootType}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}
