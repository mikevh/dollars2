/**
 * Money is entered, never rounded for the user: the columns hold cents and the API rejects
 * anything finer (issue #110), so a money input refuses the keystroke instead of quietly
 * changing the number on save.
 *
 * Matches a *draft* — what a partially typed entry looks like on its way to a valid amount, so
 * '' and a bare trailing '.' pass — with at most two decimal places. No sign: expense vs income
 * is a separate choice, and amounts are entered as magnitudes.
 */
export function isMoneyDraft(value: string): boolean {
  return /^\d*(\.\d{0,2})?$/.test(value)
}
