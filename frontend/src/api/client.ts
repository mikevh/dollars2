export interface ApiResponse<T> {
  data: T | null
  error: ApiError | null
}

export interface ApiError {
  message: string
  code: string
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5062'

let isRefreshing = false
let refreshPromise: Promise<boolean> | null = null

async function attemptRefresh(): Promise<boolean> {
  const refreshToken = localStorage.getItem('refreshToken')
  if (!refreshToken) {
    return false
  }

  try {
    const response = await fetch(`${API_BASE_URL}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })

    if (!response.ok) {
      return false
    }

    const result: ApiResponse<{ token: string; refreshToken: string }> = await response.json()
    if (result.data) {
      localStorage.setItem('token', result.data.token)
      localStorage.setItem('refreshToken', result.data.refreshToken)
      return true
    }
    return false
  } catch {
    return false
  }
}

async function refreshOnce(): Promise<boolean> {
  if (isRefreshing) {
    return refreshPromise!
  }
  isRefreshing = true
  refreshPromise = attemptRefresh().finally(() => {
    isRefreshing = false
    refreshPromise = null
  })
  return refreshPromise
}

function forceLogout() {
  localStorage.removeItem('token')
  localStorage.removeItem('refreshToken')
  window.location.href = '/'
}

export async function apiRequest<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<ApiResponse<T>> {
  const url = `${API_BASE_URL}${endpoint}`

  const buildHeaders = () => {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(options.headers as Record<string, string>),
    }

    const token = localStorage.getItem('token')
    if (token) {
      headers['Authorization'] = `Bearer ${token}`
    }

    return headers
  }

  try {
    let response = await fetch(url, { ...options, headers: buildHeaders() })

    if (response.status === 401 && !endpoint.includes('/api/auth/')) {
      const refreshed = await refreshOnce()
      if (refreshed) {
        response = await fetch(url, { ...options, headers: buildHeaders() })
      } else {
        forceLogout()
        return {
          data: null,
          error: { message: 'Session expired. Please log in again.', code: 'UNAUTHORIZED' },
        }
      }
    }

    if (!response.ok) {
      try {
        const body: ApiResponse<T> = await response.json()
        if (body.error) {
          return body
        }
      } catch {
        // response body wasn't JSON
      }
      return {
        data: null,
        error: { message: `Server error (${response.status})`, code: 'SERVER_ERROR' },
      }
    }

    return await response.json()
  } catch {
    return {
      data: null,
      error: { message: 'Network error. Please try again.', code: 'NETWORK_ERROR' },
    }
  }
}

export const api = {
  get: <T>(endpoint: string) =>
    apiRequest<T>(endpoint, { method: 'GET' }),

  post: <T>(endpoint: string, body?: unknown) =>
    apiRequest<T>(endpoint, {
      method: 'POST',
      body: body ? JSON.stringify(body) : undefined,
    }),

  put: <T>(endpoint: string, body?: unknown) =>
    apiRequest<T>(endpoint, {
      method: 'PUT',
      body: body ? JSON.stringify(body) : undefined,
    }),

  delete: <T>(endpoint: string) =>
    apiRequest<T>(endpoint, { method: 'DELETE' }),
}
