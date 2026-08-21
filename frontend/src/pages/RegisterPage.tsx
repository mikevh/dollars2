import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { api } from '../api/client'
import { performPasskeyCreation, type PasskeyOptionsResponse } from '../features/auth/webauthn'

interface RegisterForm {
  email: string
  registrationKey: string
}

export default function RegisterPage() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(false)

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<RegisterForm>()

  const onSubmit = async (data: RegisterForm) => {
    setLoading(true)
    try {
      const optionsResult = await api.post<PasskeyOptionsResponse>('/api/auth/passkey/register/options', data)
      if (optionsResult.error) {
        toast.error(optionsResult.error.message)
        return
      }

      let credentialJson: string
      try {
        credentialJson = await performPasskeyCreation(optionsResult.data!.optionsJson)
      } catch (err) {
        toast.error(err instanceof Error ? err.message : 'Passkey registration failed.')
        return
      }

      const completeResult = await api.post('/api/auth/passkey/register/complete', { credentialJson })
      if (completeResult.error) {
        toast.error(completeResult.error.message)
        return
      }

      toast.success('Passkey registered. Sign in below.')
      navigate('/login')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--app-bg)] p-4 text-text">
      <div className="card flex w-90 flex-col gap-4 pt-9 px-8 pb-8">
        <div>
          <h1 className="mb-1 text-[34px] font-bold leading-tight">Dollars2</h1>
          <p className="text-neutral-700 text-[13px]">Register a passkey for your account.</p>
        </div>
        <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-4">
          <div className="field">
            <label htmlFor="email">Email</label>
            <input id="email" type="email" autoFocus placeholder="you@example.com" className="input"
              {...register('email', {
                              required: 'Email is required',
                              pattern: {
                                value: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
                                message: 'Invalid email address',
                              }}
                          )
              }
            />
            {errors.email && (
              <p className="mt-1 text-[12px] text-accent-700">{errors.email.message}</p>
            )}
          </div>
          <div className="field">
            <label htmlFor="registrationKey">Registration key</label>
            <input id="registrationKey" type="text" placeholder="Given to you out-of-band" className="input"
              {...register('registrationKey', { required: 'Registration key is required' })}
            />
            {errors.registrationKey && (
              <p className="mt-1 text-[12px] text-accent-700">{errors.registrationKey.message}</p>
            )}
          </div>
          <button type="submit" disabled={loading} className="btn btn-primary btn-block" >{loading ? 'Waiting for passkey…' : 'Register passkey'}</button>
        </form>
        <p className="text-center text-[13px] text-neutral-700">
          Already registered? <Link to="/login" className="underline">Sign in</Link>
        </p>
      </div>
    </div>
  )
}
