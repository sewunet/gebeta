import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import am from "./am.json";
import en from "./en.json";

// English and Amharic from day one (docs/ethiopia.md). The user's choice is saved on their profile.
void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, am: { translation: am } },
  lng: "en",
  fallbackLng: "en",
  interpolation: { escapeValue: false },
});

export default i18n;
