import { formatCurrency } from '../../utils/format'

interface FlowAmountProps {
  /** The Spent (expense) or Received (income) figure to display. */
  value: number
  isIncome: boolean
  /** Color class used for the normal, non-signed case (differs per surface). */
  neutralClass: string
  /** Layout/typography classes for the wrapping span. */
  className?: string
}

/**
 * Renders a Spent/Received figure with an explicit sign and semantic color based
 * on flow direction (issue #80). Both `net = income − expense` conventions hold:
 * on an expense item `value` is spentAmount (= −net), on income it's receivedAmount
 * (= net).
 *
 *  - Expense with Spent < 0  → money flowed *in* (credit): `+$X.XX` in green.
 *  - Income with Received < 0 → money flowed *out* (adverse): `-$X.XX` in red,
 *    reusing the existing negative convention (NOT green).
 *  - Otherwise (normal spend/income): neutral, no forced sign.
 *
 * Mirrors the signed rollover pattern (abs value + manual +/- prefix).
 */
export default function FlowAmount({ value, isIncome, neutralClass, className = '' }: FlowAmountProps) {
  let text: string
  let toneClass: string
  if (value < 0 && isIncome) {
    text = `-${formatCurrency(Math.abs(value))}`
    toneClass = 'text-accent-700'
  } else if (value < 0) {
    text = `+${formatCurrency(Math.abs(value))}`
    toneClass = 'text-positive'
  } else {
    text = formatCurrency(value)
    toneClass = neutralClass
  }
  return <span className={`${className} ${toneClass}`}>{text}</span>
}
