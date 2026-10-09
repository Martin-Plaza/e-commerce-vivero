import type { User } from '../api/types'

const LEGACY_TOKEN_KEY = 'gymshop.token'
const USER_KEY = 'gymshop.user'

export const session = {
  user: (): User | null => {
    const value = localStorage.getItem(USER_KEY)
    if (!value) return null
    try { return JSON.parse(value) as User } catch { return null }
  },
  save: (user: User) => {
    localStorage.removeItem(LEGACY_TOKEN_KEY)
    localStorage.setItem(USER_KEY, JSON.stringify(user))
    window.dispatchEvent(new Event('gymshop:session'))
  },
  clear: () => {
    localStorage.removeItem(LEGACY_TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    window.dispatchEvent(new Event('gymshop:session'))
  },
}
