/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  /** Short commit hash of the running build, injected at Docker build time. */
  readonly VITE_BUILD_ID?: string
  /** UTC build date (YYYY-MM-DD) of the running build, injected at Docker build time. */
  readonly VITE_BUILD_DATE?: string
  /** Optional URL to the Kibana logs UI; when set, a "Logs" link appears in the footer. */
  readonly VITE_KIBANA_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
