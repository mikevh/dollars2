import type { ActionReducerMapBuilder, AsyncThunk, Draft } from '@reduxjs/toolkit'

interface StaleGuardedState {
  loading: boolean
  error: string | null
  currentRequestId: string | null
}

export function addStaleGuardedThunkCases<State extends StaleGuardedState, Returned, ThunkArg>(
  builder: ActionReducerMapBuilder<State>,
  thunk: AsyncThunk<Returned, ThunkArg, object>,
  options: {
    onFulfilled: (state: Draft<State>, payload: Returned) => void
    onRejected?: (state: Draft<State>) => void
  }
): void {
  builder
    .addCase(thunk.pending, (state, action) => {
      state.loading = true
      state.error = null
      state.currentRequestId = action.meta.requestId
    })
    .addCase(thunk.fulfilled, (state, action) => {
      if (action.meta.requestId !== state.currentRequestId) {
        return
      }
      state.loading = false
      options.onFulfilled(state, action.payload)
    })
    .addCase(thunk.rejected, (state, action) => {
      if (action.meta.requestId !== state.currentRequestId) {
        return
      }
      state.loading = false
      state.error = action.payload as string
      options.onRejected?.(state)
    })
}
