import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, put } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type InvoiceDto, type PartyDto } from "../../lib/types";
import { EditFooter, Fields, MessageStrip, ObjectPage, ObjectStatus } from "../../ui/fiori";
import { paymentState } from "./invoices/shared";

/** Object page of a customer or supplier: details with an edit mode, open amounts and their invoices. */
export function PartyDetail() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { id } = useParams({ from: "/accounting/parties/$id" });
  const [editing, setEditing] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const today = todayIso();
  const since = `${Number(today.slice(0, 4)) - 2}${today.slice(4)}`;
  const party = useQuery({ queryKey: ["party", id], queryFn: () => api<PartyDto>(`/api/core/parties/${id}`) });
  const invoices = useQuery({
    queryKey: ["invoices", "party", id],
    enabled: !!company && !!party.data && can(me, "accounting.invoices.read"),
    queryFn: async () => {
      const kinds = party.data!.type === "Both" ? ["Sales", "Purchase"] : [party.data!.type === "Supplier" ? "Purchase" : "Sales"];
      const all = await Promise.all(kinds.map((k) => api<InvoiceDto[]>(`/api/accounting/invoices?companyId=${company!.id}&kind=${k}&from=${since}&to=${today}`)));
      return all.flat().filter((i) => i.partyId === id).sort((a, b) => b.postingDate.localeCompare(a.postingDate));
    },
  });
  const save = useMutation({
    mutationFn: (b: Record<string, string>) => put<PartyDto>(`/api/core/parties/${id}`, {
      name: b.name, type: b.type, tin: b.tin || null, vatRegistrationNumber: b.vatRegistrationNumber || null, phone: b.phone || null,
    }),
    onSuccess: (p) => { queryClient.setQueryData(["party", id], p); void queryClient.invalidateQueries({ queryKey: ["parties"] }); setEditing(false); setNotice(t("ui.saved")); },
  });
  if (party.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (party.isError) return <MessageStrip tone="negative">{party.error.message}</MessageStrip>;
  const p = party.data;
  const am = i18n.language === "am";
  const list = invoices.data ?? [];
  const open = (kind: string) => list.filter((i) => i.kind === kind && i.status === "Submitted" && !i.isReturn).reduce((a, i) => a + i.outstanding, 0);
  const overdue = list.filter((i) => paymentState(i, today).key === "inv.overdue").reduce((a, i) => a + i.outstanding, 0);
  const onSave = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); save.mutate(formData(e.currentTarget)); };

  return (
    <ObjectPage
      breadcrumb={<Link to="/accounting/parties">{t("nav.parties")}</Link>}
      title={p.name}
      subtitle={<>{t(`inv.partyType.${p.type}`)}{p.tin && <> · <span className="mono">{p.tin}</span></>}</>}
      status={<ObjectStatus tone={p.vatRegistrationNumber ? "positive" : "information"}>{p.vatRegistrationNumber ? t("companies.vatRegistered") : t("companies.notVatRegistered")}</ObjectStatus>}
      kpis={[
        ...(p.type !== "Supplier" ? [{ label: t("tiles.owedToUs"), value: formatMoney(open("Sales")) }] : []),
        ...(p.type !== "Customer" ? [{ label: t("tiles.weOwe"), value: formatMoney(open("Purchase")) }] : []),
        { label: t("inv.overdue"), value: formatMoney(overdue), tone: overdue > 0 ? "negative" : undefined },
        { label: t("nav.invoices"), value: String(list.length) },
      ]}
      actions={!editing && can(me, "core.parties.manage") && <button onClick={() => { setNotice(null); setEditing(true); }}>{t("ui.edit")}</button>}
      footer={editing && <EditFooter form="party-edit" saving={save.isPending} error={save.error?.message} onCancel={() => setEditing(false)}
        saveLabel={t("common.save")} cancelLabel={t("common.cancel")} />}
      messages={notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
      sections={[
        { id: "general", title: t("ui.general"), content: editing ? (
          <form id="party-edit" className="form-columns" onSubmit={onSave}>
            <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" defaultValue={p.name} required /></label>
            <label>{t("fields.type")}
              <select name="type" defaultValue={p.type}>{(["Customer", "Supplier", "Both"] as const).map((k) => <option key={k} value={k}>{t(`inv.partyType.${k}`)}</option>)}</select>
            </label>
            <label>{t("fields.tin")}<input name="tin" inputMode="numeric" pattern="\d{10}" title={t("inv.tinHint")} defaultValue={p.tin ?? ""} /></label>
            <label>{t("inv.vatNumber")}<input name="vatRegistrationNumber" defaultValue={p.vatRegistrationNumber ?? ""} /></label>
            <label>{t("inv.phone")}<input name="phone" type="tel" defaultValue={p.phone ?? ""} /></label>
            <p className="muted small" style={{ gridColumn: "1 / -1" }}>{t("inv.partyEditHint")}</p>
          </form>
        ) : (
          <Fields items={[
            { label: t("fields.name"), value: p.name },
            { label: t("fields.type"), value: t(`inv.partyType.${p.type}`) },
            { label: t("fields.tin"), value: p.tin && <span className="mono">{p.tin}</span> },
            { label: t("inv.vatNumber"), value: p.vatRegistrationNumber },
            { label: t("inv.phone"), value: p.phone },
          ]} />
        ) },
        { id: "invoices", title: t("nav.invoices"), hidden: !can(me, "accounting.invoices.read"), content: list.length ? (
          <table className="table">
            <thead><tr><th>{t("fields.number")}</th><th>{t("fields.type")}</th><th>{t("attendance.date")}</th><th className="num">{t("inv.payable")}</th><th className="num">{t("inv.outstanding")}</th><th>{t("fields.status")}</th></tr></thead>
            <tbody>{list.map((i) => {
              const state = paymentState(i, today);
              return (
                <tr key={i.id} className="navigable" onClick={() => void navigate({ to: "/accounting/invoices/$id", params: { id: i.id } })}>
                  <td className="mono strong-link">{i.number ?? t("status.Draft")}</td>
                  <td>{i.isReturn ? t(i.kind === "Sales" ? "inv.creditNote" : "inv.debitNote") : t(`inv.kind.${i.kind}`)}</td>
                  <td>{formatDate(i.postingDate, me.calendar, am)}</td>
                  <td className="num">{formatMoney(i.payableAmount)}</td>
                  <td className="num">{i.status === "Submitted" && !i.isReturn ? formatMoney(i.outstanding) : ""}</td>
                  <td><ObjectStatus tone={state.tone}>{t(state.key)}</ObjectStatus></td>
                </tr>
              );
            })}</tbody>
          </table>
        ) : <div className="empty">{invoices.isPending ? t("common.loading") : t("inv.noneFound")}</div> },
      ]}
    />
  );
}
