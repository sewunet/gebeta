/** Splits what was typed in a recipients box (commas, semicolons or new lines) into distinct, trimmed names, in the order typed. */
export function parseNames(text: string): string[] {
  const seen = new Set<string>();
  const names: string[] = [];
  for (const part of text.split(/[,;\n]/)) {
    const name = part.trim();
    if (name && !seen.has(name.toLowerCase())) {
      seen.add(name.toLowerCase());
      names.push(name);
    }
  }
  return names;
}

/** The text of the conditions' translation key: ids contain dots and dashes, which a key cannot, so they become underscores. */
export const conditionKey = (id: string) => id.replace(/[.-]/g, "_");
