/** Parses a raw provider payload once, shared by anything that needs to read it — a pretty-printer,
 * a status badge, whatever. Malformed JSON is not an error case to throw on: a malformed payload is
 * precisely the thing an archive view exists to reveal. */
export function tryParseJson(rawJson: string): { value: unknown; malformed: boolean } {
  try {
    return { value: JSON.parse(rawJson), malformed: false }
  } catch {
    return { value: undefined, malformed: true }
  }
}

/** Pretty-prints a raw provider payload, falling back to the verbatim string when it doesn't parse. */
export function parseJsonPayload(rawJson: string): { text: string; malformed: boolean } {
  const { value, malformed } = tryParseJson(rawJson)
  return { text: malformed ? rawJson : JSON.stringify(value, null, 2), malformed }
}
