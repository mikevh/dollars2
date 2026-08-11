import { useCallback, useEffect, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import {
  fetchNewTransactions,
  fetchTrackedTransactions,
  fetchDeletedTransactions,
  fetchPendingTransactions,
  fetchCounts,
  restoreTransaction,
  hardDeleteTransaction,
  setActiveTab,
} from './transactionSlice'
import TransactionRow from './TransactionRow'
import TransactionEditDialog from './TransactionEditDialog'
import { useOutsideMousedown } from '../../hooks/useOutsideMousedown'
import type { TransactionResponse } from '../../types/transaction'

const tabs = [
  { key: 'new' as const, label: 'New' },
  { key: 'tracked' as const, label: 'Tracked' },
  { key: 'deleted' as const, label: 'Deleted' },
  { key: 'pending' as const, label: 'Pending' },
];

interface TransactionPaneProps {
  onBudgetMutate?: () => void
}

export default function TransactionPane({ onBudgetMutate }: TransactionPaneProps)
{
  const dispatch = useAppDispatch();
  const {
    transactions, 
    loading, 
    error, 
    activeTab, 
    counts 
  } = useAppSelector(s => s.transactions);
  const { currentYear, currentMonth } = useAppSelector(s => s.budget);
  const [editingTransaction, setEditingTransaction] = useState<TransactionResponse | null | 'create'>(null);
  const [search, setSearch] = useState('');
  const [listMenuOpen, setListMenuOpen] = useState(false);
  const listMenuRef = useRef<HTMLDivElement>(null);
  useOutsideMousedown(listMenuRef, listMenuOpen, () => setListMenuOpen(false));

  // Clear the search box when the tab changes. Adjusting state during render
  // (rather than in an effect) is React's recommended way to reset state on an
  // input change and avoids a cascading re-render.
  const [searchedTab, setSearchedTab] = useState(activeTab);
  if (searchedTab !== activeTab) {
    setSearchedTab(activeTab);
    setSearch('');
  }

  const query = search.trim().toLowerCase();
  const filteredTransactions = query
    ? transactions.filter(t => {
        const fields = [t.payee, t.description, t.memo, Math.abs(t.amount).toFixed(2)];
        return fields.some(f => f?.toLowerCase().includes(query));
      })
    : transactions;

  const fetchCurrentTab = useCallback(() => {
    dispatch(fetchCounts());
    if (activeTab === 'new') {
      dispatch(fetchNewTransactions());
    } else if (activeTab === 'tracked') {
      const fromDate = new Date();
      fromDate.setMonth(fromDate.getMonth() - 2);
      dispatch(fetchTrackedTransactions({ fromDate: fromDate.toISOString().split('T')[0] }));
    } else if (activeTab === 'deleted') {
      dispatch(fetchDeletedTransactions());
    } else if (activeTab === 'pending') {
      dispatch(fetchPendingTransactions());
    }
  }, [dispatch, activeTab]);

  useEffect(() => {
    fetchCurrentTab();
    // currentYear/currentMonth aren't read by fetchCurrentTab, but navigating to a
    // different budget month has to refresh the counts and the tracked list.
  }, [fetchCurrentTab, currentYear, currentMonth]);

  const handleRestore = async (id: number) => {
    const result = await dispatch(restoreTransaction({ id }));

    if (restoreTransaction.rejected.match(result)) {
      toast.error(result.payload as string);
    }
  };

  const handleHardDelete = async (id: number) => {
    if (!window.confirm('Permanently delete this transaction? This cannot be undone.')) {
      return;
    }
    const result = await dispatch(hardDeleteTransaction({ id }));

    if (hardDeleteTransaction.rejected.match(result)) {
      toast.error(result.payload as string);
    }
  };

  const handleDialogMutate = () => {
    fetchCurrentTab();
    onBudgetMutate?.();
  }

  const activeTabLabel = tabs.find(t => t.key === activeTab)?.label ?? '';

  return (
    <div className="flex h-full flex-col">
      {/* HEADER: list selector dropdown + row count */}
      <div className="flex items-center justify-between border-b-2 border-divider px-4 py-3">
        <div ref={listMenuRef} className="relative">
          <button
            onClick={() => setListMenuOpen(o => !o)}
            aria-haspopup="menu"
            aria-expanded={listMenuOpen}
            className="flex items-center gap-1.5 font-heading text-sm font-extrabold uppercase tracking-wide text-text hover:text-[var(--app-blue)]"
          >
            {activeTabLabel}
            <ChevronIcon className="h-3.5 w-3.5" />
          </button>
          {listMenuOpen && (
            <div
              role="menu"
              className="absolute left-0 top-full z-10 mt-1 w-44 rounded-[var(--radius-control)] bg-[var(--app-card)] py-1.5 shadow-[var(--app-shadow-lg)]"
            >
              {tabs.map((tab) => {
                const count = counts[tab.key]
                return (
                  <button
                    key={tab.key}
                    role="menuitem"
                    onClick={() => { dispatch(setActiveTab(tab.key)); setListMenuOpen(false) }}
                    className="flex h-11 w-full items-center justify-between px-3 text-sm normal-case tracking-normal text-text hover:bg-[var(--app-hover)]"
                  >
                    <span>{tab.label}</span>
                    {count > 0 && (
                      <span className="rounded-full bg-[var(--color-neutral-200)] px-1.5 py-0.5 text-[11px] font-bold leading-none tabular-nums text-[var(--color-neutral-700)]">
                        {count}
                      </span>
                    )}
                  </button>
                )
              })}
            </div>
          )}
        </div>
        <span className="text-xs font-medium tabular-nums text-muted">{filteredTransactions.length}</span>
      </div>
      {/* SEARCH BOX */}
      <div className="border-b border-divider px-4 py-3">
        <input type="text" value={search} onChange={e => setSearch(e.target.value)} placeholder="Search transactions..." className="input" />
      </div>

      {/* TRANSACTION LIST */}
      <div className="flex-1 overflow-y-auto">
        {loading && (
          <div className="py-8 text-center text-sm text-muted">Loading...</div>
        )}

        {!loading && 
        error && (
          <div className="py-8 text-center text-sm text-accent-700">{error}</div>
        )}

        {!loading && 
        !error && 
        filteredTransactions.length === 0 && (
          <div className="py-8 text-center text-sm text-muted">
            {query ? 'No matching transactions' : 'No transactions'}
          </div>
        )}

        {!loading && 
        !error && 
        filteredTransactions.map(t => (
          <TransactionRow
            key={t.id}
            transaction={t}
            draggable={activeTab === 'new'}
            showAssignment={activeTab === 'tracked'}
            onClick={activeTab !== 'pending' ? () => setEditingTransaction(t) : undefined}
            actions={
              // New-tab rows carry no delete control: the row is a drag source, and the edit
              // dialog already owns deleting a transaction.
              activeTab === 'deleted' ? (
                <div className="flex gap-3">
                  <button className="font-heading text-xs font-bold uppercase tracking-wide text-accent hover:text-accent-700"
                    onClick={e => { e.stopPropagation(); handleRestore(t.id) }}>
                      Restore
                  </button>
                  {t.isManual && (
                    <button className="font-heading text-xs font-bold uppercase tracking-wide text-muted hover:text-accent-700"
                      onClick={e => { e.stopPropagation(); handleHardDelete(t.id) }} >
                      Delete
                    </button>
                  )}
                </div>
              ) : undefined
            }
          />
        ))}
      </div>

      {activeTab === 'new' && (
        <div className="border-t-2 border-divider p-2">
          <button
            onClick={() => setEditingTransaction('create')}
            className="font-heading text-sm font-extrabold text-[var(--app-blue)] hover:text-[var(--app-blue-hover)]"
          >
            + Add Transaction
          </button>
        </div>
      )}

      {editingTransaction !== null && (
        <TransactionEditDialog
          transaction={editingTransaction === 'create' ? null : editingTransaction}
          onClose={() => setEditingTransaction(null)}
          onMutate={handleDialogMutate}
        />
      )}
    </div>
  )
}

function ChevronIcon({ className = 'h-4 w-4' }: { className?: string }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className={className}>
      <path fillRule="evenodd" d="M5.22 8.22a.75.75 0 011.06 0L10 11.94l3.72-3.72a.75.75 0 111.06 1.06l-4.25 4.25a.75.75 0 01-1.06 0L5.22 9.28a.75.75 0 010-1.06z" clipRule="evenodd" />
    </svg>
  )
}
