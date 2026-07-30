import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import { nextMonth, prevMonth } from './budgetSlice'

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export default function MonthNav() {
  const dispatch = useAppDispatch();
  const { currentYear, currentMonth } = useAppSelector(s => s.budget);

  // Pinned so month navigation stays reachable while the budget list scrolls. `sticky`
  // replaces `relative` — it is also a positioned ancestor, so the absolute "Dollars2"
  // span still anchors here. `bg-bg` is load-bearing rather than inherited decoration:
  // without its own background the budget rows would show through the bar.
  return (
    <div className="sticky top-0 z-20 flex items-center justify-center gap-4 border-b-2 border-divider bg-bg px-4 py-3">
      <span className="absolute left-4 font-heading text-[16px] font-extrabold">Dollars2</span>
      <button onClick={() => dispatch(prevMonth())} className="btn btn-icon btn-secondary" aria-label="Previous month">
        <FontAwesomeIcon icon={faChevronLeft} />
      </button>
      <h2 className="w-50 text-center text-[18px]">{MONTH_NAMES[currentMonth - 1]} {currentYear}</h2>
      <button onClick={() => dispatch(nextMonth())} className="btn btn-icon btn-secondary" aria-label="Next month">
        <FontAwesomeIcon icon={faChevronRight} />
      </button>
    </div>
  );
}
