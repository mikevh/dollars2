import { useEffect } from 'react'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import { fetchAccounts } from './accountsSlice'
import type { AccountGroup } from '../../types/account'

/** Fetches the accounts list once if it isn't already loaded, and returns it. A page that only
 * needs one account's name or sourceType (rather than the full accounts list itself) may still
 * be reached directly, without having gone through AccountsPage first. */
export function useEnsureAccountsLoaded(): AccountGroup[] {
  const dispatch = useAppDispatch()
  const groups = useAppSelector((state) => state.accounts.groups)

  useEffect(() => {
    if (groups.length === 0) {
      dispatch(fetchAccounts())
    }
  }, [dispatch, groups.length])

  return groups
}
