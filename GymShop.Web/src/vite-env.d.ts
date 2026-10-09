/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_STORE_NAME?: string
  readonly VITE_STORE_DESCRIPTION?: string
  readonly VITE_SITE_URL?: string
  readonly VITE_SEO_IMAGE?: string
  readonly VITE_TWITTER_HANDLE?: string
  readonly VITE_SEO_EXTRA_PATHS?: string
  readonly VITE_INSTAGRAM_URL?: string
  readonly VITE_FACEBOOK_URL?: string
  readonly VITE_SOCIAL_EMAIL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

interface Window {
  google?: {
    accounts: { id: {
      initialize(options: { client_id: string; callback(response: { credential?: string; select_by?: string }): void }): void
      renderButton(element: HTMLElement, options: Record<string, unknown>): void
    } }
  }
}
