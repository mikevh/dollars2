import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import { nextMonth, prevMonth } from './budgetSlice'

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export default function MonthNav()
{
  const dispatch = useAppDispatch();
  const { currentYear, currentMonth } = useAppSelector(s => s.budget);

  // Pinned so month navigation stays reachable while the budget list scrolls. `sticky`
  // replaces `relative` — it is also a positioned ancestor, so the absolute "Dollars2"
  // span still anchors here. `bg-bg` is load-bearing rather than inherited decoration:
  // without its own background the budget rows would show through the bar.
  return (
    <div className="sticky top-0 z-20 flex items-center justify-center gap-4 border-b border-divider bg-[var(--app-bg)] px-7 py-3.5">
      <span className="absolute left-7 font-heading text-[19px] font-extrabold">Dollars2</span>
      <button
        onClick={() => dispatch(prevMonth())}
        className="btn btn-secondary h-10 w-10 bg-[var(--app-card)] p-0"
        aria-label="Previous month"
      >
        <FontAwesomeIcon icon={faChevronLeft} />
      </button>
      <h2 className="w-[250px] text-center text-[21px] font-semibold">{MONTH_NAMES[currentMonth - 1]} {currentYear}</h2>
      <button
        onClick={() => dispatch(nextMonth())}
        className="btn btn-secondary h-10 w-10 bg-[var(--app-card)] p-0"
        aria-label="Next month"
      >
        <FontAwesomeIcon icon={faChevronRight} />
      </button>
    </div>
  );
}
