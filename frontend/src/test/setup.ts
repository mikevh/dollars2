import '@testing-library/jest-dom'

// jsdom under Vitest v4 doesn't provide localStorage, but modules like
// authSlice read it at import time. Provide a minimal in-memory stub so tests
// that import the real store (e.g. Footer.test.tsx) can load.
if (typeof globalThis.localStorage === 'undefined') {
  const store = new Map<string, string>()
  const localStorageStub: Storage = {
    getItem: (key) => (store.has(key) ? store.get(key)! : null),
    setItem: (key, value) => {
      store.set(key, String(value))
    },
    removeItem: (key) => {
      store.delete(key)
    },
    clear: () => {
      store.clear()
    },
    key: (index) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size
    },
  }
  globalThis.localStorage = localStorageStub
}
