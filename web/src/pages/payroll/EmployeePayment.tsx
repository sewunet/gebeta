import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, put } from "../../lib/api";
import { useCostCenters } from "../../lib/queries";
import { can, type PayrollProfileDto } from "../../lib/types";
import { Fields, MessageStrip } from "../../ui/fiori";

/** How the employee is paid (bank transfer or cash), their pension number, and the cost centre their salary cost is reported under. */
export function EmployeePayment({ employeeId }: { employeeId: string }) {
  const { t } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [method, setMethod] = useState<PayrollProfileDto["paymentMethod"]>("BankTransfer");
  const centers = useCostCenters();
  const profile = useQuery({ queryKey: ["payroll-profile", employeeId], queryFn: () => api<PayrollProfileDto>(`/api/payroll/employees/${employeeId}/profile`) });
  const save = useMutation({
    mutationFn: (b: Record<string, string>) => put<PayrollProfileDto>(`/api/payroll/employees/${employeeId}/profile`, {
      paymentMethod: method, bankName: method === "Cash" ? null : b.bankName || null, bankAccountNumber: method === "Cash" ? null : b.bankAccountNumber || null,
      pensionNumber: b.pensionNumber || null, costCenterId: b.costCenterId || null,
    }),
    onSuccess: (p) => { queryClient.setQueryData(["payroll-profile", employeeId], p); setEditing(false); },
  });
  if (profile.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (profile.isError) return <MessageStrip tone="negative">{profile.error.message}</MessageStrip>;
  const p = profile.data;
  const centerName = (id?: string) => {
    const c = centers.data?.find((x) => x.id === id) ?? centers.data?.find((x) => x.isMain);
    return c ? `${c.code} · ${c.name}` : "—";
  };
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); save.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  if (!editing) {
    return (
      <>
        <Fields items={[
          { label: t("payment.method"), value: t(`payment.${p.paymentMethod}`) },
          { label: t("payment.bank"), value: p.bankName, hidden: p.paymentMethod === "Cash" },
          { label: t("payment.account"), value: p.bankAccountNumber ? <span className="mono">{p.bankAccountNumber}</span> : <span className="error">{t("payment.missing")}</span>, hidden: p.paymentMethod === "Cash" },
          { label: t("payment.pension"), value: p.pensionNumber ?? "—" },
          { label: t("nav.costCenters"), value: centerName(p.costCenterId), hidden: !centers.data },
        ]} />
        {can(me, "payroll.profiles.manage") && <button className="secondary" onClick={() => { setMethod(p.paymentMethod); setEditing(true); }}>{t("ui.edit")}</button>}
      </>
    );
  }

  return (
    <form className="form-columns" onSubmit={onSubmit}>
      <label>{t("payment.method")}
        <select value={method} onChange={(e) => setMethod(e.target.value as PayrollProfileDto["paymentMethod"])}>
          <option value="BankTransfer">{t("payment.BankTransfer")}</option><option value="Cash">{t("payment.Cash")}</option>
        </select>
      </label>
      {method === "BankTransfer" && <>
        <label>{t("payment.bank")}<span className="required" aria-hidden>*</span><input name="bankName" required defaultValue={p.bankName ?? ""} placeholder="CBE" /></label>
        <label>{t("payment.account")}<span className="required" aria-hidden>*</span>
          <input name="bankAccountNumber" required inputMode="numeric" pattern="\d{6,30}" className="mono" defaultValue={p.bankAccountNumber ?? ""} /></label>
      </>}
      <label>{t("payment.pension")}<input name="pensionNumber" defaultValue={p.pensionNumber ?? ""} /></label>
      {centers.data && (
        <label>{t("nav.costCenters")}
          <select name="costCenterId" defaultValue={p.costCenterId ?? ""}>
            <option value="">{t("payment.mainDefault")}</option>
            {centers.data.filter((c) => c.isActive && !c.isMain).map((c) => <option key={c.id} value={c.id}>{c.code} · {c.name}</option>)}
          </select>
        </label>
      )}
      <div className="inline-fields" style={{ gridColumn: "1 / -1" }}>
        <button type="submit" disabled={save.isPending}>{t("common.save")}</button>
        <button type="button" className="secondary" onClick={() => setEditing(false)}>{t("common.cancel")}</button>
        {save.isError && <span className="error">{save.error.message}</span>}
      </div>
    </form>
  );
}
