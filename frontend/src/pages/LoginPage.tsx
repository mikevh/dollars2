import { useForm } from 'react-hook-form'
import { useNavigate, Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { loginThunkAsync } from '../features/auth/authSlice'

interface LoginForm {
  email: string
}

export default function LoginPage() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const { loading } = useAppSelector((state) => state.auth)

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginForm>()

  const onSubmit = async (data: LoginForm) => {
    const result = await dispatch(loginThunkAsync(data.email));
    if (loginThunkAsync.fulfilled.match(result)) {
      navigate('/');
    } else {
      toast.error(result.payload as string);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--app-bg)] p-4 text-text">
      <div className="card flex w-90 flex-col gap-4 pt-9 px-8 pb-8">
        <div>
          <h1 className="mb-1 text-[34px] font-bold leading-tight">Dollars2</h1>
          <p className="text-neutral-700 text-[13px]">Zero-based budgeting, self-hosted.</p>
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
          <button type="submit" disabled={loading} className="btn btn-primary btn-block" >{loading ? 'Waiting for passkey…' : 'Sign in'}</button>
        </form>
        <p className="text-center text-[13px] text-neutral-700">
          Have a registration key? <Link to="/register" className="underline">Register a passkey</Link>
        </p>
      </div>
    </div>
  )
}
