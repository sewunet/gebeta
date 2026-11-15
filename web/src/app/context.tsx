import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { api, put } from "../lib/api";
import type { CompanyDto, MeDto, ModuleDto } from "../lib/types";

type Session = {
  me: MeDto;
  modules: ModuleDto[];
  companies: CompanyDto[];
  company: CompanyDto | undefined;
  setCompanyId: (id: string) => void;
  savePreferences: (p: { language?: "en" | "am"; calendar?: MeDto["calendar"] }) => void;
};

const SessionContext = createContext<Session | null>(null);

const COMPANY_KEY = "gebeta.company";

/** Loads the signed-in user, installed modules and accessible companies, and applies the user's language. */
export function SessionProvider({ children }: { children: ReactNode }) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const me = useQuery({ queryKey: ["me"], queryFn: () => api<MeDto>("/api/me") });
  const modules = useQuery({ queryKey: ["modules"], queryFn: () => api<{ modules: ModuleDto[] }>("/api/modules") });
  const companies = useQuery({ queryKey: ["companies"], queryFn: () => api<CompanyDto[]>("/api/core/companies"), retry: false });
  const [companyId, setCompanyIdState] = useState<string | null>(() => {
    try {
      return localStorage.getItem(COMPANY_KEY);
    } catch {
      return null;
    }
  });

  useEffect(() => {
    if (me.data && i18n.language !== me.data.language) void i18n.changeLanguage(me.data.language);
    document.documentElement.lang = me.data?.language ?? "en";
  }, [me.data, i18n]);

  const savePreferences = useMutation({
    mutationFn: (p: { language?: "en" | "am"; calendar?: MeDto["calendar"] }) =>
      put<MeDto>("/api/me/preferences", {
        language: p.language ?? me.data!.language,
        calendar: p.calendar ?? me.data!.calendar,
        defaultCompanyId: me.data!.defaultCompanyId ?? null,
      }),
    onSuccess: (updated) => queryClient.setQueryData(["me"], updated),
  });

  if (me.isPending || modules.isPending || companies.isPending) return <div className="center-screen">{t("common.loading")}</div>;
  if (me.isError) return <div className="center-screen">{t("common.error")}: {me.error.message}</div>;

  const list = companies.data ?? [];
  const company = list.find((c) => c.id === companyId) ?? list.find((c) => c.id === me.data.defaultCompanyId) ?? list[0];
  const setCompanyId = (id: string) => {
    setCompanyIdState(id);
    try {
      localStorage.setItem(COMPANY_KEY, id);
    } catch {
      // storage not available: the choice lasts until reload
    }
  };

  return (
    <SessionContext.Provider
      value={{ me: me.data, modules: modules.data?.modules ?? [], companies: list, company, setCompanyId, savePreferences: savePreferences.mutate }}
    >
      {children}
    </SessionContext.Provider>
  );
}

export function useSession(): Session {
  const session = useContext(SessionContext);
  if (!session) throw new Error("useSession outside SessionProvider");
  return session;
}
