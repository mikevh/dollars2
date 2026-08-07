/** Pretty-prints a raw provider payload, falling back to the verbatim string when it doesn't parse — a
 * malformed payload is precisely the thing this view exists to reveal, so it renders as-is rather than
 * throwing. */
export function parseJsonPayload(rawJson: string): { text: string; malformed: boolean } {
  try {
    return { text: JSON.stringify(JSON.parse(rawJson), null, 2), malformed: false }
  } catch {
    return { text: rawJson, malformed: true }
  }
}
